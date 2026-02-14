namespace Plutus.Core.Workers

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

module CandlestickSync =

    let private historyDays = 365
    let private recentCandleThreshold = TimeSpan.FromMinutes 5.0
    let private historyBoundaryDays = 30

    let toCandlestick (symbol: string) (timeframe: string) (c: OkxCandlestick) : Candlestick =
        { Id = 0
          Symbol = symbol
          MarketType = int MarketType.Okx
          Timestamp = c.Timestamp
          Open = c.Open
          High = c.High
          Low = c.Low
          Close = c.Close
          Volume = c.Volume
          VolumeQuote = c.VolumeQuoteCurrency
          IsCompleted = c.IsCompleted
          Timeframe = timeframe }

    let private fetchAndSave
        (fetch: string -> Http.CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>)
        (db: IDbConnection)
        (logger: ILogger)
        (symbol: string)
        (afterMs: string option)
        (beforeMs: string option)
        (ct: CancellationToken)
        =
        task {
            let! result = fetch symbol { Bar = Some "1m"; After = afterMs; Before = beforeMs; Limit = Some 100 }

            match result with
            | Ok candles when candles.Length > 0 ->
                let mapped = candles |> Array.map (toCandlestick symbol "1m") |> Array.toList
                let! _ = CandlestickRepository.save db mapped ct
                return candles.Length
            | Ok _ -> return 0
            | Error err ->
                logger.LogError("Fetch failed for {Symbol}: {Error}", symbol, serviceMessage err)
                return 0
        }

    let private pageBackward
        (fetch: string -> Http.CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>)
        (db: IDbConnection)
        (logger: ILogger)
        (symbol: string)
        (startTs: DateTimeOffset)
        (stopTs: DateTimeOffset)
        (ct: CancellationToken)
        =
        task {
            let mutable cursor = startTs
            let mutable total = 0
            let mutable keepGoing = true

            while keepGoing && not ct.IsCancellationRequested && cursor > stopTs do
                let afterMs = cursor.ToUnixTimeMilliseconds().ToString()
                let! count = fetchAndSave fetch db logger symbol (Some afterMs) None ct

                if count = 0 then
                    keepGoing <- false
                else
                    total <- total + count
                    cursor <- cursor.AddMinutes(-100.0 |> float)

            return total
        }

    let syncRecent (http: Http.T) (db: IDbConnection) (logger: ILogger) (symbol: string) (ct: CancellationToken) =
        task {
            match! CandlestickRepository.getLatest db symbol MarketType.Okx "1m" ct with
            | Ok(Some latest) ->
                let gap = DateTime.UtcNow - latest.Timestamp

                if gap <= recentCandleThreshold then
                    let afterMs = DateTimeOffset(latest.Timestamp).ToUnixTimeMilliseconds().ToString()

                    let! _ = fetchAndSave http.getCandlesticks db logger symbol (Some afterMs) None ct
                    ()
                else
                    let! count =
                        pageBackward
                            http.getCandlesticks
                            db
                            logger
                            symbol
                            DateTimeOffset.UtcNow
                            (DateTimeOffset(latest.Timestamp))
                            ct

                    if count > 0 then
                        logger.LogInformation("Recent catchup for {Symbol}: {Count} candles saved", symbol, count)
            | Ok None ->
                let! _ = fetchAndSave http.getCandlesticks db logger symbol None None ct
                ()
            | Error err ->
                logger.LogError("Failed to get latest candle for {Symbol}: {Error}", symbol, serviceMessage err)
        }

    let syncHistory (http: Http.T) (db: IDbConnection) (logger: ILogger) (symbol: string) (ct: CancellationToken) =
        task {
            let oneYearAgo = DateTimeOffset.UtcNow.AddDays(-historyDays)

            match! CandlestickRepository.getOldest db symbol MarketType.Okx "1m" ct with
            | Ok(Some oldest) when DateTimeOffset(oldest.Timestamp) <= oneYearAgo -> ()
            | Ok maybeOldest ->
                let startFrom =
                    match maybeOldest with
                    | Some oldest -> DateTimeOffset(oldest.Timestamp)
                    | None -> DateTimeOffset.UtcNow

                let! count = pageBackward http.getHistoryCandlesticks db logger symbol startFrom oneYearAgo ct

                if count > 0 then
                    logger.LogInformation("History backfill for {Symbol}: {Count} candles saved", symbol, count)
            | Error err ->
                logger.LogError("Failed to get oldest candle for {Symbol}: {Error}", symbol, serviceMessage err)
        }

    let syncGaps (http: Http.T) (db: IDbConnection) (logger: ILogger) (symbol: string) (ct: CancellationToken) =
        task {
            match! CandlestickRepository.findGaps db symbol MarketType.Okx "1m" ct with
            | Ok gaps when gaps.Length > 0 ->
                logger.LogInformation("Found {Count} gaps for {Symbol}", gaps.Length, symbol)

                for gap in gaps do
                    if not ct.IsCancellationRequested then
                        let gapEnd = DateTimeOffset(gap.GapEnd)
                        let gapStart = DateTimeOffset(gap.GapStart)
                        let daysAgo = (DateTimeOffset.UtcNow - gapEnd).TotalDays

                        let fetch =
                            if daysAgo > float historyBoundaryDays then
                                http.getHistoryCandlesticks
                            else
                                http.getCandlesticks

                        let! _ = pageBackward fetch db logger symbol gapEnd gapStart ct
                        ()
            | Ok _ -> ()
            | Error err -> logger.LogError("Failed to find gaps for {Symbol}: {Error}", symbol, serviceMessage err)
        }

    let getEnabledSymbols (db: IDbConnection) (ct: CancellationToken) =
        task {
            match! PipelineRepository.getEnabled db ct with
            | Ok pipelines -> return pipelines |> List.map _.Symbol |> List.distinct |> Array.ofList
            | Error _ -> return Array.empty
        }


type OkxSynchronizationWorker(scopeFactory: IServiceScopeFactory, logger: ILogger<OkxSynchronizationWorker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct) =
        task {
            use scope = scopeFactory.CreateScope()
            let http = scope.ServiceProvider.GetRequiredService<Http.T>()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let! symbols = CandlestickSync.getEnabledSymbols db ct

            for symbol in symbols do
                if not ct.IsCancellationRequested then
                    do! CandlestickSync.syncHistory http db logger symbol ct
                    do! CandlestickSync.syncGaps http db logger symbol ct

            logger.LogInformation("Initial candlestick sync complete")

            use timer = new PeriodicTimer(TimeSpan.FromMinutes 1.0)
            let mutable tickCount = 0

            while not ct.IsCancellationRequested do
                try
                    let! _ = timer.WaitForNextTickAsync(ct)
                    tickCount <- tickCount + 1
                    let! currentSymbols = CandlestickSync.getEnabledSymbols db ct

                    for symbol in currentSymbols do
                        if not ct.IsCancellationRequested then
                            do! CandlestickSync.syncRecent http db logger symbol ct

                    if tickCount % 60 = 0 then
                        for symbol in currentSymbols do
                            if not ct.IsCancellationRequested then
                                do! CandlestickSync.syncGaps http db logger symbol ct
                                do! CandlestickSync.syncHistory http db logger symbol ct
                with
                | :? OperationCanceledException -> ()
                | ex -> logger.LogError(ex, "Error in candlestick sync loop")
        }
