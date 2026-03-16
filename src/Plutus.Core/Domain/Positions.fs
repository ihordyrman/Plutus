namespace Plutus.Core.Domain

open System

type PositionState =
    | Open of openedAt: DateTime
    | Closed of exitPrice: PositiveDecimal * closedAt: DateTime
    | Cancelled of cancelledAt: DateTime

type Position =
    { PipelineId: PipelineId
      Instrument: Instrument
      EntryPrice: PositiveDecimal
      Quantity: PositiveDecimal
      BuyOrderId: OrderId
      SellOrderId: OrderId option
      PositionState: PositionState }

type CreatePositionRequest =
    { PipelineId: PipelineId
      Instrument: Instrument
      EntryPrice: PositiveDecimal
      Quantity: PositiveDecimal
      BuyOrderId: OrderId }
