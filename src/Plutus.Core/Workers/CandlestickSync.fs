namespace Plutus.Core.Workers

open System
open System.Collections.Generic
open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories
open Plutus.Core.Shared
open Plutus.Core.Shared.Errors

module CandlestickSync =

    let private recentCandleThreshold = TimeSpan.FromMinutes 5.0
    let private historyBoundaryDays = 30

    let toCandlestick (instrument: Instrument) (interval: Interval) (c: OkxCandlestick) : Candlestick =
        { Id = 0
          Instrument = instrument
          MarketType = MarketType.Okx
          Timestamp = c.Timestamp
          Open = c.Open
          High = c.High
          Low = c.Low
          Close = c.Close
          Volume = c.Volume
          VolumeQuote = c.VolumeQuoteCurrency
          IsCompleted = c.IsCompleted
          Interval = interval }

    let private fetchAndSave
        (fetch: string -> Http.CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>)
        (db: IDbConnection)
        (logger: ILogger)
        (instrument: Instrument)
        (afterMs: string option)
        (beforeMs: string option)
        (ct: CancellationToken)
        =
        task {
            let! result =
                fetch
                    (string instrument)
                    { Bar = Some Interval.OneMinute; After = afterMs; Before = beforeMs; Limit = Some 100 }

            match result with
            | Ok candles when candles.Length > 0 ->
                let mapped = candles |> Array.map (toCandlestick instrument Interval.OneMinute) |> Array.toList
                let! _ = CandlestickRepository.save db mapped ct
                return candles.Length
            | Ok _ -> return 0
            | Error err ->
                logger.LogError("Fetch failed for {Instrument}: {Error}", instrument, serviceMessage err)
                return 0
        }

    let private pageBackward
        (fetch: string -> Http.CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>)
        (db: IDbConnection)
        (logger: ILogger)
        (instrument: Instrument)
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
                let! count = fetchAndSave fetch db logger instrument (Some afterMs) None ct

                if count = 0 then
                    keepGoing <- false
                else
                    total <- total + count
                    cursor <- cursor.AddMinutes(-100.0 |> float)

            return total
        }

    let syncRecent
        (http: Http.T)
        (db: IDbConnection)
        (logger: ILogger)
        (instrument: Instrument)
        (ct: CancellationToken)
        =
        task {
            match! CandlestickRepository.getLatest db instrument MarketType.Okx Interval.OneMinute ct with
            | Ok(Some latest) ->
                let gap = DateTime.UtcNow - latest.Timestamp

                if gap <= recentCandleThreshold then
                    let afterMs = DateTimeOffset(latest.Timestamp).ToUnixTimeMilliseconds().ToString()

                    let! _ = fetchAndSave http.getCandlesticks db logger instrument (Some afterMs) None ct
                    ()
                else
                    let! count =
                        pageBackward
                            http.getCandlesticks
                            db
                            logger
                            instrument
                            DateTimeOffset.UtcNow
                            (DateTimeOffset(latest.Timestamp))
                            ct

                    if count > 0 then
                        logger.LogInformation(
                            "Recent catchup for {Instrument}: {Count} candles saved",
                            instrument,
                            count
                        )
            | Ok None ->
                let! _ = fetchAndSave http.getCandlesticks db logger instrument None None ct
                ()
            | Error err ->
                logger.LogError("Failed to get latest candle for {Instrument}: {Error}", instrument, serviceMessage err)
        }

    let syncHistory
        (http: Http.T)
        (db: IDbConnection)
        (logger: ILogger)
        (instrument: Instrument)
        (ct: CancellationToken)
        =
        task {
            let monthAgo = DateTimeOffset.UtcNow.AddDays(-30)

            match! CandlestickRepository.getOldest db instrument MarketType.Okx Interval.OneMinute ct with
            | Ok(Some oldest) when DateTimeOffset(oldest.Timestamp) <= monthAgo -> ()
            | Ok maybeOldest ->
                let startFrom =
                    match maybeOldest with
                    | Some oldest -> DateTimeOffset(oldest.Timestamp)
                    | None -> DateTimeOffset.UtcNow

                let! count = pageBackward http.getHistoryCandlesticks db logger instrument startFrom monthAgo ct

                if count > 0 then
                    logger.LogInformation("History backfill for {Instrument}: {Count} candles saved", instrument, count)
            | Error err ->
                logger.LogError("Failed to get oldest candle for {Instrument}: {Error}", instrument, serviceMessage err)
        }

    let syncGaps (http: Http.T) (db: IDbConnection) (logger: ILogger) (instrument: Instrument) (ct: CancellationToken) =
        task {
            match! CandlestickRepository.findGaps db instrument MarketType.Okx Interval.OneMinute ct with
            | Ok gaps when gaps.Length > 0 ->
                logger.LogInformation("Found {Count} gaps for {Instrument}", gaps.Length, instrument)

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

                        let! _ = pageBackward fetch db logger instrument gapEnd gapStart ct
                        ()
            | Ok _ -> ()
            | Error err ->
                logger.LogError("Failed to find gaps for {Instrument}: {Error}", instrument, serviceMessage err)
        }

    let getEnabledInstruments (db: IDbConnection) (ct: CancellationToken) =
        task {
            let instruments = HashSet<Instrument>()

            match! PipelineRepository.getAll db ct with
            | Ok pipelines ->
                for pi in pipelines do
                    let! steps = PipelineStepRepository.getByPipelineId db pi.Id ct

                    match steps with
                    | Error _ -> ()
                    | Ok steps ->
                        steps
                        |> List.filter (fun x -> x.StepTypeKey = "trend-following-signal")
                        |> List.iter (fun x ->
                            match x.Parameters.TryGetValue("instruments") with
                            | true, s ->
                                s.Split(';')
                                |> Array.iter (fun s -> instruments.Add(Instrument.parse (s.Trim())) |> ignore)
                            | _ -> ()
                        )

                pipelines
                |> List.map _.Instrument
                |> List.distinct
                |> List.iter (fun i -> instruments.Add i |> ignore)

                return instruments |> Seq.toArray
            | Error _ -> return Array.empty
        }

type OkxSynchronizationWorker(scopeFactory: IServiceScopeFactory, logger: ILogger<OkxSynchronizationWorker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct) =
        task {
            use scope = scopeFactory.CreateScope()
            let http = scope.ServiceProvider.GetRequiredService<Http.T>()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let! instruments = CandlestickSync.getEnabledInstruments db ct

            for instrument in instruments do
                if not ct.IsCancellationRequested then
                    do! CandlestickSync.syncHistory http db logger instrument ct
                    do! CandlestickSync.syncGaps http db logger instrument ct

            logger.LogInformation("Initial candlestick sync complete")

            use timer = new PeriodicTimer(TimeSpan.FromMinutes 1.0)
            let mutable tickCount = 0

            while not ct.IsCancellationRequested do
                try
                    let! _ = timer.WaitForNextTickAsync(ct)
                    tickCount <- tickCount + 1
                    let! currentInstruments = CandlestickSync.getEnabledInstruments db ct

                    for instrument in currentInstruments do
                        if not ct.IsCancellationRequested then
                            do! CandlestickSync.syncRecent http db logger instrument ct

                    if tickCount % 60 = 0 then
                        for instrument in currentInstruments do
                            if not ct.IsCancellationRequested then
                                do! CandlestickSync.syncGaps http db logger instrument ct
                                do! CandlestickSync.syncHistory http db logger instrument ct
                with
                | :? OperationCanceledException -> ()
                | ex -> logger.LogError(ex, "Error in candlestick sync loop")
        }
