namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Ports
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private UserEntity =
    { Id: int
      Username: string
      PasswordHash: string }

module UserAdapters =
    let private toAuthenticatedUser (user: UserEntity) : Result<AuthenticatedUser, string> =
        match UserId.create user.Id, Username.create user.Username, PasswordHash.create user.PasswordHash with
        | Ok id, Ok username, Ok passwordHash ->
            Ok
                { Id = id
                  Username = username
                  PasswordHash = passwordHash }
        | Error e, _, _ -> Error $"Invalid ID for user {user.Id}: {e}"
        | _, Error e, _ -> Error $"Invalid username for user ID {user.Id}: {e}"
        | _, _, Error e -> Error $"Invalid password hash for user ID {user.Id}: {e}"

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
                        match toAuthenticatedUser user with
                        | Ok authenticatedUser -> return Ok(Some authenticatedUser)
                        | Error e -> return Error(Unexpected(Exception($"Failed to map user: {e}")))
                with ex ->
                    return Error(Unexpected ex)
            }

    let userExists (db: IDbConnection) : UserExists =
        fun ct ->
            task {
                try
                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(1) FROM users", cancellationToken = ct)
                        )

                    return Ok(count > 0)
                with ex ->
                    return Error(Unexpected ex)
            }

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
