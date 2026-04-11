namespace Plutus.Identity.GetByHash

open System.Data
open Dapper
open Plutus.Identity.Entities
open Plutus.Identity.Helpers
open Plutus.Identity.Domain
open Plutus.Identity.Ports
open Plutus.Shared.Errors

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
