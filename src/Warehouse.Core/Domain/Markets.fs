namespace Warehouse.Core.Domain

open System

type MarketType =
    | Okx = 0
    | Binance = 1
    | IBKR = 2

type ConnectionState =
    | Disconnected = 0
    | Connecting = 1
    | Connected = 2
    | Failed = 3

[<CLIMutable>]
type Market =
    { Id: int
      Type: MarketType
      ApiKey: string
      Passphrase: string option
      SecretKey: string
      IsSandbox: bool
      CreatedAt: DateTime
      UpdatedAt: DateTime }
