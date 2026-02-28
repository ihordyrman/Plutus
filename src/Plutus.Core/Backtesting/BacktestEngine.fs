namespace Plutus.Core.Backtesting

open System
open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Trading
open Plutus.Core.Repositories

module BacktestEngine =

    type BacktestResult =
        { RunId: int
          Metrics: BacktestMetrics.Metrics
          Trades: BacktestTrade list
          EquityPoints: BacktestEquityPoint list }

    let private forceClosePosition (state: BacktestState.T) (lastPrice: decimal) (candleTime: DateTime) =
        match state.CurrentPosition with
        | Some pos ->
            let proceeds = pos.Quantity * lastPrice
            state.Balance <- state.Balance + proceeds
            state.TradeCounter <- state.TradeCounter + 1
            state.CurrentPosition <- None

            let trade =
                { Id = 0
                  BacktestRunId = 0
                  Side = OrderSide.Sell
                  Price = lastPrice
                  Quantity = pos.Quantity
                  Fee = 0m
                  CandleTime = candleTime
                  Capital = state.Balance }

            state.Trades <- trade :: state.Trades
        | None -> ()

    let private sampleEquityPoints (points: (DateTime * decimal) list) (maxPoints: int) =
        if points.Length <= maxPoints then
            points
        else
            let step = max 1 (points.Length / maxPoints)
            points |> List.indexed |> List.filter (fun (i, _) -> i % step = 0) |> List.map snd

    let run (scopeFactory: IServiceScopeFactory) (runId: int) (config: BacktestConfig) (ct: CancellationToken) =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let! _ =
                BacktestRepository.updateRunResults
                    db
                    { Id = runId
                      PipelineId = config.PipelineId
                      Status = BacktestStatus.Running
                      StartDate = config.StartDate
                      EndDate = config.EndDate
                      IntervalMinutes = config.IntervalMinutes
                      InitialCapital = config.InitialCapital
                      FinalCapital = None
                      TotalTrades = 0
                      WinRate = None
                      MaxDrawdown = None
                      SharpeRatio = None
                      ErrorMessage = null
                      CreatedAt = DateTime.UtcNow
                      CompletedAt = None }
                    ct

            match! PipelineRepository.getById db config.PipelineId ct with
            | Error err -> return Error $"Pipeline not found: {err}"
            | Ok pipeline ->

                match! PipelineStepRepository.getByPipelineId db config.PipelineId ct with
                | Error err -> return Error $"Failed to load pipeline steps: {err}"
                | Ok pipelineSteps ->

                    let stepConfigs =
                        pipelineSteps
                        |> List.map (fun step ->
                            { Builder.StepTypeKey = step.StepTypeKey
                              Builder.Order = step.Order
                              Builder.IsEnabled = step.IsEnabled
                              Builder.Parameters =
                                step.Parameters |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Map.ofSeq }
                        )

                    let state = BacktestState.create config

                    let backtestRegistry =
                        TradingSteps.all (BacktestAdapters.getPosition state) (BacktestAdapters.tradeExecutor state)
                        |> Registry.create

                    match Builder.buildSteps backtestRegistry scope.ServiceProvider stepConfigs with
                    | Error errors ->
                        let msg = errors |> List.map (fun e -> $"{e.StepKey}: {e.Errors}") |> String.concat "; "

                        let! _ =
                            BacktestRepository.updateRunResults
                                db
                                { Id = runId
                                  PipelineId = config.PipelineId
                                  Status = BacktestStatus.Failed
                                  StartDate = config.StartDate
                                  EndDate = config.EndDate
                                  IntervalMinutes = config.IntervalMinutes
                                  InitialCapital = config.InitialCapital
                                  FinalCapital = None
                                  TotalTrades = 0
                                  WinRate = None
                                  MaxDrawdown = None
                                  SharpeRatio = None
                                  ErrorMessage = msg
                                  CreatedAt = DateTime.UtcNow
                                  CompletedAt = Some DateTime.UtcNow }
                                ct

                        return Error $"Failed to build steps: {msg}"

                    | Ok steps when steps.IsEmpty -> return Error "No enabled steps"

                    | Ok steps ->

                        match!
                            CandlestickRepository.query
                                db
                                pipeline.Instrument
                                pipeline.MarketType
                                "1m"
                                (Some config.StartDate)
                                (Some config.EndDate)
                                None
                                ct
                        with
                        | Error err -> return Error $"Failed to load candles: {err}"
                        | Ok candles when candles.IsEmpty -> return Error "No candle data for the specified date range"
                        | Ok candles ->

                            let sortedCandles = candles |> List.sortBy _.Timestamp
                            let intervalSpan = TimeSpan.FromMinutes(float config.IntervalMinutes)
                            let mutable nextExecution = config.StartDate
                            let logs = ResizeArray<ExecutionLog>()
                            let equitySnapshots = ResizeArray<DateTime * decimal>()

                            let logStep (candleTime: DateTime) (log: ExecutionLog) =
                                logs.Add({ log with StartTime = candleTime; EndTime = candleTime })

                            for candle in sortedCandles do
                                if not ct.IsCancellationRequested && candle.Timestamp >= nextExecution then
                                    let ctx =
                                        { TradingContext.empty pipeline.Id pipeline.Instrument pipeline.MarketType with
                                            CurrentPrice = candle.Close }
                                        |> TradingContext.withData "backtest:currentTime" candle.Timestamp

                                    let! _ =
                                        Runner.run
                                            pipeline.Id
                                            ctx.ExecutionId
                                            TradingContext.serializeForLog
                                            (logStep candle.Timestamp)
                                            steps
                                            ctx
                                            ct

                                    let equity =
                                        state.Balance
                                        + (state.CurrentPosition
                                           |> Option.map (fun p -> p.Quantity * candle.Close)
                                           |> Option.defaultValue 0m)

                                    equitySnapshots.Add(candle.Timestamp, equity)
                                    nextExecution <- candle.Timestamp + intervalSpan

                            let lastCandle = sortedCandles |> List.last
                            forceClosePosition state lastCandle.Close lastCandle.Timestamp

                            let trades = state.Trades |> List.rev
                            let sampledEquity = sampleEquityPoints (equitySnapshots |> Seq.toList) 500

                            let backtestTrades = trades |> List.map (fun t -> { t with BacktestRunId = runId })

                            let equityPoints =
                                sampledEquity
                                |> List.map (fun (time, equity) ->
                                    let peak =
                                        sampledEquity
                                        |> List.filter (fun (t, _) -> t <= time)
                                        |> List.map snd
                                        |> List.max

                                    { Id = 0
                                      BacktestRunId = runId
                                      CandleTime = time
                                      Equity = equity
                                      Drawdown = if peak > 0m then (peak - equity) / peak else 0m }
                                )

                            let metrics = BacktestMetrics.calculate config.InitialCapital backtestTrades equityPoints

                            let backtestLogs =
                                logs
                                |> Seq.toList
                                |> List.map (fun l ->
                                    { Id = 0
                                      BacktestRunId = runId
                                      ExecutionId = l.ExecutionId
                                      StepTypeKey = l.StepTypeKey
                                      Outcome =
                                        match l.Outcome with
                                        | StepOutcome.Success -> 0
                                        | StepOutcome.Stopped -> 1
                                        | StepOutcome.Failed -> 2
                                      Message = l.Message
                                      Context = l.ContextSnapshot
                                      CandleTime = l.StartTime
                                      StartTime = l.StartTime
                                      EndTime = l.EndTime }
                                )

                            let! _ = BacktestRepository.insertTrades db backtestTrades ct
                            let! _ = BacktestRepository.insertEquityPoints db equityPoints ct
                            let! _ = BacktestRepository.insertLogs db backtestLogs ct

                            let! _ =
                                BacktestRepository.updateRunResults
                                    db
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
                                    ct

                            return
                                Ok
                                    { RunId = runId
                                      Metrics = metrics
                                      Trades = backtestTrades
                                      EquityPoints = equityPoints }
        }
