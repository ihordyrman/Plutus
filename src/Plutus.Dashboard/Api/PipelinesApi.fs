namespace Plutus.Dashboard.Api

open System
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Repositories

module PipelinesApi =

    let private withDb (ctx: HttpContext) (f: System.Data.IDbConnection -> _) =
        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
        use scope = scopeFactory.CreateScope()
        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()
        f db

    let list: HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineRepository.getAll db ctx.RequestAborted with
                | Ok pipelines ->
                    let dtos = pipelines |> List.map ApiDtos.toPipelineDto
                    return! ApiResponse.okList dtos dtos.Length ctx
                | Error err -> return! ApiResponse.internalError $"Failed to fetch pipelines: {err}" ctx
            }

    let get (id: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineRepository.getById db id ctx.RequestAborted with
                | Error _ -> return! ApiResponse.notFound $"Pipeline {id} not found" ctx
                | Ok pipeline ->
                    match! PipelineStepRepository.getByPipelineId db id ctx.RequestAborted with
                    | Ok steps -> return! ApiResponse.ok (ApiDtos.toPipelineDetailDto pipeline steps) ctx
                    | Error _ -> return! ApiResponse.ok (ApiDtos.toPipelineDetailDto pipeline []) ctx
            }

    let private validatePipeline (instrument: string) (marketType: int) (intervalMinutes: int) =
        let errors = ResizeArray<string>()

        if String.IsNullOrWhiteSpace(instrument) then
            errors.Add("instrument is required")

        if marketType < 0 || marketType > 2 then
            errors.Add("marketType must be 0 (Okx), 1 (Binance), or 2 (IBKR)")

        if intervalMinutes <= 0 then
            errors.Add("executionIntervalMinutes must be greater than 0")

        if errors.Count > 0 then Error(errors |> Seq.toList) else Ok()

    let create: HttpHandler =
        fun ctx ->
            task {
                match! ApiResponse.readBody<ApiDtos.CreatePipelineRequest> ctx with
                | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                | Ok req ->
                    match validatePipeline req.Instrument req.MarketType req.ExecutionIntervalMinutes with
                    | Error errors -> return! ApiResponse.validationFailed "Validation failed" errors ctx
                    | Ok() ->
                        let pipeline: Pipeline =
                            { Id = 0
                              Name = req.Instrument
                              Instrument = req.Instrument
                              MarketType = enum<MarketType> req.MarketType
                              Enabled = req.Enabled
                              ExecutionInterval = TimeSpan.FromMinutes(float req.ExecutionIntervalMinutes)
                              LastExecutedAt = Nullable()
                              Status = PipelineStatus.Idle
                              Tags = req.Tags
                              CreatedAt = DateTime.UtcNow
                              UpdatedAt = DateTime.UtcNow }

                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                        match! PipelineRepository.create db pipeline ctx.RequestAborted with
                        | Ok created -> return! ApiResponse.created (ApiDtos.toPipelineDto created) ctx
                        | Error err -> return! ApiResponse.internalError $"Failed to create pipeline: {err}" ctx
            }

    let update (id: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineRepository.getById db id ctx.RequestAborted with
                | Error _ -> return! ApiResponse.notFound $"Pipeline {id} not found" ctx
                | Ok existing ->
                    if existing.Status = PipelineStatus.Running then
                        return! ApiResponse.conflict "PIPELINE_RUNNING" "Cannot update a running pipeline" ctx
                    else
                        match! ApiResponse.readBody<ApiDtos.UpdatePipelineRequest> ctx with
                        | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                        | Ok req ->
                            match validatePipeline req.Instrument req.MarketType req.ExecutionIntervalMinutes with
                            | Error errors -> return! ApiResponse.validationFailed "Validation failed" errors ctx
                            | Ok() ->
                                let updated =
                                    { existing with
                                        Instrument = req.Instrument
                                        MarketType = enum<MarketType> req.MarketType
                                        Enabled = req.Enabled
                                        ExecutionInterval = TimeSpan.FromMinutes(float req.ExecutionIntervalMinutes)
                                        Tags = req.Tags }

                                match! PipelineRepository.update db updated ctx.RequestAborted with
                                | Ok p -> return! ApiResponse.ok (ApiDtos.toPipelineDto p) ctx
                                | Error err -> return! ApiResponse.internalError $"Failed to update pipeline: {err}" ctx
            }

    let delete (id: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineRepository.getById db id ctx.RequestAborted with
                | Error _ -> return! ApiResponse.notFound $"Pipeline {id} not found" ctx
                | Ok existing ->
                    if existing.Status = PipelineStatus.Running then
                        return! ApiResponse.conflict "PIPELINE_RUNNING" "Cannot delete a running pipeline" ctx
                    else
                        match! PipelineRepository.delete db id ctx.RequestAborted with
                        | Ok() -> return! ApiResponse.ok {| Deleted = true |} ctx
                        | Error err -> return! ApiResponse.internalError $"Failed to delete pipeline: {err}" ctx
            }
