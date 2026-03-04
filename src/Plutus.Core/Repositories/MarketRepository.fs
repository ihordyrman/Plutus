namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module MarketRepository =
    let getById (db: IDbConnection) (id: int) (cancellation: CancellationToken) =
        task {
            try
                let! markets =
                    db.QueryAsync<Market>(
                        CommandDefinition(
                            "SELECT id, type, created_at, updated_at
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
                            "SELECT id, type, created_at, updated_at
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
                            "SELECT id, type, created_at, updated_at
                             FROM markets ORDER BY id",
                            cancellationToken = cancellation
                        )
                    )

                let markets = markets |> Seq.toList
                return Ok markets
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

    let ensureExists (db: IDbConnection) (marketType: MarketType) (cancellation: CancellationToken) =
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

                if count = 0 then
                    let now = DateTime.UtcNow

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "INSERT INTO markets (type, created_at, updated_at) VALUES (@Type, @CreatedAt, @UpdatedAt)",
                                {| Type = int marketType; CreatedAt = now; UpdatedAt = now |},
                                cancellationToken = cancellation
                            )
                        )

                    return Ok true
                else
                    return Ok false
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
