namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Infrastructure
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type ExecutionLogRow =
    { Id: int
      PipelineId: int
      ExecutionId: string
      StepTypeKey: string
      Outcome: int
      Message: string
      ContextSnapshot: string
      StartTime: DateTime
      EndTime: DateTime }

[<CLIMutable>]
type ExecutionSummaryRow =
    { ExecutionId: string
      StartTime: DateTime
      EndTime: DateTime
      StepCount: int
      WorstOutcome: int }

[<RequireQualifiedAccess>]
module ExecutionLogRepository =

    let getByExecutionId (db: IDbConnection) (executionId: string) (token: CancellationToken) =
        task {
            try
                let! logs =
                    db.QueryAsync<ExecutionLogRow>(
                        CommandDefinition(
                            """SELECT id, pipeline_id, execution_id, step_type_key, outcome, message,
                                      context as context_snapshot, start_time, end_time
                               FROM execution_logs WHERE execution_id = @ExecutionId ORDER BY id ASC""",
                            {| ExecutionId = executionId |},
                            cancellationToken = token
                        )
                    )

                return Ok(logs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getByPipelineId
        (db: IDbConnection)
        (pipelineId: int)
        (skip: int)
        (take: int)
        (token: CancellationToken)
        =
        task {
            try
                let! logs =
                    db.QueryAsync<ExecutionLogRow>(
                        CommandDefinition(
                            """SELECT id, pipeline_id, execution_id, step_type_key, outcome, message,
                                      context as context_snapshot, start_time, end_time
                               FROM execution_logs
                               WHERE pipeline_id = @PipelineId
                               ORDER BY id DESC
                               OFFSET @Skip LIMIT @Take""",
                            {| PipelineId = pipelineId
                               Skip = skip
                               Take = take |},
                            cancellationToken = token
                        )
                    )

                return Ok(logs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getExecutionsByPipelineId
        (db: IDbConnection)
        (pipelineId: int)
        (skip: int)
        (take: int)
        (token: CancellationToken)
        =
        task {
            try
                let! rows =
                    db.QueryAsync<ExecutionSummaryRow>(
                        CommandDefinition(
                            """SELECT execution_id, MIN(start_time) as start_time, MAX(end_time) as end_time,
                                      COUNT(*) as step_count, MAX(outcome) as worst_outcome
                               FROM execution_logs WHERE pipeline_id = @PipelineId
                               GROUP BY execution_id ORDER BY MIN(start_time) DESC
                               OFFSET @Skip LIMIT @Take""",
                            {| PipelineId = pipelineId
                               Skip = skip
                               Take = take |},
                            cancellationToken = token
                        )
                    )

                return Ok(rows |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let countExecutionsByPipelineId (db: IDbConnection) (pipelineId: int) (token: CancellationToken) =
        task {
            try
                let! count =
                    db.ExecuteScalarAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(DISTINCT execution_id) FROM execution_logs WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                return Ok count
            with ex ->
                return Error(Unexpected ex)
        }
