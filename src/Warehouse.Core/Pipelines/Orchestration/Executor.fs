namespace Warehouse.Core.Pipelines.Orchestration

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Markets.Abstractions
open Warehouse.Core.Pipelines.Core
open Warehouse.Core.Pipelines.Trading
open Warehouse.Core.Markets.Stores
open Warehouse.Core.Repositories
open Warehouse.Core.Shared

module Executor =

    type private ExecutorState =
        { mutable Cts: CancellationTokenSource option; mutable Task: Task option; mutable IsRunning: bool }

    type T =
        { PipelineId: int
          Start: CancellationToken -> Task<unit>
          Stop: unit -> Task<unit>
          IsRunning: unit -> bool }

    let private loadPipeline (repo: PipelineRepository.T) (pipelineId: int) (ct: CancellationToken) =
        task {
            let! pipeline = repo.GetById pipelineId ct

            match pipeline with
            | Error _ -> return None
            | Ok pipeline -> return Some pipeline
        }

    let private createContext (pipeline: Pipeline) (currentPrice: decimal) (marketData: MarketData option) =
        { TradingContext.empty pipeline.Id pipeline.Symbol pipeline.MarketType with
            CurrentPrice = currentPrice
            CurrentMarketData = marketData }

    let private executeLoop
        (services: IServiceProvider)
        (pipelineId: int)
        (registry: Registry.T<TradingContext>)
        (logger: ILogger)
        (ct: CancellationToken)
        =
        task {
            try
                while not ct.IsCancellationRequested do
                    use scope = services.CreateScope()
                    let pipelineRepository = scope.ServiceProvider.GetRequiredService<PipelineRepository.T>()
                    let stepsRepository = scope.ServiceProvider.GetRequiredService<PipelineStepRepository.T>()

                    match! loadPipeline pipelineRepository pipelineId ct with
                    | None ->
                        logger.LogWarning("Pipeline {PipelineId} not found, stopping executor", pipelineId)
                        return ()

                    | Some pipeline ->
                        let! steps = stepsRepository.GetByPipelineId pipelineId ct

                        match steps with
                        | Error error ->
                            logger.LogError(
                                "Failed to load steps for pipeline {PipelineId}: {Error}",
                                pipelineId,
                                Errors.serviceMessage error
                            )

                            do! Task.Delay(pipeline.ExecutionInterval, ct)
                        | Ok steps ->
                            let stepConfigs =
                                steps
                                |> List.map (fun step ->
                                    { Builder.StepTypeKey = step.StepTypeKey
                                      Builder.Order = step.Order
                                      Builder.IsEnabled = step.IsEnabled
                                      Builder.Parameters =
                                        step.Parameters |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Map.ofSeq }
                                )

                            match Builder.buildSteps registry scope.ServiceProvider stepConfigs with
                            | Error errors ->
                                for err in errors do
                                    logger.LogWarning(
                                        "Pipeline {PipelineId} step {StepKey} validation failed: {Errors}",
                                        pipelineId,
                                        err.StepKey,
                                        err.Errors
                                    )

                                do! Task.Delay(pipeline.ExecutionInterval, ct)

                            | Ok steps when steps.IsEmpty ->
                                logger.LogWarning("Pipeline {PipelineId} has no enabled steps, skipping", pipelineId)
                                do! Task.Delay(pipeline.ExecutionInterval, ct)

                            | Ok steps ->
                                let repository = scope.ServiceProvider.GetRequiredService<CandlestickRepository.T>()
                                let! latestCandle = repository.GetLatest pipeline.Symbol pipeline.MarketType "1m" ct

                                match latestCandle with
                                | Error error ->
                                    logger.LogError(
                                        "Failed to retrieve latest candlestick for {Symbol}: {Error}",
                                        pipeline.Symbol,
                                        Errors.serviceMessage error
                                    )

                                    do! Task.Delay(pipeline.ExecutionInterval, ct)
                                | Ok latestCandle ->
                                    match latestCandle with
                                    | None ->
                                        logger.LogWarning(
                                            "No candlestick data for {Symbol}, skipping execution",
                                            pipeline.Symbol
                                        )

                                        do! Task.Delay(pipeline.ExecutionInterval, ct)

                                    | Some candle ->
                                        let liveDataStore = scope.ServiceProvider.GetRequiredService<LiveDataStore.T>()
                                        let marketData = liveDataStore.Get pipeline.Symbol pipeline.MarketType
                                        let context = createContext pipeline candle.Close marketData

                                        logger.LogDebug(
                                            "Executing pipeline {PipelineId} with {StepCount} steps",
                                            pipelineId,
                                            steps.Length
                                        )

                                        let! result = Runner.run steps context ct

                                        match result with
                                        | Steps.Continue(_, msg) ->
                                            logger.LogDebug(
                                                "Pipeline {PipelineId} completed: {Message}",
                                                pipelineId,
                                                msg
                                            )
                                        | Steps.Stop msg ->
                                            logger.LogDebug("Pipeline {PipelineId} stopped: {Message}", pipelineId, msg)
                                        | Steps.Fail err ->
                                            logger.LogError("Pipeline {PipelineId} failed: {Error}", pipelineId, err)

                                        do! Task.Delay(pipeline.ExecutionInterval, ct)

            with
            | :? OperationCanceledException -> ()
            | ex -> logger.LogError(ex, "Unexpected error in pipeline {PipelineId} executor", pipelineId)
        }
        :> Task

    let create
        (services: IServiceProvider)
        (registry: Registry.T<TradingContext>)
        (logger: ILogger)
        (pipelineId: int)
        : T
        =

        let state = { Cts = None; Task = None; IsRunning = false }

        let start (ct: CancellationToken) =
            task {
                if state.IsRunning then
                    return ()
                else
                    let cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    state.Cts <- Some cts
                    state.IsRunning <- true
                    state.Task <- Some(Task.Run(fun () -> executeLoop services pipelineId registry logger cts.Token))
                    logger.LogInformation("Started executor for pipeline {PipelineId}", pipelineId)
            }

        let stop () =
            task {
                match state.Cts, state.Task with
                | Some cts, Some t ->
                    cts.Cancel()

                    try
                        do! t
                    with :? OperationCanceledException ->
                        ()

                    cts.Dispose()
                    state.Cts <- None
                    state.Task <- None
                    state.IsRunning <- false
                    logger.LogInformation("Stopped executor for pipeline {PipelineId}", pipelineId)
                | _ -> ()
            }

        { PipelineId = pipelineId; Start = start; Stop = stop; IsRunning = fun () -> state.IsRunning }
