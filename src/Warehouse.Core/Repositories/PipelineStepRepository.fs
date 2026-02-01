namespace Warehouse.Core.Repositories

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Dapper
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Shared

module PipelineStepRepository =
    open Errors

    type T =
        { GetById: int -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
          GetByPipelineId: int -> CancellationToken -> Task<Result<PipelineStep list, ServiceError>>
          Create: PipelineStep -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
          Update: PipelineStep -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
          Delete: int -> CancellationToken -> Task<Result<unit, ServiceError>>
          DeleteByPipelineId: int -> CancellationToken -> Task<Result<int, ServiceError>>
          SetEnabled: int -> bool -> CancellationToken -> Task<Result<unit, ServiceError>>
          SwapOrders: PipelineStep -> PipelineStep -> CancellationToken -> Task<Result<unit, ServiceError>>
          ReorderSteps: int -> int list -> CancellationToken -> Task<Result<unit, ServiceError>>
          GetMaxOrder: int -> CancellationToken -> Task<Result<int, ServiceError>> }

    let private getById (scopeFactory: IServiceScopeFactory) (logger: ILogger) (id: int) (_: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! results =
                    db.QueryAsync<PipelineStep>("SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1", {| Id = id |})

                match results |> Seq.tryHead with
                | Some entity ->
                    logger.LogDebug("Retrieved step {Id}", id)
                    return Ok(entity)
                | None ->
                    logger.LogWarning("Step {Id} not found", id)
                    return Error(NotFound $"Step with id {id}")
            with ex ->
                logger.LogError(ex, "Failed to get step {Id}", id)
                return Error(Unexpected ex)
        }

    let private getByPipelineId
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (_: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! results =
                    db.QueryAsync<PipelineStep>(
                        "SELECT * FROM pipeline_steps WHERE pipeline_id = @PipelineId ORDER BY \"order\"",
                        {| PipelineId = pipelineId |}
                    )

                let steps = results |> Seq.toList
                logger.LogDebug("Retrieved {Count} steps for pipeline {PipelineId}", steps.Length, pipelineId)
                return Ok steps
            with ex ->
                logger.LogError(ex, "Failed to get steps for pipeline {PipelineId}", pipelineId)
                return Error(Unexpected ex)
        }

    let private createStep
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (step: PipelineStep)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let now = DateTime.UtcNow

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO pipeline_steps
                           (pipeline_id, step_type_key, name, "order", is_enabled, parameters, created_at, updated_at)
                           VALUES (@PipelineId, @StepTypeKey, @Name, @Order, @IsEnabled, @Parameters::jsonb, now(), now())
                           RETURNING id""",
                            step,
                            cancellationToken = cancellation
                        )
                    )

                logger.LogInformation("Created step {Id} for pipeline {PipelineId}", result, step.PipelineId)
                return Ok { step with Id = result; CreatedAt = now; UpdatedAt = now }
            with ex ->
                logger.LogError(ex, "Failed to create step for pipeline {PipelineId}", step.PipelineId)
                return Error(Unexpected ex)
        }

    let private updateStep
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (step: PipelineStep)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE pipeline_steps
                           SET step_type_key = @StepTypeKey, name = @Name, "order" = @Order,
                               is_enabled = @IsEnabled, parameters = @Parameters::jsonb, updated_at = now()
                           WHERE id = @Id""",
                            step,
                            cancellationToken = cancellation
                        )
                    )

                if result > 0 then
                    let! step =
                        db.QuerySingleAsync<PipelineStep>(
                            CommandDefinition(
                                "SELECT * FROM pipeline_steps WHERE id = @Id LIMIT 1",
                                {| Id = step.Id |},
                                cancellationToken = cancellation
                            )
                        )

                    logger.LogInformation("Updated step {Id}", step.Id)
                    return Ok step
                else
                    logger.LogWarning("Step {Id} not found for update", step.Id)
                    return Error(NotFound $"Step with id {step.Id}")
            with ex ->
                logger.LogError(ex, "Failed to update step {Id}", step.Id)
                return Error(Unexpected ex)
        }

    let private deleteStep
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (id: int)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipeline_steps WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Deleted step {Id}", id)
                    return Ok()
                else
                    logger.LogWarning("Step {Id} not found for deletion", id)
                    return Error(NotFound $"Step with id {id}")
            with ex ->
                logger.LogError(ex, "Failed to delete step {Id}", id)
                return Error(Unexpected ex)
        }

    let private deleteByPipelineId
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (token: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = token
                        )
                    )

                logger.LogInformation("Deleted {Count} steps for pipeline {PipelineId}", rowsAffected, pipelineId)
                return Ok rowsAffected
            with ex ->
                logger.LogError(ex, "Failed to delete steps for pipeline {PipelineId}", pipelineId)
                return Error(Unexpected ex)
        }

    let private setEnabled
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (stepId: int)
        (enabled: bool)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let now = DateTime.UtcNow

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE pipeline_steps SET is_enabled = @IsEnabled, updated_at = @UpdatedAt WHERE id = @Id",
                            {| IsEnabled = enabled; UpdatedAt = now; Id = stepId |},
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Set step {Id} enabled = {Enabled}", stepId, enabled)
                    return Ok()
                else
                    logger.LogWarning("Step {Id} not found for enable/disable", stepId)
                    return Error(NotFound $"Step with id {stepId}")
            with ex ->
                logger.LogError(ex, "Failed to set enabled for step {Id}", stepId)
                return Error(Unexpected ex)
        }

    let private swapOrders
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (step1: PipelineStep)
        (step2: PipelineStep)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

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
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogDebug("Swapped orders for steps {Id1} and {Id2}", step1.Id, step2.Id)
                    return Ok()
                else
                    logger.LogWarning("Steps {Id1} or {Id2} not found for order swap", step1.Id, step2.Id)
                    return Error(NotFound $"Steps with ids {step1.Id} or {step2.Id} not found")
            with ex ->
                logger.LogError(ex, "Failed to update order for steps {Id1} and {Id2}", step1.Id, step2.Id)
                return Error(Unexpected ex)
        }

    let private reorderSteps
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (stepIds: int list)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let now = DateTime.UtcNow

                // todo: optimize. for now it's simpler to implement like this
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            // temp large offset
                            "UPDATE pipeline_steps SET \"order\" = -\"order\" - 10000 WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = cancellation
                        )
                    )

                for i, stepId in stepIds |> List.indexed do
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE pipeline_steps SET \"order\" = @Order, updated_at = @UpdatedAt WHERE id = @Id AND pipeline_id = @PipelineId",
                                {| Order = i; UpdatedAt = now; Id = stepId; PipelineId = pipelineId |},
                                cancellationToken = cancellation
                            )
                        )

                    ()

                logger.LogInformation("Reordered {Count} steps for pipeline {PipelineId}", stepIds.Length, pipelineId)
                return Ok()
            with ex ->
                logger.LogError(ex, "Failed to reorder steps for pipeline {PipelineId}", pipelineId)
                return Error(Unexpected ex)
        }

    let private getMaxOrder
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleOrDefaultAsync<Nullable<int>>(
                        CommandDefinition(
                            "SELECT MAX(\"order\") FROM pipeline_steps WHERE pipeline_id = @PipelineId",
                            {| PipelineId = pipelineId |},
                            cancellationToken = cancellation
                        )
                    )

                let maxOrder = if result.HasValue then result.Value else -1
                logger.LogDebug("Max order for pipeline {PipelineId} is {MaxOrder}", pipelineId, maxOrder)
                return Ok maxOrder
            with ex ->
                logger.LogError(ex, "Failed to get max order for pipeline {PipelineId}", pipelineId)
                return Error(Unexpected ex)
        }

    let create (scopeFactory: IServiceScopeFactory) : T =
        let loggerFactory = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ILoggerFactory>()
        let logger = loggerFactory.CreateLogger("PipelineStepRepository")

        { GetById = getById scopeFactory logger
          GetByPipelineId = getByPipelineId scopeFactory logger
          Create = createStep scopeFactory logger
          Update = updateStep scopeFactory logger
          Delete = deleteStep scopeFactory logger
          DeleteByPipelineId = deleteByPipelineId scopeFactory logger
          SetEnabled = setEnabled scopeFactory logger
          SwapOrders = swapOrders scopeFactory logger
          ReorderSteps = reorderSteps scopeFactory logger
          GetMaxOrder = getMaxOrder scopeFactory logger }
