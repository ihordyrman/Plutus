namespace Plutus.Core.Workers

open System
open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

module CandlestickSync =

    let toPairSymbol (instrument: Instrument) = { Left = instrument; Right = Instrument.USDT }.ToString()

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

    let sync (http: Http.T) (db: IDbConnection) (logger: ILogger) (symbol: string) (afterMs: string) (limit: int) ct =
        task {
            let! result =
                http.getCandlesticks symbol { Bar = Some "1m"; Before = None; After = Some afterMs; Limit = Some limit }

            match result with
            | Ok candles when candles.Length > 0 ->
                let mapped = candles |> Array.map (toCandlestick symbol "1m") |> Array.toList
                let! _ = CandlestickRepository.save db mapped ct
                ()
            | Ok _ -> ()
            | Error err -> logger.LogError("Sync failed for {Symbol}: {Error}", symbol, serviceMessage err)
        }


type OkxSynchronizationWorker(scopeFactory: IServiceScopeFactory, logger: ILogger<OkxSynchronizationWorker>) =
    inherit BackgroundService()

    let symbols =
        [| Instrument.BTC
           Instrument.ETH
           Instrument.SOL
           Instrument.OKB
           Instrument.DOGE
           Instrument.XRP
           Instrument.BCH
           Instrument.LTC |]

    override _.ExecuteAsync(ct) =
        task {
            use scope = scopeFactory.CreateScope()
            let http = scope.ServiceProvider.GetRequiredService<Http.T>()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            [| for i in 0..23 do
                   DateTimeOffset.UtcNow.AddHours(-i).ToUnixTimeMilliseconds().ToString() |]
            |> Array.rev
            |> Array.iter (fun after ->
                for instrument in symbols do
                    CandlestickSync.sync http db logger (CandlestickSync.toPairSymbol instrument) after 60 ct
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
            )

            logger.LogInformation("Initial sync complete")
            use timer = new PeriodicTimer(TimeSpan.FromMinutes 1.0)

            while not ct.IsCancellationRequested do
                let! _ = timer.WaitForNextTickAsync(ct)

                do
                    symbols
                    |> Array.iter (fun instrument ->
                        CandlestickSync.sync
                            http
                            db
                            logger
                            (CandlestickSync.toPairSymbol instrument)
                            (DateTimeOffset.UtcNow.AddMinutes(-1.0).ToUnixTimeMilliseconds().ToString())
                            10
                            ct
                        |> Async.AwaitTask
                        |> Async.RunSynchronously
                    )
        }
