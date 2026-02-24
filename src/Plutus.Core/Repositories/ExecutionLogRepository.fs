namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
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
    { ExecutionId: string; StartTime: DateTime; EndTime: DateTime; StepCount: int; WorstOutcome: int }

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

    let getFilteredExecutionsByPipelineId
        (db: IDbConnection)
        (pipelineId: int)
        (outcome: int option)
        (dateFrom: DateTime option)
        (dateTo: DateTime option)
        (skip: int)
        (take: int)
        (token: CancellationToken)
        =
        task {
            try
                let havingClauses = ResizeArray<string>()
                let parameters = DynamicParameters()
                parameters.Add("PipelineId", pipelineId)
                parameters.Add("Skip", skip)
                parameters.Add("Take", take)

                match outcome with
                | Some o ->
                    havingClauses.Add("MAX(outcome) = @Outcome")
                    parameters.Add("Outcome", o)
                | None -> ()

                match dateFrom with
                | Some d ->
                    havingClauses.Add("MIN(start_time) >= @DateFrom")
                    parameters.Add("DateFrom", d)
                | None -> ()

                match dateTo with
                | Some d ->
                    havingClauses.Add("MAX(end_time) <= @DateTo")
                    parameters.Add("DateTo", d.Date.AddDays(1.0))
                | None -> ()

                let havingSql = if havingClauses.Count > 0 then " HAVING " + String.Join(" AND ", havingClauses) else ""

                let sql =
                    $"""SELECT execution_id, MIN(start_time) as start_time, MAX(end_time) as end_time,
                               COUNT(*) as step_count, MAX(outcome) as worst_outcome
                        FROM execution_logs WHERE pipeline_id = @PipelineId
                        GROUP BY execution_id{havingSql} ORDER BY MIN(start_time) DESC
                        OFFSET @Skip LIMIT @Take"""

                let! rows =
                    db.QueryAsync<ExecutionSummaryRow>(CommandDefinition(sql, parameters, cancellationToken = token))

                return Ok(rows |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let countFilteredExecutionsByPipelineId
        (db: IDbConnection)
        (pipelineId: int)
        (outcome: int option)
        (dateFrom: DateTime option)
        (dateTo: DateTime option)
        (token: CancellationToken)
        =
        task {
            try
                let havingClauses = ResizeArray<string>()
                let parameters = DynamicParameters()
                parameters.Add("PipelineId", pipelineId)

                match outcome with
                | Some o ->
                    havingClauses.Add("MAX(outcome) = @Outcome")
                    parameters.Add("Outcome", o)
                | None -> ()

                match dateFrom with
                | Some d ->
                    havingClauses.Add("MIN(start_time) >= @DateFrom")
                    parameters.Add("DateFrom", d)
                | None -> ()

                match dateTo with
                | Some d ->
                    havingClauses.Add("MAX(end_time) <= @DateTo")
                    parameters.Add("DateTo", d.Date.AddDays(1.0))
                | None -> ()

                let havingSql = if havingClauses.Count > 0 then " HAVING " + String.Join(" AND ", havingClauses) else ""

                let sql =
                    $"""SELECT COUNT(*) FROM (
                            SELECT execution_id
                            FROM execution_logs WHERE pipeline_id = @PipelineId
                            GROUP BY execution_id{havingSql}
                        ) sub"""

                let! count = db.ExecuteScalarAsync<int>(CommandDefinition(sql, parameters, cancellationToken = token))

                return Ok count
            with ex ->
                return Error(Unexpected ex)
        }
