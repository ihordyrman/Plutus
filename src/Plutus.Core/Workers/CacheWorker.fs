namespace Plutus.Core.Workers

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure

type CacheWorker
    (
        store: CacheStore.T,
        refreshers: CacheRefresher list,
        scopeFactory: IServiceScopeFactory,
        logger: ILogger<CacheWorker>
    ) =
    inherit BackgroundService()

    let lastRefresh = ConcurrentDictionary<string, DateTime>()

    override _.ExecuteAsync(ct) =
        task {
            while not ct.IsCancellationRequested do
                let now = DateTime.UtcNow

                for refresher in refreshers do
                    let last =
                        match lastRefresh.TryGetValue(refresher.Key) with
                        | true, v -> v
                        | _ -> DateTime.MinValue

                    if now - last >= refresher.Interval then
                        try
                            do! refresher.Refresh store scopeFactory logger ct
                            lastRefresh[refresher.Key] <- now
                            logger.LogDebug("Cache refreshed: {Key}", refresher.Key)
                        with ex ->
                            logger.LogError(ex, "Cache refresh failed: {Key}", refresher.Key)

                do! Task.Delay(TimeSpan.FromSeconds(1.0), ct)
        }
