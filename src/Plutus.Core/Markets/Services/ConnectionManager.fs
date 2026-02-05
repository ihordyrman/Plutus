namespace Plutus.Core.Markets.Services

open System
open System.Collections.Generic
open System.Data
open System.Threading
open System.Threading.Tasks
open Dapper
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx

type private MarketConnection = { Adapter: OkxAdapter.T; Symbols: Set<string>; ConnectedAt: DateTime }

[<CLIMutable>]
type private PipelineSubscription = { Symbol: string; MarketType: int }

module MarketConnectionService =

    [<Literal>]
    let private PollingIntervalMs = 5000

    [<Literal>]
    let private BooksChannel = "books"

    let private getRequiredSubscriptions (db: IDbConnection) =
        task {
            let! results =
                db.QueryAsync<PipelineSubscription>(
                    "SELECT symbol, market_type as markettype FROM pipelines WHERE enabled = true"
                )

            return
                results
                |> Seq.groupBy _.MarketType
                |> Seq.map (fun (market, items) -> enum<MarketType> market, items |> Seq.map _.Symbol |> Set.ofSeq)
                |> Map.ofSeq
        }

    let private syncSubscriptions
        (adapter: OkxAdapter.T)
        (currentSymbols: Set<string>)
        (requiredSymbols: Set<string>)
        (logger: ILogger)
        (ct: CancellationToken)
        =
        task {
            let newSymbols = Set.difference requiredSymbols currentSymbols

            for symbol in newSymbols do
                logger.LogInformation("Adding subscription for {Symbol}", symbol)
                let! _ = OkxAdapter.subscribeBooks adapter symbol ct
                ()

            let unusedSymbols = Set.difference currentSymbols requiredSymbols

            for symbol in unusedSymbols do
                logger.LogInformation("Removing subscription for {Symbol}", symbol)
                let! _ = OkxAdapter.unsubscribeBooks adapter symbol ct
                ()

            return Set.union (Set.difference currentSymbols unusedSymbols) newSymbols
        }

    let private createAdapter (marketType: MarketType) (provider: IServiceProvider) =
        match marketType with
        | MarketType.Okx -> provider.GetRequiredService<OkxAdapter.T>()
        | _ -> failwithf $"Unsupported market type: %A{marketType}"

    type Worker(logger: ILogger<Worker>, scopeFactory: IServiceScopeFactory) =
        inherit BackgroundService()

        let connections = Dictionary<MarketType, MarketConnection>()
        let connectionLock = new SemaphoreSlim(1, 1)

        let ensureConnection (marketType: MarketType) (requiredSymbols: Set<string>) (ct: CancellationToken) =
            task {
                do! connectionLock.WaitAsync(ct)

                try
                    match connections.TryGetValue(marketType) with
                    | true, conn when conn.Adapter.GetConnectionState() = ConnectionState.Connected ->
                        let! newSymbols = syncSubscriptions conn.Adapter conn.Symbols requiredSymbols logger ct
                        connections[marketType] <- { conn with Symbols = newSymbols }

                    | true, conn ->
                        logger.LogInformation("Reconnecting to {MarketType}...", marketType)
                        let! result = conn.Adapter.Connect None ct

                        match result with
                        | Ok() ->
                            let! _ = syncSubscriptions conn.Adapter Set.empty requiredSymbols logger ct

                            connections[marketType] <-
                                { conn with Symbols = requiredSymbols; ConnectedAt = DateTime.UtcNow }
                        | Error err -> logger.LogError("Failed to reconnect to {MarketType}: {Error}", marketType, err)

                    | false, _ ->
                        logger.LogInformation("Establishing connection to {MarketType}", marketType)
                        let adapter = createAdapter marketType (scopeFactory.CreateScope().ServiceProvider)
                        let! result = adapter.Connect None ct

                        match result with
                        | Ok() ->
                            for symbol in requiredSymbols do
                                let! _ = OkxAdapter.subscribeBooks adapter symbol ct
                                ()

                            connections[marketType] <-
                                { Adapter = adapter; Symbols = requiredSymbols; ConnectedAt = DateTime.UtcNow }

                            logger.LogInformation(
                                "Connected to {MarketType} with {Count} symbols",
                                marketType,
                                requiredSymbols.Count
                            )
                        | Error err -> logger.LogError("Failed to connect to {MarketType}: {Error}", marketType, err)
                finally
                    connectionLock.Release() |> ignore
            }

        let disconnectUnused (activeMarkets: Set<MarketType>) (ct: CancellationToken) =
            task {
                do! connectionLock.WaitAsync(ct)

                try
                    let toDisconnect =
                        connections.Keys |> Seq.filter (fun m -> not (activeMarkets.Contains m)) |> List.ofSeq

                    for marketType in toDisconnect do
                        logger.LogInformation("Disconnecting unused market {MarketType}", marketType)

                        match connections.TryGetValue(marketType) with
                        | true, conn ->
                            try
                                do! conn.Adapter.Disconnect ct
                                conn.Adapter.Dispose()
                            with ex ->
                                logger.LogWarning(ex, "Error disconnecting from {MarketType}", marketType)

                            connections.Remove(marketType) |> ignore
                        | false, _ -> ()
                finally
                    connectionLock.Release() |> ignore
            }

        override _.ExecuteAsync(stoppingToken) =
            task {
                logger.LogInformation("MarketConnectionService starting")

                while not stoppingToken.IsCancellationRequested do
                    try
                        use scope = scopeFactory.CreateScope()
                        let db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                        let! required = getRequiredSubscriptions db

                        for KeyValue(marketType, symbols) in required do
                            do! ensureConnection marketType symbols stoppingToken

                        let activeMarkets = required.Keys |> Set.ofSeq
                        do! disconnectUnused activeMarkets stoppingToken

                    with
                    | :? OperationCanceledException -> ()
                    | ex -> logger.LogError(ex, "Error in MarketConnectionService loop")

                    do! Task.Delay(PollingIntervalMs, stoppingToken)

                logger.LogInformation("MarketConnectionService stopping")

                for KeyValue(_, conn) in connections do
                    try
                        do! conn.Adapter.Disconnect stoppingToken
                        conn.Adapter.Dispose()
                    with ex ->
                        logger.LogWarning(ex, "Error during cleanup")
            }

        override _.Dispose() =
            connectionLock.Dispose()
            base.Dispose()
