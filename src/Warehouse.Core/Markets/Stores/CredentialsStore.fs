namespace Warehouse.Core.Markets.Stores

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Repositories
open Warehouse.Core.Shared

module CredentialsStore =
    open Errors

    type Credentials = { Key: string; Secret: string; Passphrase: string option; IsSandbox: bool }

    type T = { GetCredentials: MarketType -> CancellationToken -> Task<Result<Credentials, ServiceError>> }

    let create (scopeFactory: IServiceScopeFactory) : T =
        { GetCredentials =
            fun marketType _ ->
                use scope = scopeFactory.CreateScope()
                let loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                let logger = loggerFactory.CreateLogger("CredentialsStore")

                task {
                    try
                        use scope = scope.ServiceProvider.CreateScope()
                        let repo = scope.ServiceProvider.GetRequiredService<MarketRepository.T>()

                        let! results = repo.GetByType marketType CancellationToken.None

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
                    with ex ->
                        logger.LogError(ex, "Failed to get credentials for {MarketType}", marketType)
                        return Error(NotFound($"Failed to get credentials: {ex.Message}"))
                } }
