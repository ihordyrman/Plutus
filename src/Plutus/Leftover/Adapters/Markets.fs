namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private MarketEntity = { Id: int; Type: MarketType; CreatedAt: DateTime; UpdatedAt: DateTime }

module Markets =
    let private toMarket (entity: MarketEntity) : Market =
        match MarketId.create entity.Id with
        | Ok id -> { Id = id; Type = entity.Type; CreatedAt = entity.CreatedAt; UpdatedAt = entity.UpdatedAt }
        | Error e -> failwith $"Invalid Market ID in database: {e}"

    let getById (db: IDbConnection) : GetById =
        fun id token ->
            task {
                try
                    let id = MarketId.value id

                    let! markets =
                        db.QueryAsync<MarketEntity>(
                            CommandDefinition(
                                "SELECT id, type, created_at, updated_at
                                       FROM markets WHERE id = @Id LIMIT 1",
                                {| Id = id |},
                                cancellationToken = token
                            )
                        )

                    match markets |> Seq.tryHead with
                    | None -> return Error(NotFound $"Market with id {id}")
                    | Some entity -> return Ok(toMarket entity)
                with ex ->
                    return Error(Unexpected ex)
            }

    let getAll (db: IDbConnection) : GetAll =
        fun token ->
            task {
                try
                    let! markets =
                        db.QueryAsync<MarketEntity>(
                            CommandDefinition(
                                "SELECT id, type, created_at, updated_at
                                       FROM markets ORDER BY id",
                                cancellationToken = token
                            )
                        )

                    return markets |> Seq.toList |> List.map toMarket |> Ok
                with ex ->
                    return Error(Unexpected ex)
            }

    let count (db: IDbConnection) : Count =
        fun token ->
            task {
                try
                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(1) FROM markets", cancellationToken = token)
                        )

                    return Ok count
                with ex ->
                    return Error(Unexpected ex)
            }

    let ensureExists (db: IDbConnection) : EnsureExists =
        fun marketType token ->
            task {
                try
                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                "SELECT COUNT(1) FROM markets WHERE type = @Type",
                                {| Type = int marketType |},
                                cancellationToken = token
                            )
                        )

                    if count = 0 then
                        let now = DateTime.UtcNow

                        let! _ =
                            db.ExecuteAsync(
                                CommandDefinition(
                                    "INSERT INTO markets (type, created_at, updated_at) VALUES (@Type, @CreatedAt, @UpdatedAt)",
                                    {| Type = int marketType; CreatedAt = now; UpdatedAt = now |},
                                    cancellationToken = token
                                )
                            )

                        return Ok()
                    else
                        return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }
