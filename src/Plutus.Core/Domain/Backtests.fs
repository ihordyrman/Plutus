namespace Plutus.Core.Domain

open System

type BacktestStatus =
    | Pending = 0
    | Running = 1
    | Completed = 2
    | Failed = 3
    | Cancelled = 4

type BacktestRunId = private BacktestRunId of int

module BacktestRunId =
    let create (id: int) : Result<BacktestRunId, string> =
        if id <= 0 then
            Error "Backtest run ID must be a positive integer."
        else
            Ok(BacktestRunId id)

    let value (BacktestRunId id) = id

type BacktestRun =
    { Id: BacktestRunId
      PipelineId: PipelineId
      Status: BacktestStatus
      StartDate: DateTime
      EndDate: DateTime
      IntervalMinutes: PositiveInt
      InitialCapital: PositiveDecimal
      FinalCapital: decimal option
      TotalTrades: PositiveInt
      WinRate: decimal option
      MaxDrawdown: decimal option
      SharpeRatio: decimal option
      ErrorMessage: string option
      CreatedAt: DateTime
      CompletedAt: DateTime option }

type BacktestTrade =
    { BacktestRunId: BacktestRunId
      Side: OrderSide
      Price: decimal
      Quantity: decimal
      Fee: decimal
      CandleTime: DateTime
      Capital: decimal }

type BacktestEquityPoint =
    { BacktestRunId: BacktestRunId
      CandleTime: DateTime
      Equity: decimal
      Drawdown: decimal }

type BacktestExecutionLog =
    { BacktestRunId: BacktestRunId
      ExecutionId: ExecutionId
      StepTypeKey: StepTypeKey
      Outcome: StepOutcome
      Message: NonEmptyString
      Context: string option
      CandleTime: DateTime
      StartTime: DateTime
      EndTime: DateTime }

type BacktestExecutionSummary =
    { ExecutionId: ExecutionId
      CandleTime: DateTime
      StepCount: PositiveInt
      MaxOutcome: StepOutcome }

type BacktestConfig =
    { PipelineId: PipelineId
      StartDate: DateTime
      EndDate: DateTime
      IntervalMinutes: PositiveInt
      InitialCapital: PositiveDecimal }
