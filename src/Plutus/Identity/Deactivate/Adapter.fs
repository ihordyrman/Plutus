namespace Plutus.Identity.Deactivate

open System.Data
open Dapper
open Plutus.Identity.Domain
open Plutus.Identity.Ports
open Plutus.Shared.Errors

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
