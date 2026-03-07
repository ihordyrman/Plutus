namespace Plutus.Core.Backtesting

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Steps

module BacktestSimulator =

    type LoopAcc = { NextExecution: DateTime; Equity: (DateTime * decimal) list; Logs: ExecutionLog list }

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

    let simulate
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
