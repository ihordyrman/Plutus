namespace Plutus.Core.Queries

open System
open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories

module CoverageHeatmapCache =
    type CachedTimeframeData = { Coverage: WeeklyCoverage list; InstrumentCount: int }

    type CachedHeatmapData = { Timeframes: string list; ByTimeframe: Map<string, CachedTimeframeData> }

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

            let! timeframesResult = CandlestickRepository.getDistinctTimeframes db ct

            let timeframes =
                match timeframesResult with
                | Ok tfs -> tfs
                | Error _ -> []

            let mutable byTimeframe = Map.empty

            for tf in timeframes do
                let! coverageResult = CandlestickRepository.getWeeklyCoverage db tf ct

                let coverage =
                    match coverageResult with
                    | Ok c -> c
                    | Error _ -> []

                let! countResult = CandlestickRepository.getDistinctInstrumentCount db tf ct

                let count =
                    match countResult with
                    | Ok c -> c
                    | Error _ -> 0

                byTimeframe <- byTimeframe |> Map.add tf { Coverage = coverage; InstrumentCount = count }

            store.Set(Key, { Timeframes = timeframes; ByTimeframe = byTimeframe })
        }

    let refresher: CacheRefresher = { Key = Key; Interval = TimeSpan.FromSeconds(30.0); Refresh = refresh }
