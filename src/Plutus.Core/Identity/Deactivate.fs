namespace Plutus.Core.Identity

open System.Data
open Dapper
open Plutus.Core.Identity.Domain
open Plutus.Core.Identity.Ports
open Plutus.Core.Shared.Errors

module internal Adapter =
    let deactivate (db: IDbConnection) : Deactivate =
        fun id token ->
            task {
                try
                    let id = KeyId.value id

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE api_keys SET is_active = false WHERE id = @Id",
                                {| Id = id |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then
                        return Ok()
                    else
                        return Error(NotFound $"API key with id {id} not found")
                with ex ->
                    return Error(Unexpected ex)
            }
