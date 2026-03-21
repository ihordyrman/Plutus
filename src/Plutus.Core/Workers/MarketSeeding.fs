namespace Plutus.Core.Workers

open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Plutus.Core.Domain
open Plutus.Core.Infrastructure

type MarketSeedingWorker
    (
        scopeFactory: IServiceScopeFactory,
        creds: IOptions<MarketSettings>,
        logger: ILogger<MarketSeedingWorker>
    ) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            for c in creds.Value.Credentials do
                match System.Enum.TryParse<MarketType> c.MarketType with
                | true, marketType ->
                    match! MarketRepository.ensureExists db marketType ct with
                    | Ok true -> logger.LogInformation("Created market {MarketType}", c.MarketType)
                    | Ok false -> ()
                    | Error err -> logger.LogError("Failed to seed market {MarketType}: {Error}", c.MarketType, err)
                | false, _ -> logger.LogWarning("Unknown market type in credentials: {MarketType}", c.MarketType)
        }
