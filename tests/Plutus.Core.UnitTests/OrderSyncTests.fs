module Plutus.Core.UnitTests.OrderSyncTests

open System
open Xunit
open Plutus.Core.Domain
open Plutus.Core.Shared
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Workers

let private now = DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)

let private baseOrder: Order =
    { Id = 1
      PipelineId = Some 1
      MarketType = MarketType.Okx
      ExchangeOrderId = "ext-123"
      Instrument = { Base = "BTC"; Quote = "USDT" }
      Side = OrderSide.Buy
      Status = OrderStatus.Pending
      Quantity = 0.5m
      Price = None
      StopPrice = None
      Fee = None
      PlacedAt = None
      ExecutedAt = None
      CancelledAt = None
      TakeProfit = None
      StopLoss = None
      CreatedAt = now
      UpdatedAt = now }

let private baseUpdate: OrderSyncer.OrderUpdate =
    { Status = OrderStatus.Filled
      Fee = Some 1.5m
      AveragePrice = Some 45000m
      FilledQuantity = Some 0.1m
      ExecutedAt = Some now
      CancelledAt = None }

[<Fact>]
let ``applyUpdate - maps all update fields to order`` () =
    let updated = OrderSync.applyUpdate baseOrder baseUpdate
    Assert.Equal(OrderStatus.Filled, updated.Status)
    Assert.Equal(Some 1.5m, updated.Fee)
    Assert.Equal(Some 45000m, updated.Price)
    Assert.Equal(0.1m, updated.Quantity)
    Assert.Equal(Some now, updated.ExecutedAt)
    Assert.Equal(None, updated.CancelledAt)

[<Fact>]
let ``applyUpdate - uses original quantity when FilledQuantity is None`` () =
    let update = { baseUpdate with FilledQuantity = None }
    let updated = OrderSync.applyUpdate baseOrder update
    Assert.Equal(baseOrder.Quantity, updated.Quantity)

[<Fact>]
let ``hasChanges - returns false for identical orders`` () = Assert.False(OrderSync.hasChanges baseOrder baseOrder)

[<Fact>]
let ``hasChanges - detects Status change`` () =
    let updated = { baseOrder with Status = OrderStatus.Filled }
    Assert.True(OrderSync.hasChanges baseOrder updated)

[<Fact>]
let ``hasChanges - detects Fee change`` () =
    let updated = { baseOrder with Fee = Some 2.0m }
    Assert.True(OrderSync.hasChanges baseOrder updated)

[<Fact>]
let ``hasChanges - detects Price change`` () =
    let updated = { baseOrder with Price = Some 50000m }
    Assert.True(OrderSync.hasChanges baseOrder updated)

[<Fact>]
let ``hasChanges - detects Quantity change`` () =
    let updated = { baseOrder with Quantity = 0.2m }
    Assert.True(OrderSync.hasChanges baseOrder updated)

[<Fact>]
let ``hasChanges - detects ExecutedAt change`` () =
    let updated = { baseOrder with ExecutedAt = Some now }
    Assert.True(OrderSync.hasChanges baseOrder updated)

[<Fact>]
let ``hasChanges - detects CancelledAt change`` () =
    let updated = { baseOrder with CancelledAt = Some now }
    Assert.True(OrderSync.hasChanges baseOrder updated)
