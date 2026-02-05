namespace Plutus.Core.Domain

open System

type PositionStatus =
    | Open = 0
    | Closed = 1
    | Cancelled = 2

[<CLIMutable>]
type Position =
    { Id: int
      PipelineId: int
      Symbol: string
      EntryPrice: decimal
      Quantity: decimal
      BuyOrderId: int
      SellOrderId: int
      Status: PositionStatus
      ExitPrice: Nullable<decimal>
      ClosedAt: Nullable<DateTime>
      CreatedAt: DateTime
      UpdatedAt: DateTime }
