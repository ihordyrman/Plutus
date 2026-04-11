namespace Plutus.Identity.Create

open System
open System.Data
open Dapper
open Plutus.Identity.Domain
open Plutus.Identity.Entities
open Plutus.Identity.Helpers
open Plutus.Identity.Ports
open Plutus.Shared.Errors

module internal Adapter =
    let create (db: IDbConnection) : Create =
        fun name hash prefix token ->
            task {
                try
                    let name = KeyName.value name
                    let hash = KeyHash.value hash
                    let prefix = KeyPrefix.value prefix

                    let! key =
                        db.QuerySingleAsync<ApiKey>(
                            CommandDefinition(
                                """INSERT INTO api_keys (name, key_hash, key_prefix, created_at)
                                     VALUES (@Name, @KeyHash, @KeyPrefix, now())
                                     RETURNING *""",
                                {| Name = name
                                   KeyHash = hash
                                   KeyPrefix = prefix |},
                                cancellationToken = token
                            )
                        )

                    match toKey key with
                    | Ok k -> return Ok k
                    | Error e -> return Error(Unexpected(Exception $"Failed to map API key: {e}"))
                with ex ->
                    return Error(Unexpected ex)
            }
