namespace Warehouse.Core.Repositories

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Dapper
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Shared.Errors

module MarketRepository =

    type CreateMarketRequest =
        { Type: MarketType; ApiKey: string; SecretKey: string; Passphrase: string option; IsSandbox: bool }

    type UpdateMarketRequest =
        { ApiKey: string option
          SecretKey: string option
          Passphrase: string option
          IsSandbox: bool option }

    type T =
        { GetById: int -> CancellationToken -> Task<Result<Market, ServiceError>>
          GetByType: MarketType -> CancellationToken -> Task<Result<Market option, ServiceError>>
          GetAll: CancellationToken -> Task<Result<Market list, ServiceError>>
          Create: CreateMarketRequest -> CancellationToken -> Task<Result<Market, ServiceError>>
          Update: int -> UpdateMarketRequest -> CancellationToken -> Task<Result<Market, ServiceError>>
          Delete: int -> CancellationToken -> Task<Result<unit, ServiceError>>
          Count: CancellationToken -> Task<Result<int, ServiceError>>
          Exists: MarketType -> CancellationToken -> Task<Result<bool, ServiceError>> }

    let private getById
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (id: int)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! markets =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at
                         FROM markets WHERE id = @Id LIMIT 1",
                            {| Id = id |},
                            cancellationToken = cancellation
                        )
                    )

                match markets |> Seq.tryHead with
                | None ->
                    logger.LogWarning("Market {Id} not found", id)
                    return Error(NotFound $"Market with id {id}")
                | Some entity ->
                    logger.LogDebug("Retrieved market {Id}", id)
                    return Ok(entity)
            with ex ->
                logger.LogError(ex, "Failed to get market {Id}", id)
                return Error(Unexpected ex)
        }

    let private getByType
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (marketType: MarketType)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! markets =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at
                         FROM markets WHERE type = @Type LIMIT 1",
                            {| Type = int marketType |},
                            cancellationToken = cancellation
                        )
                    )

                match markets |> Seq.tryHead with
                | None ->
                    logger.LogDebug("Market type {MarketType} not found", marketType)
                    return Ok None
                | Some entity ->
                    logger.LogDebug("Retrieved market type {MarketType}", marketType)
                    return Ok(Some(entity))
            with ex ->
                logger.LogError(ex, "Failed to get market type {MarketType}", marketType)
                return Error(Unexpected ex)
        }

    let private getAll (scopeFactory: IServiceScopeFactory) (logger: ILogger) (cancellation: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! markets =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at
                         FROM markets ORDER BY id",
                            cancellationToken = cancellation
                        )
                    )

                let markets = markets |> Seq.toList
                logger.LogDebug("Retrieved {Count} markets", markets.Length)
                return Ok markets
            with ex ->
                logger.LogError(ex, "Failed to get all markets")
                return Error(Unexpected ex)
        }

    let private createMarket
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (request: CreateMarketRequest)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! existingCount =
                    db.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM markets WHERE type = @Type",
                        {| Type = int request.Type |}
                    )

                if existingCount > 0 then
                    logger.LogWarning("Market type {MarketType} already exists", request.Type)
                    return Error(ApiError($"Market {request.Type} already exists", Some 409))
                else
                    let now = DateTime.UtcNow

                    let! marketId =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                "INSERT INTO markets (type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at)
                                 VALUES (@Type, @ApiKey, @SecretKey, @Passphrase, @IsSandbox, @CreatedAt, @UpdatedAt)
                                 RETURNING id",
                                {| Type = int request.Type
                                   ApiKey = request.ApiKey
                                   SecretKey = request.SecretKey
                                   Passphrase = request.Passphrase |> Option.defaultValue ""
                                   IsSandbox = request.IsSandbox
                                   CreatedAt = now
                                   UpdatedAt = now |},
                                cancellationToken = cancellation
                            )
                        )

                    logger.LogInformation("Created market {Id} of type {MarketType}", marketId, request.Type)

                    let market: Market =
                        { Id = marketId
                          Type = request.Type
                          ApiKey = request.ApiKey
                          SecretKey = request.SecretKey
                          Passphrase = request.Passphrase
                          IsSandbox = request.IsSandbox
                          CreatedAt = now
                          UpdatedAt = now }

                    return Ok market
            with ex ->
                logger.LogError(ex, "Failed to create market of type {MarketType}", request.Type)
                return Error(Unexpected ex)
        }

    let private updateMarket
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (marketId: int)
        (request: UpdateMarketRequest)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! existingResults =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at
                             FROM markets WHERE id = @Id LIMIT 1",
                            {| Id = marketId |},
                            cancellationToken = cancellation
                        )
                    )

                match existingResults |> Seq.tryHead with
                | None ->
                    logger.LogWarning("Market {Id} not found for update", marketId)
                    return Error(NotFound $"Market with id {marketId}")
                | Some existing ->
                    let now = DateTime.UtcNow
                    let newApiKey = request.ApiKey |> Option.defaultValue existing.ApiKey
                    let newSecretKey = request.SecretKey |> Option.defaultValue existing.SecretKey
                    let newPassphrase = request.Passphrase
                    let newIsSandbox = request.IsSandbox |> Option.defaultValue existing.IsSandbox

                    let! _ =
                        db.ExecuteAsync(
                            "UPDATE markets
                             SET api_key = @ApiKey,
                                 secret_key = @SecretKey,
                                 passphrase = @Passphrase,
                                 is_sandbox = @IsSandbox,
                                 updated_at = @UpdatedAt
                             WHERE id = @Id",
                            {| Id = marketId
                               ApiKey = newApiKey
                               SecretKey = newSecretKey
                               Passphrase = newPassphrase
                               IsSandbox = newIsSandbox
                               UpdatedAt = now |}
                        )

                    logger.LogInformation("Updated market {Id}", marketId)

                    let updatedMarket: Market =
                        { Id = marketId
                          Type = existing.Type
                          ApiKey = newApiKey
                          SecretKey = newSecretKey
                          Passphrase = newPassphrase
                          IsSandbox = newIsSandbox
                          CreatedAt = existing.CreatedAt
                          UpdatedAt = now }

                    return Ok updatedMarket
            with ex ->
                logger.LogError(ex, "Failed to update market {Id}", marketId)
                return Error(Unexpected ex)
        }

    let private deleteMarket (scopeFactory: IServiceScopeFactory) (logger: ILogger) (id: int) (_: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! affected = db.ExecuteAsync("DELETE FROM markets WHERE id = @Id", {| Id = id |})

                if affected = 0 then
                    logger.LogWarning("Market {Id} not found for deletion", id)
                    return Error(NotFound $"Market with id {id}")
                else
                    logger.LogInformation("Deleted market {Id}", id)
                    return Ok()
            with ex ->
                logger.LogError(ex, "Failed to delete market {Id}", id)
                return Error(Unexpected ex)
        }

    let private count (scopeFactory: IServiceScopeFactory) (logger: ILogger) (cancellation: CancellationToken) =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition("SELECT COUNT(1) FROM markets", cancellationToken = cancellation)
                    )

                return Ok count
            with ex ->
                logger.LogError(ex, "Failed to count markets")
                return Error(Unexpected ex)
        }

    let private exists
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (marketType: MarketType)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(1) FROM markets WHERE type = @Type",
                            {| Type = int marketType |},
                            cancellationToken = cancellation
                        )
                    )

                return Ok(count > 0)
            with ex ->
                logger.LogError(ex, "Failed to check if market {MarketType} exists", marketType)
                return Error(Unexpected ex)
        }

    let create (scopeFactory: IServiceScopeFactory) (loggerFactory: ILoggerFactory) : T =
        let logger = loggerFactory.CreateLogger("MarketRepository")

        { GetById = getById scopeFactory logger
          GetByType = getByType scopeFactory logger
          GetAll = getAll scopeFactory logger
          Create = createMarket scopeFactory logger
          Update = updateMarket scopeFactory logger
          Delete = deleteMarket scopeFactory logger
          Count = count scopeFactory logger
          Exists = exists scopeFactory logger }
