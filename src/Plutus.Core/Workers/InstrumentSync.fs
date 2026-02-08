namespace Plutus.Core.Workers

open System
open System.Data
open System.Net.Http
open System.Text.Json
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories

type InstrumentSyncWorker(scopeFactory: IServiceScopeFactory, httpFactory: IHttpClientFactory, logger: ILogger<InstrumentSyncWorker>) =
    inherit BackgroundService()

    let jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

    let syncInstruments (ct: CancellationToken) =
        task {
            try
                let client = httpFactory.CreateClient("Okx")
                let! response = client.GetAsync("api/v5/public/instruments?instType=SPOT", ct)

                if response.IsSuccessStatusCode then
                    let! json = response.Content.ReadAsStringAsync(ct)
                    let parsed = JsonSerializer.Deserialize<OkxHttpResponse<OkxInstrument[]>>(json, jsonOpts)

                    match parsed.Data with
                    | Some instruments ->
                        let now = DateTime.UtcNow

                        let mapped =
                            instruments
                            |> Array.map (fun i ->
                                { Id = 0
                                  InstrumentId = i.InstrumentId
                                  InstrumentType = i.InstrumentType
                                  BaseCurrency = i.BaseCurrency
                                  QuoteCurrency = i.QuoteCurrency
                                  MarketType = int MarketType.Okx
                                  SyncedAt = now
                                  CreatedAt = now }
                            )
                            |> Array.toList

                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                        let! result = InstrumentRepository.upsertBatch db mapped ct

                        match result with
                        | Ok count -> logger.LogInformation("Synced {Count} OKX SPOT instruments", count)
                        | Error err -> logger.LogError("Failed to upsert instruments: {Error}", err)
                    | None ->
                        logger.LogWarning("OKX instruments response had no data: {Message}", parsed.Message)
                else
                    logger.LogWarning("OKX instruments API returned {StatusCode}", int response.StatusCode)
            with ex ->
                logger.LogError(ex, "Instrument sync failed")
        }

    override _.ExecuteAsync(ct) =
        task {
            do! syncInstruments ct

            use timer = new PeriodicTimer(TimeSpan.FromHours 24.0)

            while not ct.IsCancellationRequested do
                let! _ = timer.WaitForNextTickAsync(ct)
                do! syncInstruments ct
        }
