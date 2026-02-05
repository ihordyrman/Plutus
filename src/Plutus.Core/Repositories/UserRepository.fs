namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type User = { Id: int; Username: string; PasswordHash: string; CreatedAt: DateTime; UpdatedAt: DateTime }

[<RequireQualifiedAccess>]
module UserRepository =
    let findByUsername (db: IDbConnection) (username: string) (cancellation: CancellationToken) =
        task {
            try
                let! users =
                    db.QueryAsync<User>(
                        CommandDefinition(
                            "SELECT id, username, password_hash, created_at, updated_at
                             FROM users WHERE username = @Username LIMIT 1",
                            {| Username = username |},
                            cancellationToken = cancellation
                        )
                    )

                match users |> Seq.tryHead with
                | None -> return Ok None
                | Some user -> return Ok(Some user)
            with ex ->
                return Error(Unexpected ex)
        }

    let userExists (db: IDbConnection) (cancellation: CancellationToken) =
        task {
            try
                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition("SELECT COUNT(1) FROM users", cancellationToken = cancellation)
                    )

                return Ok(count > 0)
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (username: string) (passwordHash: string) (cancellation: CancellationToken) =
        task {
            try
                let now = DateTime.UtcNow

                let! userId =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "INSERT INTO users (username, password_hash, created_at, updated_at)
                             VALUES (@Username, @PasswordHash, @CreatedAt, @UpdatedAt)
                             RETURNING id",
                            {| Username = username; PasswordHash = passwordHash; CreatedAt = now; UpdatedAt = now |},
                            cancellationToken = cancellation
                        )
                    )

                let user: User =
                    { Id = userId; Username = username; PasswordHash = passwordHash; CreatedAt = now; UpdatedAt = now }

                return Ok user
            with ex ->
                return Error(Unexpected ex)
        }
