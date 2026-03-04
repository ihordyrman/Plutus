namespace Plutus.Core.Domain

open System

type MarketType =
    | Okx = 0
    | Binance = 1
    | IBKR = 2

[<CLIMutable>]
type Market = { Id: int; Type: MarketType; CreatedAt: DateTime; UpdatedAt: DateTime }
