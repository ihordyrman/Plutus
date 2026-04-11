namespace Plutus.Identity.GetAllKeys

open System
open System.Data
open Dapper
open Plutus.Identity.Entities
open Plutus.Identity.Helpers
open Plutus.Identity.Ports
open Plutus.Shared.Errors

module internal Adapter =
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
                            | Error e, _ -> Error(Unexpected(Exception e))
                            | _, Error e -> Error e
                        )
                        <| Ok []
                with ex ->
                    return Error(Unexpected ex)
            }
