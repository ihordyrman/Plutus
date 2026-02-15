namespace Plutus.Dashboard.Api

open System.Data
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories

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
                        use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                        match! ApiKeyRepository.getByHash db hash ctx.RequestAborted with
                        | Ok(Some key) when key.IsActive ->
                            let! _ = ApiKeyRepository.updateLastUsed db key.Id ctx.RequestAborted
                            return! inner ctx
                        | Ok _ -> return! ApiResponse.unauthorized "Invalid or inactive API key" ctx
                        | Error _ -> return! ApiResponse.unauthorized "Authentication failed" ctx
            }
