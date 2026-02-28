namespace Plutus.Core.Domain

open System

type BacktestStatus =
    | Pending = 0
    | Running = 1
    | Completed = 2
    | Failed = 3
    | Cancelled = 4

[<CLIMutable>]
type BacktestRun =
    { Id: int
      PipelineId: int
      Status: BacktestStatus
      StartDate: DateTime
      EndDate: DateTime
      IntervalMinutes: int
      InitialCapital: decimal
      FinalCapital: decimal option
      TotalTrades: int
      WinRate: decimal option
      MaxDrawdown: decimal option
      SharpeRatio: decimal option
      ErrorMessage: string
      CreatedAt: DateTime
      CompletedAt: DateTime option }

[<CLIMutable>]
type BacktestTrade =
    { Id: int
      BacktestRunId: int
      Side: OrderSide
      Price: decimal
      Quantity: decimal
      Fee: decimal
      CandleTime: DateTime
      Capital: decimal }

[<CLIMutable>]
type BacktestEquityPoint = { Id: int; BacktestRunId: int; CandleTime: DateTime; Equity: decimal; Drawdown: decimal }

[<CLIMutable>]
type BacktestExecutionLog =
    { Id: int
      BacktestRunId: int
      ExecutionId: string
      StepTypeKey: string
      Outcome: int
      Message: string
      Context: string
      CandleTime: DateTime
      StartTime: DateTime
      EndTime: DateTime }

[<CLIMutable>]
type ExecutionSummary = { ExecutionId: string; CandleTime: DateTime; StepCount: int; MaxOutcome: int }

type BacktestConfig =
    { PipelineId: int
      StartDate: DateTime
      EndDate: DateTime
      IntervalMinutes: int
      InitialCapital: decimal }
