namespace Plutus.Dashboard.Api

open System
open System.Collections.Generic
open Falco
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Repositories

module PipelineStepsApi =

    let private validateStep (registry: Registry.T<TradingContext>) (req: ApiDtos.AddStepRequest) =
        match Registry.tryFind req.StepTypeKey registry with
        | None -> Error [ $"Unknown step type '{req.StepTypeKey}'" ]
        | Some def ->
            let rawParams =
                if isNull (req.Parameters :> obj) then
                    Map.empty
                else
                    req.Parameters |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Map.ofSeq

            match Parameters.validate def.ParameterSchema rawParams with
            | Ok _ -> Ok()
            | Error errors -> Error(errors |> List.map (fun e -> $"{e.Key}: {e.Message}"))

    let list (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineStepRepository.getByPipelineId db pipelineId ctx.RequestAborted with
                | Ok steps ->
                    let dtos = steps |> List.map ApiDtos.toStepDto
                    return! ApiResponse.okList dtos dtos.Length ctx
                | Error err -> return! ApiResponse.internalError $"Failed to fetch steps: {err}" ctx
            }

    let add (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let registry = ctx.Plug<Registry.T<TradingContext>>()

                match! ApiResponse.readBody<ApiDtos.AddStepRequest> ctx with
                | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                | Ok req ->
                    match validateStep registry req with
                    | Error errors -> return! ApiResponse.validationFailed "Step validation failed" errors ctx
                    | Ok() ->
                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                        match! PipelineRepository.getById db pipelineId ctx.RequestAborted with
                        | Error _ -> return! ApiResponse.notFound $"Pipeline {pipelineId} not found" ctx
                        | Ok _ ->
                            match! PipelineStepRepository.getMaxOrder db pipelineId ctx.RequestAborted with
                            | Error err -> return! ApiResponse.internalError $"Failed to get max order: {err}" ctx
                            | Ok maxOrder ->
                                let stepDef = Registry.tryFind req.StepTypeKey registry |> Option.get

                                let step: PipelineStep =
                                    { Id = 0
                                      PipelineId = pipelineId
                                      StepTypeKey = req.StepTypeKey
                                      Name = stepDef.Name
                                      Order = maxOrder + 1
                                      IsEnabled = req.IsEnabled
                                      Parameters =
                                        if isNull (req.Parameters :> obj) then
                                            Dictionary<string, string>()
                                        else
                                            req.Parameters
                                      CreatedAt = DateTime.UtcNow
                                      UpdatedAt = DateTime.UtcNow }

                                match! PipelineStepRepository.create db step ctx.RequestAborted with
                                | Ok created -> return! ApiResponse.created (ApiDtos.toStepDto created) ctx
                                | Error err -> return! ApiResponse.internalError $"Failed to create step: {err}" ctx
            }

    let update (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let registry = ctx.Plug<Registry.T<TradingContext>>()

                match! ApiResponse.readBody<ApiDtos.AddStepRequest> ctx with
                | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                | Ok req ->
                    match validateStep registry req with
                    | Error errors -> return! ApiResponse.validationFailed "Step validation failed" errors ctx
                    | Ok() ->
                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                        match! PipelineStepRepository.getById db stepId ctx.RequestAborted with
                        | Error _ -> return! ApiResponse.notFound $"Step {stepId} not found" ctx
                        | Ok existing when existing.PipelineId <> pipelineId ->
                            return! ApiResponse.notFound $"Step {stepId} not found in pipeline {pipelineId}" ctx
                        | Ok existing ->
                            let stepDef = Registry.tryFind req.StepTypeKey registry |> Option.get

                            let updated =
                                { existing with
                                    StepTypeKey = req.StepTypeKey
                                    Name = stepDef.Name
                                    IsEnabled = req.IsEnabled
                                    Parameters =
                                        if isNull (req.Parameters :> obj) then
                                            Dictionary<string, string>()
                                        else
                                            req.Parameters }

                            match! PipelineStepRepository.update db updated ctx.RequestAborted with
                            | Ok s -> return! ApiResponse.ok (ApiDtos.toStepDto s) ctx
                            | Error err -> return! ApiResponse.internalError $"Failed to update step: {err}" ctx
            }

    let delete (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                match! PipelineStepRepository.getById db stepId ctx.RequestAborted with
                | Error _ -> return! ApiResponse.notFound $"Step {stepId} not found" ctx
                | Ok existing when existing.PipelineId <> pipelineId ->
                    return! ApiResponse.notFound $"Step {stepId} not found in pipeline {pipelineId}" ctx
                | Ok _ ->
                    match! PipelineStepRepository.delete db stepId ctx.RequestAborted with
                    | Ok() -> return! ApiResponse.ok {| Deleted = true |} ctx
                    | Error err -> return! ApiResponse.internalError $"Failed to delete step: {err}" ctx
            }

    let bulk (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let registry = ctx.Plug<Registry.T<TradingContext>>()

                match! ApiResponse.readBody<ApiDtos.BulkStepsRequest> ctx with
                | Error msg -> return! ApiResponse.validationFailed "Invalid request body" msg ctx
                | Ok req ->
                    let steps = if isNull (req.Steps :> obj) then [] else req.Steps

                    let allErrors =
                        steps
                        |> List.indexed
                        |> List.collect (fun (i, s) ->
                            match validateStep registry s with
                            | Ok() -> []
                            | Error errors -> errors |> List.map (fun e -> $"steps[{i}]: {e}")
                        )

                    if not allErrors.IsEmpty then
                        return! ApiResponse.validationFailed "Bulk validation failed" allErrors ctx
                    else
                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>()

                        match! PipelineRepository.getById db pipelineId ctx.RequestAborted with
                        | Error _ -> return! ApiResponse.notFound $"Pipeline {pipelineId} not found" ctx
                        | Ok _ ->
                            match! PipelineStepRepository.deleteByPipelineId db pipelineId ctx.RequestAborted with
                            | Error err -> return! ApiResponse.internalError $"Failed to clear steps: {err}" ctx
                            | Ok _ ->
                                let mutable createdSteps = []

                                for i, s in steps |> List.indexed do
                                    let stepDef = Registry.tryFind s.StepTypeKey registry |> Option.get

                                    let step: PipelineStep =
                                        { Id = 0
                                          PipelineId = pipelineId
                                          StepTypeKey = s.StepTypeKey
                                          Name = stepDef.Name
                                          Order = i
                                          IsEnabled = s.IsEnabled
                                          Parameters =
                                            if isNull (s.Parameters :> obj) then
                                                Dictionary<string, string>()
                                            else
                                                s.Parameters
                                          CreatedAt = DateTime.UtcNow
                                          UpdatedAt = DateTime.UtcNow }

                                    match! PipelineStepRepository.create db step ctx.RequestAborted with
                                    | Ok created -> createdSteps <- created :: createdSteps
                                    | Error _ -> ()

                                let dtos = createdSteps |> List.rev |> List.map ApiDtos.toStepDto
                                return! ApiResponse.ok dtos ctx
            }
