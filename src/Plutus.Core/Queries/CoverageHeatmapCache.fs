namespace Plutus.Core.Queries

open System
open System.Data
open System.Threading
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

    let private refresh
        (store: CacheStore.T)
        (scopeFactory: IServiceScopeFactory)
        (_: ILogger)
        (ct: CancellationToken)
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let! intervalsResult = CandlestickRepository.getDistinctIntervals db ct

            let intervals =
                match intervalsResult with
                | Ok intervals -> intervals |> List.map Interval.parse
                | Error _ -> []

            let mutable byInterval = Map.empty

            for interval in intervals do
                let! coverageResult = CandlestickRepository.getWeeklyCoverage db interval ct

                let coverage =
                    match coverageResult with
                    | Ok c -> c
                    | Error _ -> []

                let! countResult = CandlestickRepository.getDistinctInstrumentCount db interval ct

                let count =
                    match countResult with
                    | Ok c -> c
                    | Error _ -> 0

                byInterval <- byInterval |> Map.add interval { Coverage = coverage; InstrumentCount = count }

            store.Set(Key, { Intervals = intervals; ByInterval = byInterval })
        }

    let refresher: CacheRefresher = { Key = Key; Interval = TimeSpan.FromSeconds(30.0); Refresh = refresh }
