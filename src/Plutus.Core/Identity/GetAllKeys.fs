namespace Plutus.Core.Identity.GetAllKeys

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Identity.Entities
open Plutus.Core.Identity.Domain
open Plutus.Core.Identity.Ports
open Plutus.Core.Shared.Errors

module Adapter =
    let private toKey (apiKey: ApiKey) : Result<Key, string> =
        match
            KeyId.create apiKey.Id,
            KeyName.create apiKey.Name,
            KeyHash.create apiKey.KeyHash,
            KeyPrefix.create apiKey.KeyPrefix
        with
        | Ok id, Ok name, Ok hash, Ok prefix ->
            Ok
                { Id = id
                  Name = name
                  Hash = hash
                  Prefix = prefix
                  IsActive = apiKey.IsActive
                  LastUsed = apiKey.LastUsed
                  CreatedAt = apiKey.CreatedAt }
        | Error e, _, _, _ -> Error $"Invalid KeyId: {e}"
        | _, Error e, _, _ -> Error $"Invalid KeyName: {e}"
        | _, _, Error e, _ -> Error $"Invalid KeyHash: {e}"
        | _, _, _, Error e -> Error $"Invalid KeyPrefix: {e}"

    let getAll (db: IDbConnection) : GetAll =
        fun token ->
            task {
                try
                    let! keys =
                        db.QueryAsync<ApiKey>(
                            CommandDefinition(
                                "SELECT * FROM api_keys ORDER BY created_at DESC",
                                cancellationToken = token
                            )
                        )

                    return
                        keys
                        |> Seq.toList
                        |> List.map toKey
                        |> List.foldBack (fun result acc ->
                            match result, acc with
                            | Ok key, Ok keys -> Ok(key :: keys)
                            | Error e, _ -> Error(Unexpected(Exception(e)))
                            | _, Error e -> Error e
                        )
                        <| Ok []
                with ex ->
                    return Error(Unexpected ex)
            }

