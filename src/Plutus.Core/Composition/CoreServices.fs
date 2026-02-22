namespace Plutus.Core

open System.Collections.Generic
open System.Text.Json
open Dapper
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
open Plutus.Core.Markets.Stores
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Orchestration
open Plutus.Core.Pipelines.Trading
open Plutus.Core.Workers

module CoreServices =

    type private StringListTypeHandler() =
        inherit SqlMapper.TypeHandler<string list>()

        override _.SetValue(parameter, value) = parameter.Value <- JsonSerializer.Serialize(value)

        override _.Parse(value) =
            match value with
            | :? string as json when not (String.IsNullOrEmpty(json)) -> JsonSerializer.Deserialize<string list>(json)
            | _ -> []

    type private DictionaryStringStringTypeHandler() =
        inherit SqlMapper.TypeHandler<Dictionary<string, string>>()

        override _.SetValue(parameter, value) = parameter.Value <- JsonSerializer.Serialize(value)

        override _.Parse(value) =
            match value with
            | :? string as json when not (String.IsNullOrEmpty(json)) ->
                JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            | _ -> Dictionary<string, string>()

    let private pipelineOrchestrator (services: IServiceCollection) =
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

    let private httpClient (services: IServiceCollection) =
        services.AddScoped<Http.T>(fun provider ->
            let logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxHttp")
            let httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Okx")
            let credentialsStore = provider.GetRequiredService<CredentialsStore.T>()
            Http.create httpClient credentialsStore logger
        )
        |> ignore

    let private balanceManager (services: IServiceCollection) =
        services.AddScoped<BalanceManager.T>(fun provider ->
            let loggerFactory = provider.GetRequiredService<ILoggerFactory>()
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = loggerFactory.CreateLogger("OkxBalanceProvider")
            let okxBalance = BalanceProvider.create okxHttp okxLogger
            BalanceManager.create [ okxBalance ]
        )
        |> ignore

    let private syncJobManager (services: IServiceCollection) =
        services.AddSingleton<SyncJobManager.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<SyncJobManager.T>>()
            SyncJobManager.create scopeFactory logger
        )
        |> ignore

    let private okxWorker (services: IServiceCollection) =
        services.AddHostedService<OkxSynchronizationWorker>() |> ignore

    let private instrumentSyncWorker (services: IServiceCollection) =
        services.AddHostedService<InstrumentSyncWorker>() |> ignore

    let private orderSyncWorker (services: IServiceCollection) = services.AddHostedService<OrderSyncWorker>() |> ignore

    let private orderExecutor (services: IServiceCollection) =
        services.AddScoped<OrderExecutor.T list>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxOrderProvider")
            [ OrderExecutor.create okxHttp okxLogger ]
        )
        |> ignore

    let private orderSyncer (services: IServiceCollection) =
        services.AddScoped<OrderSyncer.T list>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxOrderSyncer")
            [ OrderSyncer.create okxHttp okxLogger ]
        )
        |> ignore

    let private credentialsStore (services: IServiceCollection) =
        services.AddScoped<CredentialsStore.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            CredentialsStore.create scopeFactory
        )
        |> ignore

    let private cacheStore (services: IServiceCollection) =
        services.AddSingleton<CacheStore.T>(CacheStore.T()) |> ignore

    let private cacheWorker (services: IServiceCollection) =
        services.AddHostedService<CacheWorker>(fun provider ->
            let store = provider.GetRequiredService<CacheStore.T>()
            let refreshers = provider.GetService<CacheRefresher list>()
            let refreshers = if isNull (box refreshers) then [] else refreshers
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<CacheWorker>>()
            new CacheWorker(store, refreshers, scopeFactory, logger)
        )
        |> ignore

    let private executionLogger (services: IServiceCollection) =
        services.AddSingleton<ExecutionLogger.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<ExecutionLogger.T>>()

            let getConnection () =
                let scope = scopeFactory.CreateScope()
                scope.ServiceProvider.GetRequiredService<IDbConnection>()

            ExecutionLogger.create getConnection logger
        )
        |> ignore

    let private database (services: IServiceCollection) (configuration: IConfiguration) =
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName))
        |> ignore

        DefaultTypeMap.MatchNamesWithUnderscores <- true
        SqlMapper.AddTypeHandler(StringListTypeHandler())
        SqlMapper.AddTypeHandler(DictionaryStringStringTypeHandler())

        services.AddScoped<IDbConnection>(fun sp ->
            let settings = sp.GetRequiredService<IOptions<DatabaseSettings>>().Value
            new NpgsqlConnection(settings.ConnectionString)
        )
        |> ignore

    let private httpClientFactory (services: IServiceCollection) =
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

    let register (services: IServiceCollection) (configuration: IConfiguration) =
        database services configuration

        [ cacheStore
          cacheWorker
          executionLogger
          balanceManager
          credentialsStore
          httpClientFactory
          httpClient
          instrumentSyncWorker
          okxWorker
          orderExecutor
          orderSyncer
          orderSyncWorker
          pipelineOrchestrator
          syncJobManager ]
        |> List.iter (fun addService -> addService services)

    let registerSlim (services: IServiceCollection) (configuration: IConfiguration) =
        database services configuration

        [ credentialsStore; httpClientFactory; httpClient; orderExecutor ]
        |> List.iter (fun addService -> addService services)
