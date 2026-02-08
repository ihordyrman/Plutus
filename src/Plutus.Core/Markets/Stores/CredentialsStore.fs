namespace Plutus.Core.Markets.Stores

open System.Collections.Generic
open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Repositories
open Plutus.Core.Shared

module CredentialsStore =
    open Errors

    type Credentials = { Key: string; Secret: string; Passphrase: string option; IsSandbox: bool }

    let private cache = Dictionary<MarketType, Credentials>()

    let private getCached (marketType: MarketType) : Credentials option =
        let mutable creds = Unchecked.defaultof<Credentials>

        match cache.TryGetValue(marketType, &creds) with
        | true -> Some creds
        | false -> None

    type T = { GetCredentials: MarketType -> CancellationToken -> Task<Result<Credentials, ServiceError>> }

    let create (scopeFactory: IServiceScopeFactory) : T =
        { GetCredentials =
            fun marketType ct ->
                use scope = scopeFactory.CreateScope()
                let loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                let logger = loggerFactory.CreateLogger("CredentialsStore")

                task {
                    try
                        match getCached marketType with
                        | None ->
                            use scope = scope.ServiceProvider.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                            let! results = MarketRepository.getByType db marketType ct

                            match results with
                            | Error err ->
                                logger.LogError(
                                    "Error retrieving market credentials for {MarketType}: {Error}",
                                    marketType,
                                    err
                                )

                                return Error(NotFound($"Error retrieving market credentials: {err}"))
                            | Ok None ->
                                logger.LogWarning("No credentials found for {MarketType}", marketType)
                                return Error(NotFound($"No credentials found for market type {marketType}"))
                            | Ok(Some credentials) ->
                                logger.LogDebug("Retrieved credentials for {MarketType}", credentials.Type)

                                return
                                    Ok(
                                        { Key = credentials.ApiKey
                                          Secret = credentials.SecretKey
                                          Passphrase =
                                            if credentials.Passphrase = Some "" then None else credentials.Passphrase
                                          IsSandbox = credentials.IsSandbox }
                                    )
                        | Some creds ->
                            logger.LogDebug("Credentials for {MarketType} found in cache", marketType)
                            return Ok creds

                    with ex ->
                        logger.LogError(ex, "Failed to get credentials for {MarketType}", marketType)
                        return Error(NotFound($"Failed to get credentials: {ex.Message}"))
                } }
