namespace Plutus.Core.Markets.Exchanges.Okx

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open Plutus.Core.Domain

module Auth =
    let generateSignature (timestamp: string) (secretKey: string) (method: string) (path: string) (body: string) =
        let sign =
            match body with
            | null
            | "" -> Encoding.UTF8.GetBytes($"{timestamp}{method}{path}")
            | _ -> Encoding.UTF8.GetBytes($"{timestamp}{method}{path}{body}")

        let key = Encoding.UTF8.GetBytes(secretKey)
        using (new HMACSHA256(key)) (fun hmac -> hmac.ComputeHash(sign) |> Convert.ToBase64String)

    let createAuthRequest (config: Market) =
        let timestamp =
            (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000L).ToString(CultureInfo.InvariantCulture)

        let sign = generateSignature timestamp, config.SecretKey, "GET", "/users/self/verify", ""

        {| op = "login"
           args = [| {| apiKey = config.ApiKey; passphrase = config.Passphrase; timestamp = timestamp; sign = sign |} |] |}
