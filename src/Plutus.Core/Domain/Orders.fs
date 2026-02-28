namespace Plutus.Core.Domain

open System
open Plutus.Core.Shared

type OrderSide =
    | Buy = 0
    | Sell = 1

type OrderStatus =
    | Pending = 0
    | Placed = 1
    | PartiallyFilled = 2
    | Filled = 3
    | Cancelled = 4
    | Failed = 5

[<CLIMutable>]
type Order =
    { Id: int
      PipelineId: int option
      MarketType: MarketType
      ExchangeOrderId: string
      Instrument: Instrument
      Side: OrderSide
      Status: OrderStatus
      Quantity: decimal
      Price: decimal option
      StopPrice: decimal option
      Fee: decimal option
      PlacedAt: DateTime option
      ExecutedAt: DateTime option
      CancelledAt: DateTime option
      TakeProfit: decimal option
      StopLoss: decimal option
      CreatedAt: DateTime
      UpdatedAt: DateTime }
