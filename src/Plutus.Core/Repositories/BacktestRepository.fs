namespace Plutus.Core.Repositories

open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module BacktestRepository =

    let createRun (db: IDbConnection) (config: BacktestConfig) (token: CancellationToken) =
        task {
            try
                let! id =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO backtest_runs (pipeline_id, status, start_date, end_date, interval_minutes, initial_capital, created_at)
                               VALUES (@PipelineId, @Status, @StartDate, @EndDate, @IntervalMinutes, @InitialCapital, now())
                               RETURNING id""",
                            {| PipelineId = config.PipelineId
                               Status = int BacktestStatus.Pending
                               StartDate = config.StartDate
                               EndDate = config.EndDate
                               IntervalMinutes = config.IntervalMinutes
                               InitialCapital = config.InitialCapital |},
                            cancellationToken = token
                        )
                    )

                return Ok id
            with ex ->
                return Error(Unexpected ex)
        }

    let updateRunResults (db: IDbConnection) (run: BacktestRun) (token: CancellationToken) =
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
                            {| Id = run.Id
                               Status = int run.Status
                               FinalCapital = run.FinalCapital
                               TotalTrades = run.TotalTrades
                               WinRate = run.WinRate
                               MaxDrawdown = run.MaxDrawdown
                               SharpeRatio = run.SharpeRatio
                               ErrorMessage = run.ErrorMessage
                               CompletedAt = run.CompletedAt |},
                            cancellationToken = token
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let getRunById (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! run =
                    db.QueryFirstOrDefaultAsync<BacktestRun>(
                        CommandDefinition(
                            "SELECT * FROM backtest_runs WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                match box run with
                | null -> return Ok None
                | _ -> return Ok(Some run)
            with ex ->
                return Error(Unexpected ex)
        }

    let getRunsByPipeline (db: IDbConnection) (pipelineId: int) (token: CancellationToken) =
        task {
            try
                let! runs =
                    db.QueryAsync<BacktestRun>(
                        CommandDefinition(
                            "SELECT * FROM backtest_runs WHERE pipeline_id = @PipelineId ORDER BY created_at DESC",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                return Ok(runs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let insertTrades (db: IDbConnection) (trades: BacktestTrade list) (token: CancellationToken) =
        task {
            try
                let parameters =
                    trades
                    |> List.map (fun t ->
                        {| BacktestRunId = t.BacktestRunId
                           Side = int t.Side
                           Price = t.Price
                           Quantity = t.Quantity
                           Fee = t.Fee
                           CandleTime = t.CandleTime
                           Capital = t.Capital |}
                    )

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

    let getTradesByRun (db: IDbConnection) (runId: int) (token: CancellationToken) =
        task {
            try
                let! trades =
                    db.QueryAsync<BacktestTrade>(
                        CommandDefinition(
                            "SELECT * FROM backtest_trades WHERE backtest_run_id = @RunId ORDER BY candle_time ASC",
                            {| RunId = runId |},
                            cancellationToken = token
                        )
                    )

                return Ok(trades |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let insertEquityPoints (db: IDbConnection) (points: BacktestEquityPoint list) (token: CancellationToken) =
        task {
            try
                let parameters =
                    points
                    |> List.map (fun p ->
                        {| BacktestRunId = p.BacktestRunId
                           CandleTime = p.CandleTime
                           Equity = p.Equity
                           Drawdown = p.Drawdown |}
                    )

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

    let getEquityByRun (db: IDbConnection) (runId: int) (token: CancellationToken) =
        task {
            try
                let! points =
                    db.QueryAsync<BacktestEquityPoint>(
                        CommandDefinition(
                            "SELECT * FROM backtest_equity_points WHERE backtest_run_id = @RunId ORDER BY candle_time ASC",
                            {| RunId = runId |},
                            cancellationToken = token
                        )
                    )

                return Ok(points |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let insertLogs (db: IDbConnection) (logs: BacktestExecutionLog list) (token: CancellationToken) =
        task {
            try
                let parameters =
                    logs
                    |> List.map (fun l ->
                        {| BacktestRunId = l.BacktestRunId
                           ExecutionId = l.ExecutionId
                           StepTypeKey = l.StepTypeKey
                           Outcome = l.Outcome
                           Message = l.Message
                           Context = l.Context
                           CandleTime = l.CandleTime
                           StartTime = l.StartTime
                           EndTime = l.EndTime |}
                    )

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

    let getExecutionSummaries (db: IDbConnection) (runId: int) (offset: int) (limit: int) (token: CancellationToken) =
        task {
            try
                let! summaries =
                    db.QueryAsync<ExecutionSummary>(
                        CommandDefinition(
                            """SELECT execution_id, candle_time, COUNT(*) as step_count, MAX(outcome) as max_outcome
                               FROM backtest_execution_logs
                               WHERE backtest_run_id = @RunId
                               GROUP BY execution_id, candle_time
                               ORDER BY candle_time ASC
                               OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY""",
                            {| RunId = runId; Offset = offset; Limit = limit |},
                            cancellationToken = token
                        )
                    )

                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """SELECT COUNT(DISTINCT execution_id) FROM backtest_execution_logs WHERE backtest_run_id = @RunId""",
                            {| RunId = runId |},
                            cancellationToken = token
                        )
                    )

                return Ok(summaries |> Seq.toList, count)
            with ex ->
                return Error(Unexpected ex)
        }

    let getLogsByRun (db: IDbConnection) (runId: int) (token: CancellationToken) =
        task {
            try
                let! logs =
                    db.QueryAsync<BacktestExecutionLog>(
                        CommandDefinition(
                            """SELECT * FROM backtest_execution_logs
                               WHERE backtest_run_id = @RunId
                               ORDER BY candle_time ASC, id ASC""",
                            {| RunId = runId |},
                            cancellationToken = token
                        )
                    )

                return Ok(logs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getAllRuns (db: IDbConnection) (offset: int) (limit: int) (token: CancellationToken) =
        task {
            try
                let! runs =
                    db.QueryAsync<BacktestRun>(
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

                return Ok(runs |> Seq.toList, count)
            with ex ->
                return Error(Unexpected ex)
        }

    let countRuns (db: IDbConnection) (token: CancellationToken) =
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

    let deleteRun (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """DELETE FROM backtest_execution_logs WHERE backtest_run_id = @Id;
                               DELETE FROM backtest_trades WHERE backtest_run_id = @Id;
                               DELETE FROM backtest_equity_points WHERE backtest_run_id = @Id;
                               DELETE FROM backtest_runs WHERE id = @Id""",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let getLogsByExecution (db: IDbConnection) (runId: int) (executionId: string) (token: CancellationToken) =
        task {
            try
                let! logs =
                    db.QueryAsync<BacktestExecutionLog>(
                        CommandDefinition(
                            """SELECT * FROM backtest_execution_logs
                               WHERE backtest_run_id = @RunId AND execution_id = @ExecutionId
                               ORDER BY id ASC""",
                            {| RunId = runId; ExecutionId = executionId |},
                            cancellationToken = token
                        )
                    )

                return Ok(logs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }
