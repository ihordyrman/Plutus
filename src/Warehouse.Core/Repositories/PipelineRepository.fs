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

module PipelineRepository =
    open Errors

    type SearchFilters =
        { SearchTerm: string option
          Tag: string option
          MarketType: string option
          Status: PipelineStatus option
          SortBy: string }

    type SearchResult = { Pipelines: Pipeline list; TotalCount: int }

    type T =
        { GetById: int -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
          GetAll: CancellationToken -> Task<Result<Pipeline list, ServiceError>>
          GetEnabled: CancellationToken -> Task<Result<Pipeline list, ServiceError>>
          Create: Pipeline -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
          Update: Pipeline -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
          Delete: int -> CancellationToken -> Task<Result<unit, ServiceError>>
          SetEnabled: int -> bool -> CancellationToken -> Task<Result<unit, ServiceError>>
          UpdateLastExecuted: int -> DateTime -> CancellationToken -> Task<Result<unit, ServiceError>>
          UpdateStatus: int -> PipelineStatus -> CancellationToken -> Task<Result<unit, ServiceError>>
          Count: CancellationToken -> Task<Result<int, ServiceError>>
          CountEnabled: CancellationToken -> Task<Result<int, ServiceError>>
          GetAllTags: CancellationToken -> Task<Result<string list, ServiceError>>
          Search: SearchFilters -> int -> int -> CancellationToken -> Task<Result<SearchResult, ServiceError>> }

    let private getById (scopeFactory: IServiceScopeFactory) (logger: ILogger) (id: int) (token: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! pipeline =
                    db.QueryFirstOrDefaultAsync<Pipeline>(
                        CommandDefinition(
                            "SELECT * FROM pipelines WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                match box pipeline with
                | null ->
                    logger.LogWarning("Pipeline {Id} not found", id)
                    return Error(NotFound $"Pipeline with id {id}")
                | _ ->
                    logger.LogDebug("Retrieved pipeline {Id}", id)
                    return Ok pipeline

            with ex ->
                logger.LogError(ex, "Failed to get pipeline {Id}", id)
                return Error(Unexpected ex)
        }

    let private getAll (scopeFactory: IServiceScopeFactory) (logger: ILogger) (token: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! pipelines =
                    db.QueryAsync<Pipeline>(
                        CommandDefinition("SELECT * FROM pipelines ORDER BY id", cancellationToken = token)
                    )

                let pipelines = pipelines |> Seq.toList
                logger.LogDebug("Retrieved {Count} pipelines", pipelines.Length)
                return Ok pipelines
            with ex ->
                logger.LogError(ex, "Failed to get all pipelines")
                return Error(Unexpected ex)
        }

    let private getEnabled (scopeFactory: IServiceScopeFactory) (logger: ILogger) (token: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! pipelines =
                    db.QueryAsync<Pipeline>(
                        CommandDefinition(
                            "SELECT * FROM pipelines WHERE enabled = true ORDER BY id",
                            cancellationToken = token
                        )
                    )

                let pipelines = pipelines |> Seq.toList
                logger.LogDebug("Retrieved {Count} enabled pipelines", pipelines.Length)
                return Ok pipelines
            with ex ->
                logger.LogError(ex, "Failed to get enabled pipelines")
                return Error(Unexpected ex)
        }

    let private createPipeline
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipeline: Pipeline)
        (token: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO pipelines
                           (name, symbol, market_type, enabled, execution_interval, last_executed_at, status, tags, created_at, updated_at)
                           VALUES (@Name, @Symbol, @MarketType, @Enabled, @ExecutionInterval, @LastExecutedAt, @Status, @Tags::jsonb, now(), now())
                           RETURNING id""",
                            pipeline,
                            cancellationToken = token
                        )
                    )

                logger.LogInformation("Created pipeline {Id} for symbol {Symbol}", result, pipeline.Symbol)

                let! pipeline =
                    db.QuerySingleAsync<Pipeline>(
                        CommandDefinition(
                            "SELECT * FROM pipelines WHERE id = @Id",
                            {| Id = result |},
                            cancellationToken = token
                        )
                    )

                return Ok pipeline
            with ex ->
                logger.LogError(ex, "Failed to create pipeline for symbol {Symbol}", pipeline.Symbol)
                return Error(Unexpected ex)
        }

    let private updatePipeline
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipeline: Pipeline)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE pipelines
                           SET name = @Name, symbol = @Symbol, market_type = @MarketType,
                               enabled = @Enabled, execution_interval = @ExecutionInterval,
                               last_executed_at = @LastExecutedAt, status = @Status, tags = @Tags::jsonb,
                               updated_at = now()
                           WHERE id = @Id""",
                            pipeline,
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    let! pipeline =
                        db.QuerySingleAsync<Pipeline>(
                            CommandDefinition(
                                "SELECT * FROM pipelines WHERE id = @Id",
                                {| Id = pipeline.Id |},
                                cancellationToken = cancellation
                            )
                        )

                    logger.LogInformation("Updated pipeline {Id}", pipeline.Id)
                    return Ok pipeline
                else
                    logger.LogWarning("Pipeline {Id} not found for update", pipeline.Id)
                    return Error(NotFound $"Pipeline with id {pipeline.Id}")
            with ex ->
                logger.LogError(ex, "Failed to update pipeline {Id}", pipeline.Id)
                return Error(Unexpected ex)
        }

    let private deletePipeline
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (id: int)
        (token: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipelines WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Deleted pipeline {Id}", id)
                    return Ok()
                else
                    logger.LogWarning("Pipeline {Id} not found for deletion", id)
                    return Error(NotFound $"Pipeline with id {id}")
            with ex ->
                logger.LogError(ex, "Failed to delete pipeline {Id}", id)
                return Error(Unexpected ex)
        }

    let private setEnabled
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (enabled: bool)
        (token: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let now = DateTime.UtcNow

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE pipelines SET enabled = @Enabled, updated_at = @UpdatedAt WHERE id = @Id",
                            {| Enabled = enabled; UpdatedAt = now; Id = pipelineId |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Set pipeline {Id} enabled = {Enabled}", pipelineId, enabled)
                    return Ok()
                else
                    logger.LogWarning("Pipeline {Id} not found for enable/disable", pipelineId)
                    return Error(NotFound $"Pipeline with id {pipelineId}")
            with ex ->
                logger.LogError(ex, "Failed to set enabled for pipeline {Id}", pipelineId)
                return Error(Unexpected ex)
        }

    let private updateLastExecuted
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (lastExecutedAt: DateTime)
        (token: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let now = DateTime.UtcNow

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE pipelines SET last_executed_at = @LastExecutedAt, updated_at = @UpdatedAt WHERE id = @Id",
                            {| LastExecutedAt = lastExecutedAt; UpdatedAt = now; Id = pipelineId |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogDebug("Updated last executed for pipeline {Id}", pipelineId)
                    return Ok()
                else
                    logger.LogWarning("Pipeline {Id} not found for last executed update", pipelineId)
                    return Error(NotFound $"Pipeline with id {pipelineId}")
            with ex ->
                logger.LogError(ex, "Failed to update last executed for pipeline {Id}", pipelineId)
                return Error(Unexpected ex)
        }

    let private updateStatus
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (pipelineId: int)
        (status: PipelineStatus)
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
                            "UPDATE pipelines SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
                            {| Status = int status; UpdatedAt = now; Id = pipelineId |},
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Updated status for pipeline {Id} to {Status}", pipelineId, status)
                    return Ok()
                else
                    logger.LogWarning("Pipeline {Id} not found for status update", pipelineId)
                    return Error(NotFound $"Pipeline with id {pipelineId}")
            with ex ->
                logger.LogError(ex, "Failed to update status for pipeline {Id}", pipelineId)
                return Error(Unexpected ex)
        }

    let private count (scopeFactory: IServiceScopeFactory) (logger: ILogger) (token: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition("SELECT COUNT(1) FROM pipelines", cancellationToken = token)
                    )

                logger.LogDebug("Pipeline count: {Count}", result)
                return Ok result
            with ex ->
                logger.LogError(ex, "Failed to count pipelines")
                return Error(Unexpected ex)
        }

    let private countEnabled (scopeFactory: IServiceScopeFactory) (logger: ILogger) (cancellation: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(1) FROM pipelines WHERE enabled = true",
                            cancellationToken = cancellation
                        )
                    )

                logger.LogDebug("Enabled pipeline count: {Count}", result)
                return Ok result
            with ex ->
                logger.LogError(ex, "Failed to count enabled pipelines")
                return Error(Unexpected ex)
        }

    let private getAllTags (scopeFactory: IServiceScopeFactory) (logger: ILogger) (cancellation: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! results =
                    db.QueryAsync<string>(
                        CommandDefinition(
                            "SELECT tags FROM pipelines GROUP BY tags ORDER BY tags ASC",
                            cancellationToken = cancellation
                        )
                    )

                let tags = results |> Seq.toList
                logger.LogDebug("Retrieved {Count} unique tag groups", tags.Length)
                return Ok tags
            with ex ->
                logger.LogError(ex, "Failed to get all tags")
                return Error(Unexpected ex)
        }

    let private search
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (filters: SearchFilters)
        (skip: int)
        (take: int)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let conditions = ResizeArray<string>()
                let parameters = DynamicParameters()

                match filters.SearchTerm with
                | Some term when not (String.IsNullOrEmpty term) ->
                    conditions.Add("AND symbol ILIKE @SearchTerm")
                    parameters.Add("SearchTerm", $"%%{term}%%")
                | _ -> ()

                match filters.MarketType with
                | Some marketType when not (String.IsNullOrEmpty marketType) ->
                    conditions.Add("AND market_type = @MarketType")
                    parameters.Add("MarketType", marketType)
                | _ -> ()

                match filters.Status with
                | Some status ->
                    let isEnabled = status = PipelineStatus.Running
                    conditions.Add("AND enabled = @Enabled")
                    parameters.Add("Enabled", isEnabled)
                | None -> ()

                let orderClause =
                    match filters.SortBy with
                    | "symbol-desc" -> "ORDER BY symbol DESC"
                    | "account" -> "ORDER BY market_type ASC"
                    | "account-desc" -> "ORDER BY market_type DESC"
                    | "status" -> "ORDER BY enabled ASC"
                    | "status-desc" -> "ORDER BY enabled DESC"
                    | "updated" -> "ORDER BY updated_at ASC"
                    | "updated-desc" -> "ORDER BY updated_at DESC"
                    | _ -> "ORDER BY symbol ASC"

                let whereClause = String.Join(" ", conditions)

                parameters.Add("Skip", skip)
                parameters.Add("Take", take)

                let countSql = $"SELECT COUNT(1) FROM pipelines WHERE 1=1 {whereClause}"
                let dataSql = $"SELECT * FROM pipelines WHERE 1=1 {whereClause} {orderClause} OFFSET @Skip LIMIT @Take"

                let! totalCount =
                    db.QuerySingleAsync<int>(CommandDefinition(countSql, parameters, cancellationToken = cancellation))

                let! results =
                    db.QueryAsync<Pipeline>(CommandDefinition(dataSql, parameters, cancellationToken = cancellation))

                let pipelines = results |> Seq.toList

                // Apply tag filter in memory (tags are stored as JSON)
                let filteredPipelines =
                    match filters.Tag with
                    | Some tag when not (String.IsNullOrEmpty tag) ->
                        pipelines |> List.filter (fun p -> p.Tags |> List.contains tag)
                    | _ -> pipelines

                logger.LogDebug(
                    "Search returned {Count} pipelines out of {Total}",
                    filteredPipelines.Length,
                    totalCount
                )

                return Ok { Pipelines = filteredPipelines; TotalCount = totalCount }
            with ex ->
                logger.LogError(ex, "Failed to search pipelines")
                return Error(Unexpected ex)
        }

    let create (scopeFactory: IServiceScopeFactory) : T =
        let loggerFactory = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ILoggerFactory>()
        let logger = loggerFactory.CreateLogger("PipelineRepository")

        { GetById = getById scopeFactory logger
          GetAll = getAll scopeFactory logger
          GetEnabled = getEnabled scopeFactory logger
          Create = createPipeline scopeFactory logger
          Update = updatePipeline scopeFactory logger
          Delete = deletePipeline scopeFactory logger
          SetEnabled = setEnabled scopeFactory logger
          UpdateLastExecuted = updateLastExecuted scopeFactory logger
          UpdateStatus = updateStatus scopeFactory logger
          Count = count scopeFactory logger
          CountEnabled = countEnabled scopeFactory logger
          GetAllTags = getAllTags scopeFactory logger
          Search = search scopeFactory logger }
