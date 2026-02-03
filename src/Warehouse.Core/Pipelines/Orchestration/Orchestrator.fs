namespace Warehouse.Core.Pipelines.Orchestration

open System
open System.Collections.Concurrent
open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Pipelines.Core
open Warehouse.Core.Pipelines.Trading
open Warehouse.Core.Repositories

module Orchestrator =

    [<Literal>]
    let private SyncIntervalSeconds = 30

    let private shouldRun (pipeline: Pipeline) =
        pipeline.Enabled
        && pipeline.Status <> PipelineStatus.Paused
        && pipeline.Status <> PipelineStatus.Error

    type Worker(scopeFactory: IServiceScopeFactory, logger: ILogger<Worker>, registry: Registry.T<TradingContext>) =
        inherit BackgroundService()

        let executors = ConcurrentDictionary<int, Executor.T>()

        let startExecutor (services: IServiceProvider) (pipeline: Pipeline) (ct: CancellationToken) =
            task {
                if executors.ContainsKey(pipeline.Id) then
                    logger.LogDebug("Pipeline {PipelineId} already running", pipeline.Id)
                    return false
                else
                    let executorLogger =
                        services.GetRequiredService<ILoggerFactory>().CreateLogger($"Executor-{pipeline.Id}")

                    let executor = Executor.create services registry executorLogger pipeline.Id

                    if executors.TryAdd(pipeline.Id, executor) then
                        do! executor.Start ct
                        logger.LogInformation("Started pipeline {PipelineId} ({Name})", pipeline.Id, pipeline.Name)
                        return true
                    else
                        logger.LogWarning("Pipeline {PipelineId} was added by another thread", pipeline.Id)
                        return false
            }

        let stopExecutor (pipelineId: int) =
            task {
                match executors.TryRemove(pipelineId) with
                | true, executor ->
                    do! executor.Stop()
                    logger.LogInformation("Stopped pipeline {PipelineId}", pipelineId)
                    return true
                | false, _ ->
                    logger.LogDebug("Pipeline {PipelineId} not running", pipelineId)
                    return false
            }

        let synchronize (services: IServiceProvider) (ct: CancellationToken) =
            task {
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! result = PipelineRepository.getAll db ct

                match result with
                | Error err -> logger.LogError("Failed to load pipelines: {Error}", err)
                | Ok pipelines ->
                    logger.LogDebug("Synchronizing {Count} pipelines", pipelines.Length)

                    for pipeline in pipelines do
                        let isRunning = executors.ContainsKey(pipeline.Id)
                        let shouldBeRunning = shouldRun pipeline

                        match shouldBeRunning, isRunning with
                        | true, false ->
                            let! _ = startExecutor services pipeline ct
                            ()
                        | false, true ->
                            let! _ = stopExecutor pipeline.Id
                            ()
                        | _ -> ()

                    let pipelineIds = pipelines |> List.map _.Id |> Set.ofList

                    for executorId in executors.Keys |> Seq.toList do
                        if not (pipelineIds.Contains executorId) then
                            let! _ = stopExecutor executorId
                            ()
            }

        override _.ExecuteAsync(stoppingToken: CancellationToken) =
            task {
                logger.LogInformation("PipelineOrchestrator started")

                use timer = new PeriodicTimer(TimeSpan.FromSeconds(float SyncIntervalSeconds))
                use scope = scopeFactory.CreateScope()

                do! synchronize scope.ServiceProvider stoppingToken

                while not stoppingToken.IsCancellationRequested do
                    let! tick = timer.WaitForNextTickAsync(stoppingToken)

                    if tick then
                        try
                            do! synchronize scope.ServiceProvider stoppingToken
                        with ex ->
                            logger.LogError(ex, "Error during pipeline synchronization")

                logger.LogInformation("PipelineOrchestrator stopping")
            }

        override this.StopAsync(_stoppingToken: CancellationToken) =
            task {
                logger.LogInformation("Shutting down {Count} running pipelines", executors.Count)

                for pipelineId in executors.Keys |> Seq.toList do
                    let! _ = stopExecutor pipelineId
                    ()
            }

        member _.StartPipelineAsync(pipelineId: int, ct: CancellationToken) =
            task {
                if executors.ContainsKey(pipelineId) then
                    logger.LogWarning("Pipeline {PipelineId} already running", pipelineId)
                    return false
                else
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! result = PipelineRepository.getById db pipelineId ct

                    match result with
                    | Error _ ->
                        logger.LogWarning("Pipeline {PipelineId} not found", pipelineId)
                        return false
                    | Ok pipeline -> return! startExecutor scope.ServiceProvider pipeline ct
            }

        member _.StopPipelineAsync(pipelineId: int) = stopExecutor pipelineId

        member _.GetRunningPipelines() = executors.Keys |> Seq.toList

        member _.IsPipelineRunning(pipelineId: int) = executors.ContainsKey(pipelineId)
