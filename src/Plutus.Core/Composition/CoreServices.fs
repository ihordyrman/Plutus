namespace Plutus.Core

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Npgsql
open Polly
open System
open System.Data
open System.Net
open System.Net.Http
open Plutus.Core.Infrastructure
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Markets.Services
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Orchestration
open Plutus.Core.Pipelines.Trading
open Plutus.Core.Workers

module CoreServices =

    let private useConfiguration (services: IServiceCollection) (configuration: IConfiguration) =
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName))
        |> ignore

        services.Configure<MarketCredentialsSettings>(configuration.GetSection(MarketCredentialsSettings.SectionName))
        |> ignore

    let private usePipelineOrchestrator (services: IServiceCollection) =
        services.AddSingleton<Registry.T<TradingContext>>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()

            TradingSteps.all (LiveAdapters.getPosition scopeFactory) (LiveAdapters.tradeExecutor scopeFactory)
            |> Registry.create
        )
        |> ignore

        services.AddHostedService<Orchestrator.Worker>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<Orchestrator.Worker>>()
            let registry = provider.GetRequiredService<Registry.T<TradingContext>>()
            new Orchestrator.Worker(scopeFactory, logger, registry)
        )
        |> ignore

    let private useHttpClient (services: IServiceCollection) =
        services.AddScoped<Http.T>(fun provider ->
            let logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxHttp")
            let httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Okx")
            let creds = provider.GetRequiredService<IOptions<MarketCredentialsSettings>>().Value
            let creds = creds.Credentials |> Array.find (fun x -> x.MarketType = "Okx")

            Http.create httpClient creds logger
        )
        |> ignore

    let private useBalanceManager (services: IServiceCollection) =
        services.AddScoped<BalanceManager.T>(fun provider ->
            let loggerFactory = provider.GetRequiredService<ILoggerFactory>()
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = loggerFactory.CreateLogger("OkxBalanceProvider")
            let okxBalance = BalanceProvider.create okxHttp okxLogger
            BalanceManager.create [ okxBalance ]
        )
        |> ignore

    let private useSyncJobManager (services: IServiceCollection) =
        services.AddSingleton<JobsManager.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<JobsManager.T>>()
            JobsManager.create scopeFactory logger
        )
        |> ignore

    let private useOkxWorker (services: IServiceCollection) =
        services.AddHostedService<OkxSynchronizationWorker>() |> ignore

    let private useInstrumentSyncWorker (services: IServiceCollection) =
        services.AddHostedService<InstrumentSyncWorker>() |> ignore

    let private useOrderSyncWorker (services: IServiceCollection) =
        services.AddHostedService<OrderSyncWorker>() |> ignore

    let private useOrderExecutor (services: IServiceCollection) =
        services.AddScoped<OrderExecutor.T list>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxOrderProvider")
            [ OrderExecutor.create okxHttp okxLogger ]
        )
        |> ignore

    let private useOrderSyncer (services: IServiceCollection) =
        services.AddScoped<OrderSyncer.T list>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxOrderSyncer")
            [ OrderSyncer.create okxHttp okxLogger ]
        )
        |> ignore

    let private useCacheStore (services: IServiceCollection) =
        services.AddSingleton<CacheStore.T>(CacheStore.T()) |> ignore

    let private useCacheWorker (services: IServiceCollection) =
        services.AddHostedService<CacheWorker>(fun provider ->
            let store = provider.GetRequiredService<CacheStore.T>()
            let refreshers = provider.GetService<CacheRefresher list>()
            let refreshers = if isNull (box refreshers) then [] else refreshers
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<CacheWorker>>()
            new CacheWorker(store, refreshers, scopeFactory, logger)
        )
        |> ignore

    let private useExecuteLogger (services: IServiceCollection) =
        services.AddSingleton<ExecutionLogger.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<ExecutionLogger.T>>()

            let getConnection () =
                let scope = scopeFactory.CreateScope()
                scope.ServiceProvider.GetRequiredService<IDbConnection>()

            ExecutionLogger.create getConnection logger
        )
        |> ignore

    let private useDatabase (services: IServiceCollection) =
        TypeHandlers.registerTypeHandlers ()

        services.AddScoped<IDbConnection>(fun sp ->
            let settings = sp.GetRequiredService<IOptions<DatabaseSettings>>().Value
            new NpgsqlConnection(settings.ConnectionString)
        )
        |> ignore

    let private useHttpClientFactory (services: IServiceCollection) =
        services
            .AddHttpClient("Okx")
            .ConfigureHttpClient(fun (client: HttpClient) ->
                client.BaseAddress <- Uri("https://www.okx.com/")
                client.Timeout <- TimeSpan.FromSeconds(300.0)
                client.DefaultRequestHeaders.Add("User-Agent", "Plutus/1.0")
            )
            .ConfigurePrimaryHttpMessageHandler(
                Func<HttpMessageHandler>(fun () ->
                    new HttpClientHandler(
                        AutomaticDecompression = (DecompressionMethods.GZip ||| DecompressionMethods.Deflate)
                    )
                )
            )
            .AddPolicyHandler(fun (provider: IServiceProvider) (_: HttpRequestMessage) ->
                let logger = provider.GetRequiredService<ILogger<HttpClient>>()

                Policy
                    .HandleResult<HttpResponseMessage>(fun (r: HttpResponseMessage) -> not r.IsSuccessStatusCode)
                    .WaitAndRetryAsync(
                        3,
                        (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2.0, float retryAttempt))),
                        (fun result timespan retryCount _ ->
                            let response = result.Result
                            let statusCode = int response.StatusCode
                            let uri = response.RequestMessage.RequestUri

                            logger.LogWarning(
                                "Retry attempt {RetryCount} after {TimeSpan} seconds due to HTTP {StatusCode} from {ReasonPhrase}",
                                retryCount,
                                timespan,
                                statusCode,
                                uri
                            )
                        )
                    )
                :> IAsyncPolicy<HttpResponseMessage>
            )
            .AddPolicyHandler(fun _ ->
                Policy.TimeoutAsync<HttpResponseMessage>(10) :> IAsyncPolicy<HttpResponseMessage>
            )
        |> ignore

    let private useMarketSeeding (services: IServiceCollection) =
        services.AddHostedService<MarketSeedingWorker>() |> ignore

    let register (services: IServiceCollection) (config: IConfiguration) =
        useConfiguration services config
        useDatabase services

        [ useMarketSeeding
          useCacheStore
          useCacheWorker
          useExecuteLogger
          useBalanceManager
          useHttpClientFactory
          useHttpClient
          useInstrumentSyncWorker
          useOkxWorker
          useOrderExecutor
          useOrderSyncer
          useOrderSyncWorker
          usePipelineOrchestrator
          useSyncJobManager ]
        |> List.iter (fun f -> f services)

    let registerSlim (services: IServiceCollection) (config: IConfiguration) =
        useConfiguration services config
        useDatabase services

        [ useHttpClientFactory; useHttpClient; useOrderExecutor ]
        |> List.iter (fun addService -> addService services)
