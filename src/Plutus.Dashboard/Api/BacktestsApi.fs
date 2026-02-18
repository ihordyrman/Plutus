namespace Plutus.Dashboard.Api

open System
open System.Threading
open System.Threading.Tasks
open Falco
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Backtesting
open Plutus.Core.Domain
open Plutus.Core.Repositories

module BacktestsApi =

    let run: HttpHandler =
        fun ctx ->
            task {
                match! ApiResponse.readBody<ApiDtos.RunBacktestRequest> ctx with
                | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                | Ok req ->
                    let errors = ResizeArray<string>()

                    if req.PipelineId <= 0 then
                        errors.Add("pipelineId must be > 0")

                    if req.StartDate >= req.EndDate then
                        errors.Add("startDate must be before endDate")

                    if req.IntervalMinutes <= 0 then
                        errors.Add("intervalMinutes must be > 0")

                    if req.InitialCapital <= 0m then
                        errors.Add("initialCapital must be > 0")

                    if errors.Count > 0 then
                        return! ApiResponse.validationFailed "Validation failed" (errors |> Seq.toList) ctx
                    else
                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                        match! PipelineRepository.getById db req.PipelineId ctx.RequestAborted with
                        | Error _ -> return! ApiResponse.notFound $"Pipeline {req.PipelineId} not found" ctx
                        | Ok _ ->
                            match! PipelineStepRepository.getByPipelineId db req.PipelineId ctx.RequestAborted with
                            | Error _ -> return! ApiResponse.internalError "Failed to load pipeline steps" ctx
                            | Ok steps ->
                                let enabledSteps = steps |> List.filter _.IsEnabled

                                if enabledSteps.IsEmpty then
                                    return! ApiResponse.validationFailed "Pipeline has no enabled steps" [] ctx
                                else
                                    let config: BacktestConfig =
                                        { PipelineId = req.PipelineId
                                          StartDate = req.StartDate
                                          EndDate = req.EndDate
                                          IntervalMinutes = req.IntervalMinutes
                                          InitialCapital = req.InitialCapital }

                                    match! BacktestRepository.createRun db config ctx.RequestAborted with
                                    | Error err -> return! ApiResponse.internalError $"Failed to create run: {err}" ctx
                                    | Ok runId ->
                                        let services = ctx.RequestServices

                                        let _ =
                                            Task.Run(
                                                Func<Task>(fun () ->
                                                    task {
                                                        try
                                                            let! _ =
                                                                BacktestEngine.run
                                                                    services
                                                                    runId
                                                                    config
                                                                    CancellationToken.None

                                                            ()
                                                        with ex ->
                                                            use s = services.CreateScope()

                                                            use db2 =
                                                                s.ServiceProvider.GetRequiredService<
                                                                    System.Data.IDbConnection
                                                                 >()

                                                            let! _ =
                                                                BacktestRepository.updateRunResults
                                                                    db2
                                                                    { Id = runId
                                                                      PipelineId = config.PipelineId
                                                                      Status = BacktestStatus.Failed
                                                                      StartDate = config.StartDate
                                                                      EndDate = config.EndDate
                                                                      IntervalMinutes = config.IntervalMinutes
                                                                      InitialCapital = config.InitialCapital
                                                                      FinalCapital = Nullable()
                                                                      TotalTrades = 0
                                                                      WinRate = Nullable()
                                                                      MaxDrawdown = Nullable()
                                                                      SharpeRatio = Nullable()
                                                                      ErrorMessage = ex.Message
                                                                      CreatedAt = DateTime.UtcNow
                                                                      CompletedAt = Nullable DateTime.UtcNow }
                                                                    CancellationToken.None

                                                            ()
                                                    }
                                                    :> Task
                                                )
                                            )

                                        match! BacktestRepository.getRunById db runId ctx.RequestAborted with
                                        | Ok(Some r) -> return! ApiResponse.created (ApiDtos.toBacktestRunDto r) ctx
                                        | _ ->
                                            return!
                                                ApiResponse.created
                                                    {| Id = runId; Status = int BacktestStatus.Pending |}
                                                    ctx
            }

    let get (id: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! BacktestRepository.getRunById db id ctx.RequestAborted with
                | Ok(Some run) -> return! ApiResponse.ok (ApiDtos.toBacktestRunDto run) ctx
                | Ok None -> return! ApiResponse.notFound $"Backtest run {id} not found" ctx
                | Error err -> return! ApiResponse.internalError $"Failed to fetch backtest: {err}" ctx
            }

    let list: HttpHandler =
        fun ctx ->
            task {
                let query = ctx.Request.Query

                let pipelineId =
                    match query.TryGetValue "pipelineId" with
                    | true, v -> Some(int v.[0])
                    | _ -> None

                let limit =
                    match query.TryGetValue "limit" with
                    | true, v -> int v.[0]
                    | _ -> 50

                let offset =
                    match query.TryGetValue "offset" with
                    | true, v -> int v.[0]
                    | _ -> 0

                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match pipelineId with
                | Some pid ->
                    match! BacktestRepository.getRunsByPipeline db pid ctx.RequestAborted with
                    | Ok runs ->
                        let paged = runs |> List.skip (min offset runs.Length) |> List.truncate limit
                        let dtos = paged |> List.map ApiDtos.toBacktestRunDto
                        return! ApiResponse.okList dtos runs.Length ctx
                    | Error err -> return! ApiResponse.internalError $"Failed to fetch backtests: {err}" ctx
                | None -> return! ApiResponse.validationFailed "pipelineId query parameter is required" [] ctx
            }

    let trades (id: int) : HttpHandler =
        fun ctx ->
            task {
                let query = ctx.Request.Query

                let limit =
                    match query.TryGetValue "limit" with
                    | true, v -> int v.[0]
                    | _ -> 100

                let offset =
                    match query.TryGetValue "offset" with
                    | true, v -> int v.[0]
                    | _ -> 0

                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! BacktestRepository.getRunById db id ctx.RequestAborted with
                | Ok None -> return! ApiResponse.notFound $"Backtest run {id} not found" ctx
                | Error err -> return! ApiResponse.internalError $"Failed to fetch backtest: {err}" ctx
                | Ok(Some _) ->
                    match! BacktestRepository.getTradesByRun db id ctx.RequestAborted with
                    | Error err -> return! ApiResponse.internalError $"Failed to fetch trades: {err}" ctx
                    | Ok allTrades ->
                        let pairs =
                            allTrades
                            |> List.sortBy _.CandleTime
                            |> List.chunkBySize 2
                            |> List.filter (fun chunk -> chunk.Length = 2)
                            |> List.map (fun pair -> ApiDtos.toTradeDto pair.[0] pair.[1])

                        let paged = pairs |> List.skip (min offset pairs.Length) |> List.truncate limit
                        return! ApiResponse.okList paged pairs.Length ctx
            }
