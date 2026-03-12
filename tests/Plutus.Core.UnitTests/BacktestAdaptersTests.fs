module Plutus.Core.UnitTests.BacktestAdaptersTests

open System
open System.Threading
open Xunit
open Plutus.Core.Domain
open Plutus.Core.Shared
open Plutus.Core.Pipelines.Core
open Plutus.Core.Backtesting

let private btcUsdt = { Base = "BTC"; Quote = "USDT" }

let private baseCtx: TradingContext =
    { PipelineId = 1
      ExecutionId = "test-exec"
      Instrument = btcUsdt
      MarketType = MarketType.Okx
      CurrentPrice = 50000m
      Action = NoAction
      BuyPrice = None
      Quantity = None
      ActiveOrderId = None
      SignalWeights = Map.empty
      Data = Map.empty }

let private emptyState: SimState =
    { Balance = 1000m; Position = None; Trades = []; Equity = []; TradeCount = 0 }

[<Fact>]
let ``ExecuteBuy - returns Ok with insufficient balance message when balance is too low`` () =
    task {
        let stateRef = ref { emptyState with Balance = 50m }
        let executor = BacktestAdapters.tradeExecutor stateRef

        match! executor.ExecuteBuy baseCtx 100m CancellationToken.None with
        | Ok(_, msg) -> Assert.Contains("Insufficient", msg)
        | Error err -> failwith $"Expected Ok but got Error: {err}"
    }

[<Fact>]
let ``ExecuteBuy - deducts trade amount from balance and opens position`` () =
    task {
        let stateRef = ref emptyState
        let executor = BacktestAdapters.tradeExecutor stateRef
        let! _ = executor.ExecuteBuy baseCtx 500m CancellationToken.None
        // 1000 - 500 = 500
        Assert.Equal(500m, stateRef.Value.Balance)
        Assert.True(stateRef.Value.Position.IsSome)
        Assert.Equal(50000m, stateRef.Value.Position.Value.EntryPrice)
    }

[<Fact>]
let ``ExecuteBuy - records a Buy trade in state`` () =
    task {
        let stateRef = ref emptyState
        let executor = BacktestAdapters.tradeExecutor stateRef
        let! _ = executor.ExecuteBuy baseCtx 500m CancellationToken.None
        Assert.Equal(1, stateRef.Value.Trades.Length)
        Assert.Equal(OrderSide.Buy, stateRef.Value.Trades[0].Side)
        Assert.Equal(50000m, stateRef.Value.Trades[0].Price)
    }

[<Fact>]
let ``ExecuteBuy - sets Quantity and ActiveOrderId on the returned context`` () =
    task {
        let stateRef = ref emptyState
        let executor = BacktestAdapters.tradeExecutor stateRef

        match! executor.ExecuteBuy baseCtx 500m CancellationToken.None with
        | Ok(ctx', _) ->
            // qty = 500 / 50000 = 0.01
            Assert.Equal(Some 0.01m, ctx'.Quantity)
            Assert.True(ctx'.ActiveOrderId.IsSome)
            Assert.Equal(Buy, ctx'.Action)
        | Error err -> failwith $"Expected Ok but got Error: {err}"
    }

[<Fact>]
let ``ExecuteSell - returns Ok with no-position message when there is no open position`` () =
    task {
        let stateRef = ref { emptyState with Position = None }
        let executor = BacktestAdapters.tradeExecutor stateRef

        match! executor.ExecuteSell baseCtx CancellationToken.None with
        | Ok(_, msg) -> Assert.Contains("No position", msg)
        | Error err -> failwith $"Expected Ok but got Error: {err}"
    }

[<Fact>]
let ``ExecuteSell - closes position and adds proceeds to balance`` () =
    task {
        let pos = { EntryPrice = 40000m; Quantity = 0.01m; EntryTime = DateTime.UtcNow; ExecutionId = "x" }
        let stateRef = ref { emptyState with Balance = 500m; Position = Some pos }
        let executor = BacktestAdapters.tradeExecutor stateRef
        // proceeds = 0.01 * 60000 = 600; new balance = 500 + 600 = 1100
        let! _ = executor.ExecuteSell { baseCtx with CurrentPrice = 60000m } CancellationToken.None
        Assert.Equal(1100m, stateRef.Value.Balance)
        Assert.True(stateRef.Value.Position.IsNone)
    }

[<Fact>]
let ``ExecuteSell - records a Sell trade in state`` () =
    task {
        let pos = { EntryPrice = 40000m; Quantity = 0.01m; EntryTime = DateTime.UtcNow; ExecutionId = "x" }
        let stateRef = ref { emptyState with Position = Some pos }
        let executor = BacktestAdapters.tradeExecutor stateRef
        let! _ = executor.ExecuteSell { baseCtx with CurrentPrice = 60000m } CancellationToken.None
        Assert.Equal(1, stateRef.Value.Trades.Length)
        Assert.Equal(OrderSide.Sell, stateRef.Value.Trades[0].Side)
    }

[<Fact>]
let ``ExecuteSell - clears ActiveOrderId BuyPrice and Quantity on returned context`` () =
    task {
        let pos = { EntryPrice = 40000m; Quantity = 0.01m; EntryTime = DateTime.UtcNow; ExecutionId = "x" }
        let stateRef = ref { emptyState with Position = Some pos }
        let executor = BacktestAdapters.tradeExecutor stateRef

        let ctxWithOrder =
            { baseCtx with ActiveOrderId = Some 1; BuyPrice = Some 40000m; Quantity = Some 0.01m }

        match! executor.ExecuteSell ctxWithOrder CancellationToken.None with
        | Ok(ctx', _) ->
            Assert.Equal(Sell, ctx'.Action)
            Assert.True(ctx'.ActiveOrderId.IsNone)
            Assert.True(ctx'.BuyPrice.IsNone)
            Assert.True(ctx'.Quantity.IsNone)
        | Error err -> failwith $"Expected Ok but got Error: {err}"
    }
