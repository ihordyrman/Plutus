namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private ExecutionLogEntity =
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
type private ExecutionSummaryEntity =
    { ExecutionId: string
      StartTime: DateTime
      EndTime: DateTime
      StepCount: int
      WorstOutcome: int }

module ExecutionLogs =
    let private toExecutionLog (e: ExecutionLogEntity) : Result<ExecutionLog, string> =
        match
            PipelineId.create e.PipelineId,
            ExecutionId.create e.ExecutionId,
            StepTypeKey.create e.StepTypeKey,
            StepOutcome.fromInt e.Outcome,
            NonEmptyString.create e.Message
        with
        | Ok pId, Ok exId, Ok stk, Ok outcome, Ok msg ->
            Ok
                { PipelineId = pId
                  ExecutionId = exId
                  StepTypeKey = stk
                  Outcome = outcome
                  Message = msg
                  ContextSnapshot = if String.IsNullOrEmpty e.ContextSnapshot then None else Some e.ContextSnapshot
                  StartTime = e.StartTime
                  EndTime = e.EndTime }
        | Error e, _, _, _, _ -> Error $"Invalid pipeline ID: {e}"
        | _, Error e, _, _, _ -> Error $"Invalid execution ID: {e}"
        | _, _, Error e, _, _ -> Error $"Invalid step type key: {e}"
        | _, _, _, Error e, _ -> Error $"Invalid outcome: {e}"
        | _, _, _, _, Error e -> Error $"Invalid message: {e}"

    let private toExecutionSummary (e: ExecutionSummaryEntity) : Result<ExecutionSummary, string> =
        match ExecutionId.create e.ExecutionId, PositiveInt.create e.StepCount, StepOutcome.fromInt e.WorstOutcome with
        | Ok exId, Ok stepCount, Ok outcome ->
            Ok
                { ExecutionId = exId
                  StartTime = e.StartTime
                  EndTime = e.EndTime
                  StepCount = stepCount
                  WorstOutcome = outcome }
        | Error e, _, _ -> Error $"Invalid execution ID: {e}"
        | _, Error e, _ -> Error $"Invalid step count: {e}"
        | _, _, Error e -> Error $"Invalid outcome: {e}"

    let private buildHavingClause (filter: ExecutionLogFilter) (parameters: DynamicParameters) =
        let clauses = ResizeArray<string>()

        match filter.Outcome with
        | Some outcome ->
            clauses.Add("MAX(outcome) = @Outcome")
            parameters.Add("Outcome", StepOutcome.toInt outcome)
        | None -> ()

        match filter.DateFrom with
        | Some d ->
            clauses.Add("MIN(start_time) >= @DateFrom")
            parameters.Add("DateFrom", d)
        | None -> ()

        match filter.DateTo with
        | Some d ->
            clauses.Add("MAX(end_time) <= @DateTo")
            parameters.Add("DateTo", d.Date.AddDays(1.0))
        | None -> ()

        if clauses.Count > 0 then
            " HAVING " + String.Join(" AND ", clauses)
        else
            ""

    let getByExecutionId (db: IDbConnection) : GetByExecutionId =
        fun executionId token ->
            task {
                try
                    let! logs =
                        db.QueryAsync<ExecutionLogEntity>(
                            CommandDefinition(
                                """SELECT id, pipeline_id, execution_id, step_type_key, outcome, message,
                                          context as context_snapshot, start_time, end_time
                                   FROM execution_logs WHERE execution_id = @ExecutionId ORDER BY id ASC""",
                                {| ExecutionId = ExecutionId.value executionId |},
                                cancellationToken = token
                            )
                        )

                    let results =
                        logs
                        |> Seq.toList
                        |> List.map toExecutionLog
                        |> List.choose (function Ok x -> Some x | Error _ -> None)

                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let getFilteredExecutions (db: IDbConnection) : GetFilteredExecutions =
        fun filter skip take token ->
            task {
                try
                    let parameters = DynamicParameters()
                    parameters.Add("PipelineId", PipelineId.value filter.PipelineId)
                    parameters.Add("Skip", skip)
                    parameters.Add("Take", take)

                    let havingSql = buildHavingClause filter parameters

                    let sql =
                        $"""SELECT execution_id, MIN(start_time) as start_time, MAX(end_time) as end_time,
                                   COUNT(*) as step_count, MAX(outcome) as worst_outcome
                            FROM execution_logs WHERE pipeline_id = @PipelineId
                            GROUP BY execution_id{havingSql} ORDER BY MIN(start_time) DESC
                            OFFSET @Skip LIMIT @Take"""

                    let! rows =
                        db.QueryAsync<ExecutionSummaryEntity>(CommandDefinition(sql, parameters, cancellationToken = token))

                    let results =
                        rows
                        |> Seq.toList
                        |> List.map toExecutionSummary
                        |> List.choose (function Ok x -> Some x | Error _ -> None)

                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let countFilteredExecutions (db: IDbConnection) : CountFilteredExecutions =
        fun filter token ->
            task {
                try
                    let parameters = DynamicParameters()
                    parameters.Add("PipelineId", PipelineId.value filter.PipelineId)

                    let havingSql = buildHavingClause filter parameters

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
