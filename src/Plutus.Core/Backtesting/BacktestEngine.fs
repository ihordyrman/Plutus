namespace Plutus.Core.Backtesting

open System
open System.Data
open System.Threading
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Trading
open Plutus.Core.Repositories
open Plutus.Core.Shared

module BacktestEngine =

    let private emptyRun runId (config: BacktestConfig) status errorMsg =
        { Id = runId
          PipelineId = config.PipelineId
          Status = status
          StartDate = config.StartDate
          EndDate = config.EndDate
          IntervalMinutes = config.IntervalMinutes
          InitialCapital = config.InitialCapital
          FinalCapital = None
          TotalTrades = 0
          WinRate = None
          MaxDrawdown = None
          SharpeRatio = None
          ErrorMessage = errorMsg
          CreatedAt = DateTime.UtcNow
          CompletedAt = None }

    let private runFromMetrics runId (config: BacktestConfig) (metrics: BacktestMetrics) =
        { Id = runId
          PipelineId = config.PipelineId
          Status = BacktestStatus.Completed
          StartDate = config.StartDate
          EndDate = config.EndDate
          IntervalMinutes = config.IntervalMinutes
          InitialCapital = config.InitialCapital
          FinalCapital = Some metrics.FinalCapital
          TotalTrades = metrics.TotalTrades
          WinRate = Some metrics.WinRate
          MaxDrawdown = Some metrics.MaxDrawdownPct
          SharpeRatio = Some metrics.SharpeRatio
          ErrorMessage = null
          CreatedAt = DateTime.UtcNow
          CompletedAt = Some DateTime.UtcNow }

    let private loadPipeline db (config: BacktestConfig) ct =
        taskResult {
            let! pipeline =
                PipelineRepository.getById db config.PipelineId ct
                |> TaskResult.mapError (fun err -> $"Pipeline not found: {err}")

            let! steps =
                PipelineStepRepository.getByPipelineId db config.PipelineId ct
                |> TaskResult.mapError (fun err -> $"Failed to load pipeline steps: {err}")

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

    let private toBacktestLog runId (x: ExecutionLog) =
        { Id = 0
          BacktestRunId = runId
          ExecutionId = x.ExecutionId
          StepTypeKey = x.StepTypeKey
          Outcome =
            match x.Outcome with
            | StepOutcome.Success -> 0
            | StepOutcome.Stopped -> 1
            | StepOutcome.Failed -> 2
          Message = x.Message
          Context = x.ContextSnapshot
          CandleTime = x.StartTime
          StartTime = x.StartTime
          EndTime = x.EndTime }

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

            let! _ =
                BacktestRepository.updateRunResults db (emptyRun runId config BacktestStatus.Running null) ct
                |> TaskResult.mapError string

            let! pipeline, steps =
                loadPipeline db config ct |> TaskResult.mapError (fun e -> $"Failed to load pipeline: {e}")

            let stepConfigs = steps |> List.map toStepConfig
            let stateRef = ref { SimState.empty with Balance = config.InitialCapital }

            let registry =
                TradingSteps.all (BacktestAdapters.getPosition stateRef) (BacktestAdapters.tradeExecutor stateRef)
                |> Registry.create

            let! steps =
                Builder.buildSteps registry scope.ServiceProvider stepConfigs
                |> Result.mapError (fun errors ->
                    errors |> List.map (fun e -> $"{e.StepKey}: {e.Errors}") |> String.concat "; "
                )

            let! candles =
                loadCandles db pipeline config ct |> TaskResult.mapError (fun e -> $"Failed to load candles: {e}")

            let! loop = BacktestSimulator.simulate pipeline steps stateRef config candles ct
            let lastCandle = List.last candles
            BacktestSimulator.finalizePosition stateRef lastCandle.Close lastCandle.Timestamp

            let trades = stateRef.Value.Trades |> List.rev |> List.map (fun x -> { x with BacktestRunId = runId })

            let sampledEquity = loop.Equity |> List.rev |> BacktestSimulator.sampleEquityPoints <| 500
            let equity = sampledEquity |> List.map (toEquityPoint runId sampledEquity)
            let metrics = BacktestMetrics.calculate config.InitialCapital trades equity

            let! _ = BacktestRepository.insertTrades db trades ct |> TaskResult.mapError string
            let! _ = BacktestRepository.insertEquityPoints db equity ct |> TaskResult.mapError string

            let! _ =
                BacktestRepository.insertLogs db (loop.Logs |> List.map (toBacktestLog runId)) ct
                |> TaskResult.mapError string

            let! _ =
                BacktestRepository.updateRunResults db (runFromMetrics runId config metrics) ct
                |> TaskResult.mapError string

            return { RunId = runId; Metrics = metrics; Trades = trades; EquityPoints = equity }
        }
