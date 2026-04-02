namespace Plutus.Core.Identity

open System.Data
open Dapper
open Plutus.Core.Identity.Entities
open Plutus.Core.Identity.Helpers
open Plutus.Core.Identity.Domain
open Plutus.Core.Identity.Ports
open Plutus.Core.Shared.Errors

module internal Adapter =
    let getByHash (db: IDbConnection) : GetByHash =
        fun hash token ->
            task {
                try
                    let hash = KeyHash.value hash

                    let! keys =
                        db.QueryAsync<ApiKey>(
                            CommandDefinition(
                                "SELECT * FROM api_keys WHERE key_hash = @Hash AND is_active = true",
                                {| Hash = hash |},
                                cancellationToken = token
                            )
                        )

                    match keys |> Seq.tryHead with
                    | None -> return Ok None
                    | Some key -> return mapApiKey key
                with ex ->
                    return Error(Unexpected ex)
            }
