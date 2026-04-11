namespace Plutus.Identity.Api

open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Plutus.Shared

module Endpoint =
    let create (handler: HttpHandler) : HttpHandler =
        fun (ctx: HttpContext) ->
            task {
                let result = handler ctx
                return ApiResponse.ok result
            }
