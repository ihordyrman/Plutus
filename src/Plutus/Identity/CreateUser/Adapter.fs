namespace Plutus.Identity.CreateUser

open System
open System.Data
open Dapper
open Plutus.Identity.Domain
open Plutus.Identity.Ports
open Plutus.Shared.Errors

module internal Adapter =
    let createUser (db: IDbConnection) : CreateUser =
        fun username passwordHash ct ->
            task {
                try
                    let now = DateTime.UtcNow
                    let usernameValue = Username.value username
                    let passwordHashValue = PasswordHash.value passwordHash

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "INSERT INTO users (username, password_hash, created_at, updated_at)
                                VALUES (@Username, @PasswordHash, @CreatedAt, @UpdatedAt)",
                                {| Username = usernameValue
                                   PasswordHash = passwordHashValue
                                   CreatedAt = now
                                   UpdatedAt = now |},
                                cancellationToken = ct
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }
