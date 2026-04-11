namespace Plutus.Identity.Authorize

open FsToolkit.ErrorHandling
open Plutus.Shared
open Plutus.Shared.Errors
open Plutus.Identity.Domain
open Plutus.Identity.Ports

module internal Adapter =
    let authorize (getByHash: GetByHash) (updateLastUsed: UpdateLastUsed) : AuthorizeApiKey =
        fun token ct ->
            taskResult {
                let hash = Authentication.computeSha256 token

                let! keyHash =
                    KeyHash.create hash
                    |> Result.mapError (fun _ -> Validation "Invalid API key format")

                let! key = getByHash keyHash ct

                match key with
                | Some k when k.IsActive ->
                    do! updateLastUsed k.Id ct
                    return ()
                | _ -> return! Error(Unathorized "API key not found or inactive")
            }
