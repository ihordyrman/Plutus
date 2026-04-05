namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private BacktestRunEntity =
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
type private BacktestTradeEntity =
    { Id: int
      BacktestRunId: int
      Side: OrderSide
      Price: decimal
      Quantity: decimal
      Fee: decimal
      CandleTime: DateTime
      Capital: decimal }

[<CLIMutable>]
type private BacktestEquityPointEntity =
    { Id: int
      BacktestRunId: int
      CandleTime: DateTime
      Equity: decimal
      Drawdown: decimal }

[<CLIMutable>]
type private BacktestExecutionLogEntity =
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
type private BacktestExecutionSummaryEntity =
    { ExecutionId: string
      CandleTime: DateTime
      StepCount: int
      MaxOutcome: int }

module Backtests =
    let private toBacktestRun (e: BacktestRunEntity) : Result<BacktestRun, string> =
        match
            BacktestRunId.create e.Id,
            PipelineId.create e.PipelineId,
            PositiveInt.create e.IntervalMinutes,
            PositiveDecimal.create e.InitialCapital
        with
        | Ok id, Ok pId, Ok interval, Ok capital ->
            Ok
                { Id = id
                  PipelineId = pId
                  Status = e.Status
                  StartDate = e.StartDate
                  EndDate = e.EndDate
                  IntervalMinutes = interval
                  InitialCapital = capital
                  FinalCapital = e.FinalCapital
                  TotalTrades = e.TotalTrades
                  WinRate = e.WinRate
                  MaxDrawdown = e.MaxDrawdown
                  SharpeRatio = e.SharpeRatio
                  ErrorMessage = if String.IsNullOrEmpty e.ErrorMessage then None else Some e.ErrorMessage
                  CreatedAt = e.CreatedAt
                  CompletedAt = e.CompletedAt }
        | Error e, _, _, _ -> Error $"Invalid backtest run ID: {e}"
        | _, Error e, _, _ -> Error $"Invalid pipeline ID: {e}"
        | _, _, Error e, _ -> Error $"Invalid interval minutes: {e}"
        | _, _, _, Error e -> Error $"Invalid initial capital: {e}"

    let private toBacktestTrade (e: BacktestTradeEntity) : Result<BacktestTrade, string> =
        match BacktestRunId.create e.BacktestRunId with
        | Ok runId ->
            Ok
                { BacktestRunId = runId
                  Side = e.Side
                  Price = e.Price
                  Quantity = e.Quantity
                  Fee = e.Fee
                  CandleTime = e.CandleTime
                  Capital = e.Capital }
        | Error err -> Error $"Invalid backtest run ID: {err}"

    let private toBacktestEquityPoint (e: BacktestEquityPointEntity) : Result<BacktestEquityPoint, string> =
        match BacktestRunId.create e.BacktestRunId with
        | Ok runId ->
            Ok
                { BacktestRunId = runId
                  CandleTime = e.CandleTime
                  Equity = e.Equity
                  Drawdown = e.Drawdown }
        | Error err -> Error $"Invalid backtest run ID: {err}"

    let private toBacktestExecutionLog (e: BacktestExecutionLogEntity) : Result<BacktestExecutionLog, string> =
        match
            BacktestRunId.create e.BacktestRunId,
            ExecutionId.create e.ExecutionId,
            StepTypeKey.create e.StepTypeKey,
            StepOutcome.fromInt e.Outcome,
            NonEmptyString.create e.Message
        with
        | Ok runId, Ok exId, Ok stk, Ok outcome, Ok msg ->
            Ok
                { BacktestRunId = runId
                  ExecutionId = exId
                  StepTypeKey = stk
                  Outcome = outcome
                  Message = msg
                  Context = if String.IsNullOrEmpty e.Context then None else Some e.Context
                  CandleTime = e.CandleTime
                  StartTime = e.StartTime
                  EndTime = e.EndTime }
        | Error e, _, _, _, _ -> Error $"Invalid backtest run ID: {e}"
        | _, Error e, _, _, _ -> Error $"Invalid execution ID: {e}"
        | _, _, Error e, _, _ -> Error $"Invalid step type key: {e}"
        | _, _, _, Error e, _ -> Error $"Invalid outcome: {e}"
        | _, _, _, _, Error e -> Error $"Invalid message: {e}"

    let private toBacktestExecutionSummary (e: BacktestExecutionSummaryEntity) : Result<BacktestExecutionSummary, string> =
        match ExecutionId.create e.ExecutionId, PositiveInt.create e.StepCount, StepOutcome.fromInt e.MaxOutcome with
        | Ok exId, Ok stepCount, Ok outcome ->
            Ok
                { ExecutionId = exId
                  CandleTime = e.CandleTime
                  StepCount = stepCount
                  MaxOutcome = outcome }
        | Error e, _, _ -> Error $"Invalid execution ID: {e}"
        | _, Error e, _ -> Error $"Invalid step count: {e}"
        | _, _, Error e -> Error $"Invalid outcome: {e}"

    let createRun (db: IDbConnection) : CreateBacktestRun =
        fun config token ->
            task {
                try
                    let! id =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                """INSERT INTO backtest_runs (pipeline_id, status, start_date, end_date, interval_minutes, initial_capital, created_at)
                                   VALUES (@PipelineId, @Status, @StartDate, @EndDate, @IntervalMinutes, @InitialCapital, now())
                                   RETURNING id""",
                                {| PipelineId = PipelineId.value config.PipelineId
                                   Status = int BacktestStatus.Pending
                                   StartDate = config.StartDate
                                   EndDate = config.EndDate
                                   IntervalMinutes = PositiveInt.create (PositiveInt.value config.IntervalMinutes) |> Result.defaultValue config.IntervalMinutes |> PositiveInt.value
                                   InitialCapital = PositiveDecimal.value config.InitialCapital |},
                                cancellationToken = token
                            )
                        )

                    match BacktestRunId.create id with
                    | Ok runId -> return Ok runId
                    | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let updateRunResults (db: IDbConnection) : UpdateBacktestRunResults =
        fun run token ->
            task {
                try
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE backtest_runs
                                   SET status = @Status, final_capital = @FinalCapital, total_trades = @TotalTrades,
                                       win_rate = @WinRate, max_drawdown = @MaxDrawdown, sharpe_ratio = @SharpeRatio,
                                       error_message = @ErrorMessage, completed_at = @CompletedAt
                                   WHERE id = @Id""",
                                {| Id = BacktestRunId.value run.Id
                                   Status = int run.Status
                                   FinalCapital = run.FinalCapital
                                   TotalTrades = run.TotalTrades
                                   WinRate = run.WinRate
                                   MaxDrawdown = run.MaxDrawdown
                                   SharpeRatio = run.SharpeRatio
                                   ErrorMessage = run.ErrorMessage |> Option.toObj
                                   CompletedAt = run.CompletedAt |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getRunById (db: IDbConnection) : GetBacktestRunById =
        fun id token ->
            task {
                try
                    let! run =
                        db.QueryFirstOrDefaultAsync<BacktestRunEntity>(
                            CommandDefinition(
                                "SELECT * FROM backtest_runs WHERE id = @Id",
                                {| Id = BacktestRunId.value id |},
                                cancellationToken = token
                            )
                        )

                    match box run with
                    | null -> return Ok None
                    | _ ->
                        match toBacktestRun run with
                        | Ok r -> return Ok(Some r)
                        | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let getRunsByPipeline (db: IDbConnection) : GetBacktestRunsByPipeline =
        fun pipelineId token ->
            task {
                try
                    let! runs =
                        db.QueryAsync<BacktestRunEntity>(
                            CommandDefinition(
                                "SELECT * FROM backtest_runs WHERE pipeline_id = @PipelineId ORDER BY created_at DESC",
                                {| PipelineId = PipelineId.value pipelineId |},
                                cancellationToken = token
                            )
                        )

                    let results = runs |> Seq.toList |> List.choose (toBacktestRun >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let insertTrades (db: IDbConnection) : InsertBacktestTrades =
        fun trades token ->
            task {
                try
                    let parameters =
                        trades
                        |> List.map (fun t ->
                            {| BacktestRunId = BacktestRunId.value t.BacktestRunId
                               Side = int t.Side
                               Price = t.Price
                               Quantity = t.Quantity
                               Fee = t.Fee
                               CandleTime = t.CandleTime
                               Capital = t.Capital |})

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO backtest_trades (backtest_run_id, side, price, quantity, fee, candle_time, capital)
                                   VALUES (@BacktestRunId, @Side, @Price, @Quantity, @Fee, @CandleTime, @Capital)""",
                                parameters,
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getTradesByRun (db: IDbConnection) : GetBacktestTradesByRun =
        fun runId token ->
            task {
                try
                    let! trades =
                        db.QueryAsync<BacktestTradeEntity>(
                            CommandDefinition(
                                "SELECT * FROM backtest_trades WHERE backtest_run_id = @RunId ORDER BY candle_time ASC",
                                {| RunId = BacktestRunId.value runId |},
                                cancellationToken = token
                            )
                        )

                    let results = trades |> Seq.toList |> List.choose (toBacktestTrade >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let insertEquityPoints (db: IDbConnection) : InsertBacktestEquityPoints =
        fun points token ->
            task {
                try
                    let parameters =
                        points
                        |> List.map (fun p ->
                            {| BacktestRunId = BacktestRunId.value p.BacktestRunId
                               CandleTime = p.CandleTime
                               Equity = p.Equity
                               Drawdown = p.Drawdown |})

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO backtest_equity_points (backtest_run_id, candle_time, equity, drawdown)
                                   VALUES (@BacktestRunId, @CandleTime, @Equity, @Drawdown)""",
                                parameters,
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getEquityByRun (db: IDbConnection) : GetBacktestEquityByRun =
        fun runId token ->
            task {
                try
                    let! points =
                        db.QueryAsync<BacktestEquityPointEntity>(
                            CommandDefinition(
                                "SELECT * FROM backtest_equity_points WHERE backtest_run_id = @RunId ORDER BY candle_time ASC",
                                {| RunId = BacktestRunId.value runId |},
                                cancellationToken = token
                            )
                        )

                    let results = points |> Seq.toList |> List.choose (toBacktestEquityPoint >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let insertLogs (db: IDbConnection) : InsertBacktestLogs =
        fun logs token ->
            task {
                try
                    let parameters =
                        logs
                        |> List.map (fun l ->
                            {| BacktestRunId = BacktestRunId.value l.BacktestRunId
                               ExecutionId = ExecutionId.value l.ExecutionId
                               StepTypeKey = StepTypeKey.value l.StepTypeKey
                               Outcome = StepOutcome.toInt l.Outcome
                               Message = NonEmptyString.value l.Message
                               Context = l.Context |> Option.toObj
                               CandleTime = l.CandleTime
                               StartTime = l.StartTime
                               EndTime = l.EndTime |})

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO backtest_execution_logs
                                   (backtest_run_id, execution_id, step_type_key, outcome, message, context, candle_time, start_time, end_time)
                                   VALUES (@BacktestRunId, @ExecutionId, @StepTypeKey, @Outcome, @Message, @Context, @CandleTime, @StartTime, @EndTime)""",
                                parameters,
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getExecutionSummaries (db: IDbConnection) : GetBacktestExecutionSummaries =
        fun runId offset limit token ->
            task {
                try
                    let! summaries =
                        db.QueryAsync<BacktestExecutionSummaryEntity>(
                            CommandDefinition(
                                """SELECT execution_id, candle_time, COUNT(*) as step_count, MAX(outcome) as max_outcome
                                   FROM backtest_execution_logs
                                   WHERE backtest_run_id = @RunId
                                   GROUP BY execution_id, candle_time
                                   ORDER BY candle_time ASC
                                   OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY""",
                                {| RunId = BacktestRunId.value runId; Offset = offset; Limit = limit |},
                                cancellationToken = token
                            )
                        )

                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                "SELECT COUNT(DISTINCT execution_id) FROM backtest_execution_logs WHERE backtest_run_id = @RunId",
                                {| RunId = BacktestRunId.value runId |},
                                cancellationToken = token
                            )
                        )

                    let results =
                        summaries |> Seq.toList |> List.choose (toBacktestExecutionSummary >> Result.toOption)

                    return Ok(results, count)
                with ex ->
                    return Error(Unexpected ex)
            }

    let getLogsByRun (db: IDbConnection) : GetBacktestLogsByRun =
        fun runId token ->
            task {
                try
                    let! logs =
                        db.QueryAsync<BacktestExecutionLogEntity>(
                            CommandDefinition(
                                """SELECT * FROM backtest_execution_logs
                                   WHERE backtest_run_id = @RunId
                                   ORDER BY candle_time ASC, id ASC""",
                                {| RunId = BacktestRunId.value runId |},
                                cancellationToken = token
                            )
                        )

                    let results = logs |> Seq.toList |> List.choose (toBacktestExecutionLog >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let getAllRuns (db: IDbConnection) : GetAllBacktestRuns =
        fun offset limit token ->
            task {
                try
                    let! runs =
                        db.QueryAsync<BacktestRunEntity>(
                            CommandDefinition(
                                "SELECT * FROM backtest_runs ORDER BY created_at DESC OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY",
                                {| Offset = offset; Limit = limit |},
                                cancellationToken = token
                            )
                        )

                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(*) FROM backtest_runs", cancellationToken = token)
                        )

                    let results = runs |> Seq.toList |> List.choose (toBacktestRun >> Result.toOption)
                    return Ok(results, count)
                with ex ->
                    return Error(Unexpected ex)
            }

    let countRuns (db: IDbConnection) : CountBacktestRuns =
        fun token ->
            task {
                try
                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(*) FROM backtest_runs", cancellationToken = token)
                        )

                    return Ok count
                with ex ->
                    return Error(Unexpected ex)
            }

    let deleteRun (db: IDbConnection) : DeleteBacktestRun =
        fun id token ->
            task {
                try
                    let idVal = BacktestRunId.value id

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """DELETE FROM backtest_execution_logs WHERE backtest_run_id = @Id;
                                   DELETE FROM backtest_trades WHERE backtest_run_id = @Id;
                                   DELETE FROM backtest_equity_points WHERE backtest_run_id = @Id;
                                   DELETE FROM backtest_runs WHERE id = @Id""",
                                {| Id = idVal |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getLogsByExecution (db: IDbConnection) : GetBacktestLogsByExecution =
        fun runId executionId token ->
            task {
                try
                    let! logs =
                        db.QueryAsync<BacktestExecutionLogEntity>(
                            CommandDefinition(
                                """SELECT * FROM backtest_execution_logs
                                   WHERE backtest_run_id = @RunId AND execution_id = @ExecutionId
                                   ORDER BY id ASC""",
                                {| RunId = BacktestRunId.value runId; ExecutionId = ExecutionId.value executionId |},
                                cancellationToken = token
                            )
                        )

                    let results = logs |> Seq.toList |> List.choose (toBacktestExecutionLog >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }
