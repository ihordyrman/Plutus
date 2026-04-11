namespace Plutus.Shared

open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Plutus.Shared.Errors

type AuthorizeApiKey = string -> CancellationToken -> Task<Result<unit, ServiceError>>

[<RequireQualifiedAccess>]
module Authentication =
    let hashPassword (password: string) : string =
        BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt 12)

    let verifyPassword (password: string) (hash: string) : bool =
        try
            BCrypt.Net.BCrypt.Verify(password, hash)
        with _ ->
            false

    let defaultSessionDuration = TimeSpan.FromDays 7.0
    let extendedSessionDuration = TimeSpan.FromDays 30.0

    let computeSha256 (input: string) : string =
        Convert.ToHexStringLower(SHA256.HashData(Text.Encoding.UTF8.GetBytes input))

    let generateApiKey () : string =
        "plts_"
        + Convert.ToBase64String(RandomNumberGenerator.GetBytes 32).Replace("+", "-").Replace("/", "_").TrimEnd '='

module Authorization =
    let requireApiKey (inner: HttpHandler) : HttpHandler =
        fun (ctx: HttpContext) ->
            task {
                let authHeader = ctx.Request.Headers.Authorization.ToString()

                if not (authHeader.StartsWith("Bearer ")) then
                    return! ApiResponse.unauthorized "Missing or invalid Authorization header" ctx
                else
                    let token = authHeader.Substring(7).Trim()
                    let authorize = ctx.RequestServices.GetRequiredService<AuthorizeApiKey>()

                    match! authorize token ctx.RequestAborted with
                    | Ok() -> return! inner ctx
                    | Error _ -> return! ApiResponse.unauthorized "Authentication failed" ctx
            }
