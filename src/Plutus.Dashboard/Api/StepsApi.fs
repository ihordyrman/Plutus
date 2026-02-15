namespace Plutus.Dashboard.Api

open Falco
open Microsoft.AspNetCore.Http
open Plutus.Core.Pipelines.Core

module StepsApi =

    let list: HttpHandler =
        fun (ctx: HttpContext) ->
            let registry = ctx.Plug<Registry.T<TradingContext>>()
            let dtos = Registry.all registry |> List.map ApiDtos.toStepDefinitionDto
            ApiResponse.okList dtos dtos.Length ctx

    let byKey (key: string) : HttpHandler =
        fun (ctx: HttpContext) ->
            let registry = ctx.Plug<Registry.T<TradingContext>>()

            match Registry.tryFind key registry with
            | Some def -> ApiResponse.ok (ApiDtos.toStepDefinitionDto def) ctx
            | None -> ApiResponse.notFound $"Step definition '{key}' not found" ctx
