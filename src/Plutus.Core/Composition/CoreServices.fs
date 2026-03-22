namespace Plutus.Core

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Npgsql
open Plutus.Core.Adapters
open Plutus.Core.Ports
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
        services.Configure<DatabaseSettings>(configuration.GetSection DatabaseSettings.SectionName)
        |> ignore

        services.Configure<MarketSettings>(configuration.GetSection MarketSettings.SectionName)
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

    let private usePorts (services: IServiceCollection) =
        services.AddScoped<UserPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { FindByUsername = UserAdapters.findByUsername db
              UserExists = UserAdapters.userExists db
              CreateUser = UserAdapters.createUser db }
        )
        |> ignore

        services.AddScoped<KeyPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetByHash = ApiKey.getByHash db
              GetAll = ApiKey.getAll db
              Create = ApiKey.create db
              Deactivate = ApiKey.deactivate db
              UpdateLastUsed = ApiKey.updateLastUsed db }
        )
        |> ignore

        services.AddScoped<InstrumentPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { UpsertBatch = Instruments.upsertBatch db
              GetBaseCurrency = Instruments.getBaseCurrencies db
              GetQuoteCurrency = Instruments.getQuoteCurrencies db }
        )
        |> ignore

        services.AddScoped<MarketPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetById = Markets.getById db
              GetAll = Markets.getAll db
              Count = Markets.count db
              EnsureExists = Markets.ensureExists db }
        )
        |> ignore

        services.AddScoped<PositionPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetOpen = Positions.getOpen db
              Create = Positions.create db }
        )
        |> ignore

        services.AddScoped<ExecutionLogPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetByExecutionId = ExecutionLogs.getByExecutionId db
              GetFilteredExecutions = ExecutionLogs.getFilteredExecutions db
              CountFilteredExecutions = ExecutionLogs.countFilteredExecutions db }
        )
        |> ignore

        services.AddScoped<SyncJobPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { Create = SyncJobs.create db
              UpdateStatus = SyncJobs.updateStatus db
              UpdateProgress = SyncJobs.updateProgress db
              Delete = SyncJobs.delete db
              GetActive = SyncJobs.getActive db
              GetAll = SyncJobs.getAll db }
        )
        |> ignore

        services.AddScoped<CandlestickPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetLatest = Candlesticks.getLatest db
              GetOldest = Candlesticks.getOldest db
              FindGaps = Candlesticks.findGaps db
              Query = Candlesticks.query db
              Save = Candlesticks.save db
              DeleteByInstrument = Candlesticks.deleteByInstrument db
              GetDistinctIntervals = Candlesticks.getDistinctIntervals db
              GetDistinctInstrumentCount = Candlesticks.getDistinctInstrumentCount db
              GetWeeklyCoveragePaged = Candlesticks.getWeeklyCoveragePaged db
              GetWeeklyCoverage = Candlesticks.getWeeklyCoverage db }
        )
        |> ignore

        services.AddScoped<PipelinePorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetById = Pipelines.getById db
              GetAll = Pipelines.getAll db
              Create = Pipelines.create db
              Update = Pipelines.update db
              Delete = Pipelines.delete db
              Count = Pipelines.count db
              CountEnabled = Pipelines.countEnabled db
              GetAllTags = Pipelines.getAllTags db
              Search = Pipelines.search db }
        )
        |> ignore

        services.AddScoped<PipelineStepPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetById = PipelineSteps.getById db
              GetByPipelineId = PipelineSteps.getByPipelineId db
              Create = PipelineSteps.create db
              Update = PipelineSteps.update db
              Delete = PipelineSteps.delete db
              DeleteByPipelineId = PipelineSteps.deleteByPipelineId db
              SetEnabled = PipelineSteps.setEnabled db
              SwapOrders = PipelineSteps.swapOrders db
              GetMaxOrder = PipelineSteps.getMaxOrder db }
        )
        |> ignore

        services.AddScoped<OrderPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { GetActive = Orders.getActive db
              Create = Orders.create db
              Update = Orders.update db
              Search = Orders.search db
              Count = Orders.count db }
        )
        |> ignore

        services.AddScoped<BacktestPorts>(fun x ->
            let db = x.GetRequiredService<IDbConnection>()

            { CreateRun = Backtests.createRun db
              UpdateRunResults = Backtests.updateRunResults db
              GetRunById = Backtests.getRunById db
              GetRunsByPipeline = Backtests.getRunsByPipeline db
              InsertTrades = Backtests.insertTrades db
              GetTradesByRun = Backtests.getTradesByRun db
              InsertEquityPoints = Backtests.insertEquityPoints db
              GetEquityByRun = Backtests.getEquityByRun db
              InsertLogs = Backtests.insertLogs db
              GetExecutionSummaries = Backtests.getExecutionSummaries db
              GetLogsByRun = Backtests.getLogsByRun db
              GetAllRuns = Backtests.getAllRuns db
              CountRuns = Backtests.countRuns db
              DeleteRun = Backtests.deleteRun db
              GetLogsByExecution = Backtests.getLogsByExecution db }
        )
        |> ignore

    let private useHttpClient (services: IServiceCollection) =
        services.AddScoped<Http.T>(fun provider ->
            let logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger "OkxHttp"

            let httpClient =
                provider.GetRequiredService<IHttpClientFactory>().CreateClient "Okx"

            let creds = provider.GetRequiredService<IOptions<MarketSettings>>().Value
            let creds = creds.Credentials |> Array.find (fun x -> x.MarketType = "Okx")

            Http.create httpClient creds logger
        )
        |> ignore

    let private useBalanceManager (services: IServiceCollection) =
        services.AddScoped<BalanceManager.T>(fun provider ->
            let loggerFactory = provider.GetRequiredService<ILoggerFactory>()
            let okxHttp = provider.GetRequiredService<Http.T>()
            let okxLogger = loggerFactory.CreateLogger "OkxBalanceProvider"
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

            let okxLogger =
                provider.GetRequiredService<ILoggerFactory>().CreateLogger "OkxOrderProvider"

            [ OrderExecutor.create okxHttp okxLogger ]
        )
        |> ignore

    let private useOrderSyncer (services: IServiceCollection) =
        services.AddScoped<OrderSyncer.T list>(fun provider ->
            let okxHttp = provider.GetRequiredService<Http.T>()

            let okxLogger =
                provider.GetRequiredService<ILoggerFactory>().CreateLogger "OkxOrderSyncer"

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
                client.BaseAddress <- Uri "https://www.okx.com/"
                client.Timeout <- TimeSpan.FromSeconds 300.0
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
            .AddPolicyHandler(fun _ -> Policy.TimeoutAsync<HttpResponseMessage> 10 :> IAsyncPolicy<HttpResponseMessage>)
        |> ignore

    let private useMarketSeeding (services: IServiceCollection) =
        services.AddHostedService<MarketSeedingWorker>() |> ignore

    let register (services: IServiceCollection) (config: IConfiguration) =
        useConfiguration services config
        useDatabase services

        [ useMarketSeeding
          usePorts
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
