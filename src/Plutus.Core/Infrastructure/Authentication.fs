namespace Plutus.Core.Infrastructure

open System
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module Authentication =
    let hashPassword (password: string) : string =
        BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12))

    let verifyPassword (password: string) (hash: string) : bool =
        try
            BCrypt.Net.BCrypt.Verify(password, hash)
        with _ ->
            false

    let generateSessionToken () : string =
        let bytes = RandomNumberGenerator.GetBytes(32)
        Convert.ToBase64String(bytes)

    [<Literal>]
    let CookieScheme = "PlutusAuth"

    [<Literal>]
    let CookieName = ".Plutus.Auth"

    let defaultSessionDuration = TimeSpan.FromDays(7.0)

    let extendedSessionDuration = TimeSpan.FromDays(30.0)

    let computeSha256 (input: string) : string =
        let bytes = SHA256.HashData(Text.Encoding.UTF8.GetBytes(input))
        Convert.ToHexStringLower(bytes)

    let generateApiKey () : string =
        let bytes = RandomNumberGenerator.GetBytes(32)
        "plts_" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')
