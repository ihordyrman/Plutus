namespace Warehouse.Core

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
open Warehouse.Core.Infrastructure
open Warehouse.Core.Markets.Abstractions
open Warehouse.Core.Markets.Exchanges.Okx
open Warehouse.Core.Markets.Services
open Warehouse.Core.Markets.Stores
open Warehouse.Core.Pipelines.Core
open Warehouse.Core.Pipelines.Orchestration
open Warehouse.Core.Pipelines.Trading
open Warehouse.Core.Repositories
open Warehouse.Core.Workers

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

    let private heartbeat (services: IServiceCollection) =
        services.AddSingleton<Heartbeat.T>(fun provider ->
            let webSocket = provider.GetRequiredService<WebSocketClient.T>()
            let logger = provider.GetRequiredService<ILogger<Heartbeat.T>>()
            Heartbeat.create logger webSocket
        )
        |> ignore

    let private pipelineRepository (services: IServiceCollection) =
        services.AddScoped<PipelineRepository.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            PipelineRepository.create scopeFactory
        )
        |> ignore

    let private pipelineStepRepository (services: IServiceCollection) =
        services.AddScoped<PipelineStepRepository.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            PipelineStepRepository.create scopeFactory
        )
        |> ignore

    let private candlestickRepository (services: IServiceCollection) =
        services.AddScoped<CandlestickRepository.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            CandlestickRepository.create scopeFactory
        )
        |> ignore

    let private marketRepository (services: IServiceCollection) =
        services.AddScoped<MarketRepository.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let loggerFactory = provider.GetRequiredService<ILoggerFactory>()
            MarketRepository.create scopeFactory loggerFactory
        )
        |> ignore

    let private okxAdapter (services: IServiceCollection) =
        services.AddSingleton<OkxAdapter.T>(fun provider ->
            let loggerFactory = provider.GetRequiredService<ILoggerFactory>()
            let liveDataStore = provider.GetRequiredService<LiveDataStore.T>()
            let webSocket = WebSocketClient.create (loggerFactory.CreateLogger("WebSocket"))
            let logger = loggerFactory.CreateLogger("OkxAdapter")
            OkxAdapter.create webSocket liveDataStore logger
        )
        |> ignore

    let private pipelineOrchestrator (services: IServiceCollection) =
        let registry = TradingSteps.all |> Registry.create
        services.AddSingleton<Registry.T<TradingContext>>(registry) |> ignore

        services.AddHostedService<Orchestrator.Worker>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            let logger = provider.GetRequiredService<ILogger<Orchestrator.Worker>>()
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

    let private webSocketClient (services: IServiceCollection) =
        services.AddSingleton<WebSocketClient.T>(fun provider ->
            let logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("WebSocket")
            WebSocketClient.create logger
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

    let private marketConnectionService (services: IServiceCollection) =
        services.AddHostedService<MarketConnectionService.Worker>() |> ignore

    let private okxWorker (services: IServiceCollection) =
        services.AddHostedService<OkxSynchronizationWorker>() |> ignore

    let private orderExecutor (services: IServiceCollection) =
        services.AddScoped<OrderExecutor.T>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OkxOrderProvider")
            OrderExecutor.create okxHttp okxLogger
        )
        |> ignore

    let private credentialsStore (services: IServiceCollection) =
        services.AddScoped<CredentialsStore.T>(fun provider ->
            let scopeFactory = provider.GetRequiredService<IServiceScopeFactory>()
            CredentialsStore.create scopeFactory
        )
        |> ignore

    let private liveDataStore (services: IServiceCollection) =
        services.AddSingleton<LiveDataStore.T>(fun _ -> LiveDataStore.create ()) |> ignore

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
                client.DefaultRequestHeaders.Add("User-Agent", "Warehouse/1.0")
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
                        (fun _ timespan retryCount _ ->
                            logger.LogWarning(
                                "Retry attempt {RetryCount} after {TimeSpan} seconds",
                                retryCount,
                                timespan
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

        [ pipelineRepository
          pipelineStepRepository
          candlestickRepository
          marketRepository
          liveDataStore
          balanceManager
          credentialsStore
          heartbeat
          httpClientFactory
          httpClient
          marketConnectionService
          okxAdapter
          okxWorker
          orderExecutor
          pipelineOrchestrator
          webSocketClient ]
        |> List.iter (fun addService -> addService services)

    let registerSlim (services: IServiceCollection) (configuration: IConfiguration) =
        database services configuration

        [ pipelineRepository
          pipelineStepRepository
          candlestickRepository
          marketRepository
          liveDataStore
          credentialsStore
          httpClientFactory
          httpClient
          okxAdapter
          orderExecutor ]
        |> List.iter (fun addService -> addService services)
