namespace Plutus.Core.Identity.UpdateLastUsed

open System
open System.Data
open Dapper
open Plutus.Core.Identity.Domain
open Plutus.Core.Identity.Ports
open Plutus.Core.Shared.Errors

module internal Adapter =
    let updateLastUsed (db: IDbConnection) : UpdateLastUsed =
        fun id token ->
            task {
                try
                    let now = DateTime.UtcNow
                    let keyId = KeyId.value id

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE api_keys SET last_used = @Now WHERE id = @Id",
                                {| Now = now; Id = keyId |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }
