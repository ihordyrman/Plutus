namespace Plutus.Core.Domain

open System

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

type OrderId = private OrderId of int

module OrderId =
    let create (id: int) : Result<OrderId, string> =
        match id with
        | x when x <= 0 -> Error "Order ID must be a positive integer."
        | _ -> Ok(OrderId id)

    let value (OrderId id) = id

type Order =
    { Id: OrderId
      PipelineId: PipelineId option
      MarketType: MarketType
      ExchangeOrderId: string option
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
