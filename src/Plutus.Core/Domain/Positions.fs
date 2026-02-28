namespace Plutus.Core.Domain

open System
open Plutus.Core.Shared

type PositionStatus =
    | Open = 0
    | Closed = 1
    | Cancelled = 2

[<CLIMutable>]
type Position =
    { Id: int
      PipelineId: int
      Instrument: Instrument
      EntryPrice: decimal
      Quantity: decimal
      BuyOrderId: int
      SellOrderId: int
      Status: PositionStatus
      ExitPrice: decimal option
      ClosedAt: DateTime option
      CreatedAt: DateTime
      UpdatedAt: DateTime }
