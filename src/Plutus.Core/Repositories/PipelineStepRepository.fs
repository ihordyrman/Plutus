namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module PipelineStepRepository =

    let getById (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<PipelineStep>(
                        CommandDefinition(
                            "SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                match results |> Seq.tryHead with
                | Some entity -> return Ok entity
                | None -> return Error(NotFound $"Step with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let getByPipelineId (db: IDbConnection) (pipelineId: int) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<PipelineStep>(
                        CommandDefinition(
                            "SELECT * FROM pipeline_steps WHERE pipeline_id = @PipelineId ORDER BY \"order\"",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (step: PipelineStep) (token: CancellationToken) =
        task {
            try
                let now = DateTime.UtcNow

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO pipeline_steps
                           (pipeline_id, step_type_key, name, "order", is_enabled, parameters, created_at, updated_at)
                           VALUES (@PipelineId, @StepTypeKey, @Name, @Order, @IsEnabled, @Parameters::jsonb, now(), now())
                           RETURNING id""",
                            step,
                            cancellationToken = token
                        )
                    )

                return Ok { step with Id = result; CreatedAt = now; UpdatedAt = now }
            with ex ->
                return Error(Unexpected ex)
        }

    let update (db: IDbConnection) (step: PipelineStep) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE pipeline_steps
                           SET step_type_key = @StepTypeKey, name = @Name, "order" = @Order,
                               is_enabled = @IsEnabled, parameters = @Parameters::jsonb, updated_at = now()
                           WHERE id = @Id""",
                            step,
                            cancellationToken = token
                        )
                    )

                if result > 0 then
                    let! step =
                        db.QuerySingleAsync<PipelineStep>(
                            CommandDefinition(
                                "SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1",
                                {| Id = step.Id |},
                                cancellationToken = token
                            )
                        )

                    return Ok step
                else
                    return Error(NotFound $"Step with id {step.Id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let delete (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipeline_steps WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then return Ok() else return Error(NotFound $"Step with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let deleteByPipelineId (db: IDbConnection) (pipelineId: int) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                return Ok rowsAffected
            with ex ->
                return Error(Unexpected ex)
        }

    let setEnabled (db: IDbConnection) (stepId: int) (enabled: bool) (token: CancellationToken) =
        task {
            try
                let now = DateTime.UtcNow

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE pipeline_steps SET is_enabled = @IsEnabled, updated_at = @UpdatedAt WHERE id = @Id",
                            {| IsEnabled = enabled; UpdatedAt = now; Id = stepId |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then return Ok() else return Error(NotFound $"Step with id {stepId}")
            with ex ->
                return Error(Unexpected ex)
        }

    let swapOrders (db: IDbConnection) (step1: PipelineStep) (step2: PipelineStep) (token: CancellationToken) =
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
                            {| Id1 = step1.Id
                               Order1 = step1.Order
                               Id2 = step2.Id
                               Order2 = step2.Order
                               UpdatedAt = now |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then
                    return Ok()
                else
                    return Error(NotFound $"Steps with ids {step1.Id} or {step2.Id} not found")
            with ex ->
                return Error(Unexpected ex)
        }

    let getMaxOrder (db: IDbConnection) (pipelineId: int) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.QuerySingleOrDefaultAsync<Nullable<int>>(
                        CommandDefinition(
                            "SELECT MAX(\"order\") FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                let maxOrder = if result.HasValue then result.Value else -1
                return Ok maxOrder
            with ex ->
                return Error(Unexpected ex)
        }
