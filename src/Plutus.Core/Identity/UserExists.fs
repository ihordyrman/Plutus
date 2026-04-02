namespace Plutus.Core.Identity

open System.Data
open Dapper
open Plutus.Core.Identity.Ports
open Plutus.Core.Shared.Errors

module internal Adapter =
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
