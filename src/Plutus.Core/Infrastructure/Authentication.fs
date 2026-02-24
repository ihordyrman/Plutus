namespace Plutus.Core.Infrastructure

open System
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module Authentication =
    let hashPassword (password: string) : string =
        BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt 12)

    let verifyPassword (password: string) (hash: string) : bool =
        try
            BCrypt.Net.BCrypt.Verify(password, hash)
        with _ ->
            false

    let defaultSessionDuration = TimeSpan.FromDays(7.0)

    let extendedSessionDuration = TimeSpan.FromDays(30.0)

    let computeSha256 (input: string) : string =
        Convert.ToHexStringLower(SHA256.HashData(Text.Encoding.UTF8.GetBytes input))

    let generateApiKey () : string =
        "plts_"
        + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").TrimEnd('=')
