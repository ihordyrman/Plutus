namespace Plutus.Core.Queries

open System
open System.Data
open System.Threading
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories
open Plutus.Core.Shared

module CoverageHeatmapCache =
    type CachedIntervalData = { Coverage: WeeklyCoverage list; InstrumentCount: int }

    type CachedHeatmapData = { Intervals: Interval list; ByInterval: Map<Interval, CachedIntervalData> }

    [<Literal>]
    let Key = "coverage-heatmap"

    [<Literal>]
    let RefreshIntervalSeconds = 30.0

    let private refresh
        (store: CacheStore.T)
        (scopeFactory: IServiceScopeFactory)
        (_: ILogger)
        (ct: CancellationToken)
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let mutable byInterval = Map.empty

            let! intervals =
                CandlestickRepository.getDistinctIntervals db ct
                |> Task.map (Result.defaultValue [] >> List.map Interval.parse)

            for interval in intervals do
                let! coverage =
                    CandlestickRepository.getWeeklyCoverage db interval ct |> Task.map (Result.defaultValue [])

                let! count =
                    CandlestickRepository.getDistinctInstrumentCount db interval ct |> Task.map (Result.defaultValue 0)

                byInterval <- byInterval |> Map.add interval { Coverage = coverage; InstrumentCount = count }

            store.Set(Key, { Intervals = intervals; ByInterval = byInterval })

        }

    let refresher: CacheRefresher =
        { Key = Key; Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds); Refresh = refresh }
