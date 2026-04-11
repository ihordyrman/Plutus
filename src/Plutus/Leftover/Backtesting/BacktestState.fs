namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain
open Plutus.Core.Infrastructure

type BacktestPosition = { EntryPrice: decimal; Quantity: decimal; EntryTime: DateTime; ExecutionId: string }

type SimState =
    { Balance: decimal
      Position: BacktestPosition option
      Trades: BacktestTrade list
      Equity: (DateTime * decimal) list
      TradeCount: int }

    static member empty: SimState = { Balance = 0m; Position = None; Trades = []; Equity = []; TradeCount = 0 }

type BacktestResult =
    { RunId: int
      Metrics: BacktestMetrics
      Trades: BacktestTrade list
      EquityPoints: BacktestEquityPoint list }

module BacktestRun =
    let create runId (config: BacktestConfig) status errorMsg : BacktestRun =
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

    let fromMetrics runId (config: BacktestConfig) (metrics: BacktestMetrics) : BacktestRun =
        { create runId config BacktestStatus.Completed null with
            FinalCapital = Some metrics.FinalCapital
            TotalTrades = metrics.TotalTrades
            WinRate = Some metrics.WinRate
            MaxDrawdown = Some metrics.MaxDrawdownPct
            SharpeRatio = Some metrics.SharpeRatio
            CompletedAt = Some DateTime.UtcNow }

module BacktestExecutionLog =
    let ofLog runId (x: ExecutionLog) : BacktestExecutionLog =
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
