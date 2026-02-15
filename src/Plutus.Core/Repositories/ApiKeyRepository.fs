namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module ApiKeyRepository =

    let getByHash (db: IDbConnection) (hash: string) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.QueryFirstOrDefaultAsync<ApiKey>(
                        CommandDefinition(
                            "SELECT * FROM api_keys WHERE key_hash = @Hash AND is_active = true",
                            {| Hash = hash |},
                            cancellationToken = token
                        )
                    )

                match box result with
                | null -> return Ok None
                | _ -> return Ok(Some result)
            with ex ->
                return Error(Unexpected ex)
        }

    let getAll (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<ApiKey>(
                        CommandDefinition("SELECT * FROM api_keys ORDER BY created_at DESC", cancellationToken = token)
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (name: string) (hash: string) (prefix: string) (token: CancellationToken) =
        task {
            try
                let! id =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO api_keys (name, key_hash, key_prefix, created_at)
                               VALUES (@Name, @KeyHash, @KeyPrefix, now())
                               RETURNING id""",
                            {| Name = name; KeyHash = hash; KeyPrefix = prefix |},
                            cancellationToken = token
                        )
                    )

                let! key =
                    db.QuerySingleAsync<ApiKey>(
                        CommandDefinition(
                            "SELECT * FROM api_keys WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                return Ok key
            with ex ->
                return Error(Unexpected ex)
        }

    let deactivate (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE api_keys SET is_active = false WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then return Ok() else return Error(NotFound $"API key with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let updateLastUsed (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "UPDATE api_keys SET last_used = @Now WHERE id = @Id",
                            {| Now = DateTime.UtcNow; Id = id |},
                            cancellationToken = token
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }
