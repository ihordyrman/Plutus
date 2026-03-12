namespace Plutus.Dashboard.Api

open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Ports

module ApiAuth =

    let requireApiKey (inner: HttpHandler) : HttpHandler =
        fun (ctx: HttpContext) ->
            task {
                let authHeader = ctx.Request.Headers.Authorization.ToString()

                if not (authHeader.StartsWith("Bearer ")) then
                    return! ApiResponse.unauthorized "Missing or invalid Authorization header" ctx
                else
                    let token = authHeader.Substring(7).Trim()

                    if System.String.IsNullOrEmpty(token) then
                        return! ApiResponse.unauthorized "Empty bearer token" ctx
                    else
                        let hash = Authentication.computeSha256 token
                        let scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        let keyPorts = scope.ServiceProvider.GetRequiredService<KeyPorts>()

                        match KeyHash.create hash with
                        | Error _ -> return! ApiResponse.unauthorized "Authentication failed" ctx
                        | Ok keyHash ->

                            match! keyPorts.GetByHash keyHash ctx.RequestAborted with
                            | Ok(Some key) when key.IsActive ->
                                let! _ = keyPorts.UpdateLastUsed key.Id ctx.RequestAborted
                                return! inner ctx
                            | Ok _ -> return! ApiResponse.unauthorized "Invalid or inactive API key" ctx
                            | Error _ -> return! ApiResponse.unauthorized "Authentication failed" ctx
            }
