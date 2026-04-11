namespace Plutus.Core.Domain

open System

type MarketType =
    | Okx = 0
    | Binance = 1
    | IBKR = 2

type MarketId = private MarketId of int

module MarketId =
    let create (id: int) : Result<MarketId, string> =
        match id with
        | x when x <= 0 -> Error "Market ID must be a positive integer."
        | _ -> Ok(MarketId id)

    let value (MarketId id) = id

type Market = { Id: MarketId; Type: MarketType; CreatedAt: DateTime; UpdatedAt: DateTime }
