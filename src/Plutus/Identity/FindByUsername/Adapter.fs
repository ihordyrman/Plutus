namespace Plutus.Identity.FindByUsername

open System
open System.Data
open Dapper
open Plutus.Identity.Domain
open Plutus.Identity.Entities
open Plutus.Identity.Helpers
open Plutus.Identity.Ports
open Plutus.Shared.Errors

module internal Adapter =
    let findByUsername (db: IDbConnection) : FindUserByUsername =
        fun username ct ->
            task {
                try
                    let value = Username.value username

                    let! users =
                        db.QueryAsync<UserEntity>(
                            CommandDefinition(
                                "SELECT id, username, password_hash, created_at, updated_at
                                FROM users WHERE username = @Username LIMIT 1",
                                {| Username = value |},
                                cancellationToken = ct
                            )
                        )

                    match users |> Seq.tryHead with
                    | None -> return Ok None
                    | Some user ->
                        match toUser user with
                        | Ok authenticatedUser -> return Ok(Some authenticatedUser)
                        | Error e -> return Error(Unexpected(Exception $"Failed to map user: {e}"))
                with ex ->
                    return Error(Unexpected ex)
            }
