namespace Plutus.Core.Backtesting

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Trading
open Plutus.Core.Repositories

module BacktestEngine =

    type LoopAcc = { NextExecution: DateTime; Equity: (DateTime * decimal) list; Logs: ExecutionLog list }

    let inline private orFail result = result |> TaskResult.mapError string

    let finalizePosition (state: SimState ref) (lastPrice: decimal) (candleTime: DateTime) =
        match state.Value.Position with
        | Some pos ->
            let proceeds = pos.Quantity * lastPrice

            let trades =
                { Id = 0
                  BacktestRunId = 0
                  Side = OrderSide.Sell
                  Price = lastPrice
                  Quantity = pos.Quantity
                  Fee = 0m
                  CandleTime = candleTime
                  Capital = state.Value.Balance }
                :: state.Value.Trades

            state.Value <-
                { state.Value with
                    Balance = state.Value.Balance + proceeds
                    Position = None
                    Trades = trades
                    TradeCount = trades.Length }
        | None -> ()

    let sampleEquityPoints (points: (DateTime * decimal) list) (maxPoints: int) =
        if points.Length <= maxPoints then
            points
        else
            let step = max 1 (points.Length / maxPoints)
            points |> List.indexed |> List.filter (fun (x, _) -> x % step = 0) |> List.map snd

    let private runCandle
        (pipeline: Pipeline)
        (steps: Step<TradingContext> list)
        (stateRef: SimState ref)
        (intervalSpan: TimeSpan)
        (ct: CancellationToken)
        (acc: LoopAcc)
        (candle: Candlestick)
        =
        task {
            if ct.IsCancellationRequested || candle.Timestamp < acc.NextExecution then
                return acc
            else
                let ctx =
                    { TradingContext.empty pipeline.Id pipeline.Instrument pipeline.MarketType with
                        CurrentPrice = candle.Close }
                    |> TradingContext.withData "backtest:currentTime" candle.Timestamp

                let logs = ResizeArray()

                let! _ =
                    Runner.run
                        pipeline.Id
                        ctx.ExecutionId
                        TradingContext.serializeForLog
                        (fun log -> logs.Add({ log with StartTime = candle.Timestamp; EndTime = candle.Timestamp }))
                        steps
                        ctx
                        ct

                let state = stateRef.Value

                let posValue =
                    state.Position |> Option.map (fun p -> p.Quantity * candle.Close) |> Option.defaultValue 0m

                return
                    { NextExecution = candle.Timestamp + intervalSpan
                      Equity = (candle.Timestamp, state.Balance + posValue) :: acc.Equity
                      Logs = (logs |> Seq.toList) @ acc.Logs }
        }

    let private simulate
        (pipeline: Pipeline)
        (steps: Step<TradingContext> list)
        (stateRef: SimState ref)
        (config: BacktestConfig)
        (candles: Candlestick list)
        (ct: CancellationToken)
        =
        let intervalSpan = TimeSpan.FromMinutes(float config.IntervalMinutes)
        let seed = { NextExecution = config.StartDate; Equity = []; Logs = [] }
        let runOne = runCandle pipeline steps stateRef intervalSpan ct

        candles
        |> List.fold
            (fun accTask candle ->
                task {
                    let! acc = accTask
                    return! runOne acc candle
                }
            )
            (Task.FromResult seed)

    let private loadPipeline db (config: BacktestConfig) ct =
        taskResult {
            let! pipeline =
                PipelineRepository.getById db config.PipelineId ct
                |> TaskResult.mapError (fun e -> $"Pipeline not found: {e}")

            let! steps =
                PipelineStepRepository.getByPipelineId db config.PipelineId ct
                |> TaskResult.mapError (fun e -> $"Failed to load pipeline steps: {e}")

            return (pipeline, steps)
        }

    let private loadCandles db (pipeline: Pipeline) config ct =
        taskResult {
            let! candles =
                CandlestickRepository.query
                    db
                    pipeline.Instrument
                    pipeline.MarketType
                    Interval.OneMinute
                    (Some config.StartDate)
                    (Some config.EndDate)
                    None
                    ct
                |> TaskResult.mapError (fun e -> $"Failed to load candles: {e}")

            match candles with
            | [] -> return! Error "No candle data for the specified date range"
            | _ -> return candles |> List.sortBy _.Timestamp
        }

    let private toStepConfig (step: PipelineStep) =
        { Builder.StepTypeKey = step.StepTypeKey
          Builder.Order = step.Order
          Builder.IsEnabled = step.IsEnabled
          Builder.Parameters = step.Parameters |> Seq.map (fun x -> x.Key, x.Value) |> Map.ofSeq }

    let private toEquityPoint runId (sampledEquity: (DateTime * decimal) list) (time: DateTime, equity: decimal) =
        let peak = sampledEquity |> List.filter (fun (x, _) -> x <= time) |> List.map snd |> List.max

        { Id = 0
          BacktestRunId = runId
          CandleTime = time
          Equity = equity
          Drawdown = if peak > 0m then (peak - equity) / peak else 0m }

    let run (scopeFactory: IServiceScopeFactory) (runId: int) (config: BacktestConfig) (ct: CancellationToken) =
        taskResult {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let stateRef = ref { SimState.empty with Balance = config.InitialCapital }

            do!
                BacktestRepository.updateRunResults db (BacktestRun.create runId config BacktestStatus.Running null) ct
                |> orFail

            let! pipeline, steps = loadPipeline db config ct
            let stepConfigs = steps |> List.map toStepConfig

            let registry =
                TradingSteps.all (BacktestAdapters.getPosition stateRef) (BacktestAdapters.tradeExecutor stateRef)
                |> Registry.create

            let! steps =
                Builder.buildSteps registry scope.ServiceProvider stepConfigs
                |> Result.mapError (fun errors ->
                    errors |> List.map (fun e -> $"{e.StepKey}: {e.Errors}") |> String.concat "; "
                )

            let! candles = loadCandles db pipeline config ct
            let! loop = simulate pipeline steps stateRef config candles ct
            let lastCandle = List.last candles
            finalizePosition stateRef lastCandle.Close lastCandle.Timestamp

            let trades = stateRef.Value.Trades |> List.rev |> List.map (fun x -> { x with BacktestRunId = runId })

            let sampledEquity = loop.Equity |> List.rev |> sampleEquityPoints <| 500
            let equity = sampledEquity |> List.map (toEquityPoint runId sampledEquity)
            let metrics = BacktestMetrics.calculate config.InitialCapital trades equity

            do! BacktestRepository.insertTrades db trades ct |> orFail
            do! BacktestRepository.insertEquityPoints db equity ct |> orFail

            do!
                BacktestRepository.insertLogs db (loop.Logs |> List.map (BacktestExecutionLog.ofLog runId)) ct
                |> orFail

            do! BacktestRepository.updateRunResults db (BacktestRun.fromMetrics runId config metrics) ct |> orFail

            return { RunId = runId; Metrics = metrics; Trades = trades; EquityPoints = equity }
        }
