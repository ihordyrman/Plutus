namespace Warehouse.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Warehouse.Core.Domain
open Warehouse.Core.Shared.Errors

type CreateMarketRequest =
    { Type: MarketType; ApiKey: string; SecretKey: string; Passphrase: string option; IsSandbox: bool }

type UpdateMarketRequest =
    { ApiKey: string option
      SecretKey: string option
      Passphrase: string option
      IsSandbox: bool option }

[<RequireQualifiedAccess>]
module MarketRepository =
    let getById (db: IDbConnection) (id: int) (cancellation: CancellationToken) =
        task {
            try

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
                | None -> return Error(NotFound $"Market with id {id}")
                | Some entity -> return Ok(entity)
            with ex ->
                return Error(Unexpected ex)
        }

    let getByType (db: IDbConnection) (marketType: MarketType) (cancellation: CancellationToken) =
        task {
            try
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
                | None -> return Ok None
                | Some entity -> return Ok(Some(entity))
            with ex ->
                return Error(Unexpected ex)
        }

    let getAll (db: IDbConnection) (cancellation: CancellationToken) =
        task {
            try
                let! markets =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, api_key, secret_key, passphrase, is_sandbox, created_at, updated_at
                             FROM markets ORDER BY id",
                            cancellationToken = cancellation
                        )
                    )

                let markets = markets |> Seq.toList
                return Ok markets
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (request: CreateMarketRequest) (cancellation: CancellationToken) =
        task {
            try
                let! existingCount =
                    db.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM markets WHERE type = @Type",
                        {| Type = int request.Type |}
                    )

                if existingCount > 0 then
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
                return Error(Unexpected ex)
        }

    let update (db: IDbConnection) (marketId: int) (request: UpdateMarketRequest) (cancellation: CancellationToken) =
        task {
            try
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
                | None -> return Error(NotFound $"Market with id {marketId}")
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
                return Error(Unexpected ex)
        }

    let delete (db: IDbConnection) (id: int) (cancellation: CancellationToken) =
        task {
            try
                let! affected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM markets WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = cancellation
                        )
                    )

                if affected = 0 then return Error(NotFound $"Market with id {id}") else return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let count (db: IDbConnection) (cancellation: CancellationToken) =
        task {
            try
                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition("SELECT COUNT(1) FROM markets", cancellationToken = cancellation)
                    )

                return Ok count
            with ex ->
                return Error(Unexpected ex)
        }

    let exists (db: IDbConnection) (marketType: MarketType) (cancellation: CancellationToken) =
        task {
            try
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
                return Error(Unexpected ex)
        }
