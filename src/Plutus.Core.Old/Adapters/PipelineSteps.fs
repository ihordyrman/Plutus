namespace Plutus.Core.Adapters

open System
open System.Collections.Generic
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private PipelineStepEntity =
    { Id: int
      PipelineId: int
      StepTypeKey: string
      Name: string
      Order: int
      IsEnabled: bool
      Parameters: Dictionary<string, string>
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module PipelineSteps =
    let private toPipelineStep (e: PipelineStepEntity) : Result<PipelineStep, string> =
        match
            StepId.create e.Id,
            PipelineId.create e.PipelineId,
            StepTypeKey.create e.StepTypeKey,
            NonEmptyString.create e.Name,
            StepOrder.create e.Order
        with
        | Ok id, Ok pId, Ok stk, Ok name, Ok order ->
            Ok
                { Id = id
                  PipelineId = pId
                  StepTypeKey = stk
                  Name = name
                  Order = order
                  IsEnabled = e.IsEnabled
                  Parameters = e.Parameters |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                  CreatedAt = e.CreatedAt
                  UpdatedAt = e.UpdatedAt }
        | Error e, _, _, _, _ -> Error $"Invalid step ID: {e}"
        | _, Error e, _, _, _ -> Error $"Invalid pipeline ID: {e}"
        | _, _, Error e, _, _ -> Error $"Invalid step type key: {e}"
        | _, _, _, Error e, _ -> Error $"Invalid step name: {e}"
        | _, _, _, _, Error e -> Error $"Invalid step order: {e}"

    let private toParams (m: Map<string, string>) =
        let d = Dictionary<string, string>()
        m |> Map.iter (fun k v -> d.[k] <- v)
        d

    let getById (db: IDbConnection) : GetStepById =
        fun id token ->
            task {
                try
                    let! results =
                        db.QueryAsync<PipelineStepEntity>(
                            CommandDefinition(
                                "SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1",
                                {| Id = StepId.value id |},
                                cancellationToken = token
                            )
                        )

                    match results |> Seq.tryHead with
                    | Some entity ->
                        match toPipelineStep entity with
                        | Ok step -> return Ok step
                        | Error e -> return Error(Unexpected(Exception(e)))
                    | None -> return Error(NotFound $"Step with id {StepId.value id}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let getByPipelineId (db: IDbConnection) : GetStepsByPipelineId =
        fun pipelineId token ->
            task {
                try
                    let! results =
                        db.QueryAsync<PipelineStepEntity>(
                            CommandDefinition(
                                "SELECT * FROM pipeline_steps WHERE pipeline_id = @PipelineId ORDER BY \"order\"",
                                {| PipelineId = PipelineId.value pipelineId |},
                                cancellationToken = token
                            )
                        )

                    let steps = results |> Seq.toList |> List.choose (toPipelineStep >> Result.toOption)
                    return Ok steps
                with ex ->
                    return Error(Unexpected ex)
            }

    let create (db: IDbConnection) : CreateStep =
        fun step token ->
            task {
                try
                    let now = DateTime.UtcNow

                    let! id =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                """INSERT INTO pipeline_steps
                                   (pipeline_id, step_type_key, name, "order", is_enabled, parameters, created_at, updated_at)
                                   VALUES (@PipelineId, @StepTypeKey, @Name, @Order, @IsEnabled, @Parameters::jsonb, now(), now())
                                   RETURNING id""",
                                {| PipelineId = PipelineId.value step.PipelineId
                                   StepTypeKey = StepTypeKey.value step.StepTypeKey
                                   Name = NonEmptyString.value step.Name
                                   Order = StepOrder.value step.Order
                                   IsEnabled = step.IsEnabled
                                   Parameters = toParams step.Parameters |},
                                cancellationToken = token
                            )
                        )

                    match StepId.create id with
                    | Ok newId -> return Ok { step with Id = newId; CreatedAt = now; UpdatedAt = now }
                    | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let update (db: IDbConnection) : UpdateStep =
        fun step token ->
            task {
                try
                    let idVal = StepId.value step.Id

                    let! result =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE pipeline_steps
                                   SET step_type_key = @StepTypeKey, name = @Name, "order" = @Order,
                                       is_enabled = @IsEnabled, parameters = @Parameters::jsonb, updated_at = now()
                                   WHERE id = @Id""",
                                {| Id = idVal
                                   StepTypeKey = StepTypeKey.value step.StepTypeKey
                                   Name = NonEmptyString.value step.Name
                                   Order = StepOrder.value step.Order
                                   IsEnabled = step.IsEnabled
                                   Parameters = toParams step.Parameters |},
                                cancellationToken = token
                            )
                        )

                    if result > 0 then
                        let! entity =
                            db.QuerySingleAsync<PipelineStepEntity>(
                                CommandDefinition(
                                    "SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1",
                                    {| Id = idVal |},
                                    cancellationToken = token
                                )
                            )

                        match toPipelineStep entity with
                        | Ok s -> return Ok s
                        | Error e -> return Error(Unexpected(Exception(e)))
                    else
                        return Error(NotFound $"Step with id {idVal}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let delete (db: IDbConnection) : DeleteStep =
        fun id token ->
            task {
                try
                    let idVal = StepId.value id

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "DELETE FROM pipeline_steps WHERE id = @Id",
                                {| Id = idVal |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then return Ok() else return Error(NotFound $"Step with id {idVal}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let deleteByPipelineId (db: IDbConnection) : DeleteStepsByPipelineId =
        fun pipelineId token ->
            task {
                try
                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "DELETE FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                                {| PipelineId = PipelineId.value pipelineId |},
                                cancellationToken = token
                            )
                        )

                    return Ok rowsAffected
                with ex ->
                    return Error(Unexpected ex)
            }

    let setEnabled (db: IDbConnection) : SetStepEnabled =
        fun id enabled token ->
            task {
                try
                    let idVal = StepId.value id
                    let now = DateTime.UtcNow

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE pipeline_steps SET is_enabled = @IsEnabled, updated_at = @UpdatedAt WHERE id = @Id",
                                {| IsEnabled = enabled; UpdatedAt = now; Id = idVal |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then return Ok() else return Error(NotFound $"Step with id {idVal}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let swapOrders (db: IDbConnection) : SwapStepOrders =
        fun step1 step2 token ->
            task {
                try
                    let now = DateTime.UtcNow

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE pipeline_steps
                                   SET "order" = CASE
                                       WHEN id = @Id1 THEN @Order2
                                       WHEN id = @Id2 THEN @Order1
                                   END,
                                   updated_at = @UpdatedAt
                                   WHERE id IN (@Id1, @Id2)""",
                                {| Id1 = StepId.value step1.Id
                                   Order1 = StepOrder.value step1.Order
                                   Id2 = StepId.value step2.Id
                                   Order2 = StepOrder.value step2.Order
                                   UpdatedAt = now |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then
                        return Ok()
                    else
                        return Error(NotFound $"Steps with ids {StepId.value step1.Id} or {StepId.value step2.Id} not found")
                with ex ->
                    return Error(Unexpected ex)
            }

    let getMaxOrder (db: IDbConnection) : GetMaxStepOrder =
        fun pipelineId token ->
            task {
                try
                    let! result =
                        db.QuerySingleOrDefaultAsync<Nullable<int>>(
                            CommandDefinition(
                                "SELECT MAX(\"order\") FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                                {| PipelineId = PipelineId.value pipelineId |},
                                cancellationToken = token
                            )
                        )

                    let maxOrder = if result.HasValue then result.Value else -1
                    return Ok maxOrder
                with ex ->
                    return Error(Unexpected ex)
            }
