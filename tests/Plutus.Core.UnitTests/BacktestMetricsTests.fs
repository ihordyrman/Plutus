module Plutus.Core.UnitTests.BacktestMetricsTests

open System
open Xunit
open Plutus.Core.Domain
open Plutus.Core.Backtesting

let private t0 = DateTime(2024, 1, 1)

let private mkEquityPoint (t: DateTime) (equity: decimal) : BacktestEquityPoint =
    { Id = 0; BacktestRunId = 0; CandleTime = t; Equity = equity; Drawdown = 0m }

let private mkTrade (side: OrderSide) (price: decimal) (qty: decimal) (t: DateTime) : BacktestTrade =
    { Id = 0
      BacktestRunId = 0
      Side = side
      Price = price
      Quantity = qty
      Fee = 0m
      CandleTime = t
      Capital = 0m }

[<Fact>]
let ``calculate - no trades returns zero trade metrics`` () =
    let equity = [ mkEquityPoint t0 1000m ]
    let result = BacktestMetrics.calculate 1000m [] equity
    Assert.Equal(0, result.TotalTrades)
    Assert.Equal(0m, result.WinRate)
    Assert.Equal(0m, result.TotalReturn)

[<Fact>]
let ``calculate - one winning pair computes correct total return and trade count`` () =
    let t1 = t0.AddDays 1.0
    let trades = [ mkTrade OrderSide.Buy 100m 1m t0; mkTrade OrderSide.Sell 110m 1m t1 ]
    let equity = [ mkEquityPoint t0 1000m; mkEquityPoint t1 1010m ]
    let result = BacktestMetrics.calculate 1000m trades equity
    Assert.Equal(1, result.TotalTrades)
    Assert.Equal(1, result.WinningTrades)
    Assert.Equal(1010m, result.FinalCapital)
    Assert.Equal(1.0m, result.TotalReturn)

[<Fact>]
let ``calculate - mixed pairs give correct win rate`` () =
    let trades =
        [ mkTrade OrderSide.Buy 100m 1m t0
          mkTrade OrderSide.Sell 110m 1m (t0.AddHours 1.0) // win
          mkTrade OrderSide.Buy 100m 1m (t0.AddHours 2.0)
          mkTrade OrderSide.Sell 90m 1m (t0.AddHours 3.0) ] // loss

    let equity = [ mkEquityPoint t0 1000m ]
    let result = BacktestMetrics.calculate 1000m trades equity
    Assert.Equal(2, result.TotalTrades)
    Assert.Equal(50m, result.WinRate)

[<Fact>]
let ``calculate - equity drop yields correct max drawdown`` () =
    // Peak = 1000, min = 750 → drawdown = 25%
    let equityValues = [ 1000m; 800m; 900m; 750m; 950m ]
    let equity = equityValues |> List.mapi (fun i e -> mkEquityPoint (t0.AddDays(float i)) e)
    let result = BacktestMetrics.calculate 1000m [] equity
    Assert.Equal(25m, result.MaxDrawdownPct)

[<Fact>]
let ``calculate - flat equity gives zero Sharpe ratio`` () =
    let equity = [ 1; 2; 3 ] |> List.map (fun i -> mkEquityPoint (t0.AddDays(float i)) 1000m)
    let result = BacktestMetrics.calculate 1000m [] equity
    Assert.Equal(0m, result.SharpeRatio)

[<Fact>]
let ``calculate - all losing trades gives zero profit factor`` () =
    let t1 = t0.AddHours 1.0
    let trades = [ mkTrade OrderSide.Buy 100m 1m t0; mkTrade OrderSide.Sell 90m 1m t1 ]
    let equity = [ mkEquityPoint t0 1000m; mkEquityPoint t1 990m ]
    let result = BacktestMetrics.calculate 1000m trades equity
    Assert.Equal(0m, result.ProfitFactor)

[<Fact>]
let ``calculate - average holding period computed from trade timestamps`` () =
    // pair 1: hold 1h, pair 2: hold 3h → avg = 2h
    let trades =
        [ mkTrade OrderSide.Buy 100m 1m t0
          mkTrade OrderSide.Sell 110m 1m (t0.AddHours 1.0)
          mkTrade OrderSide.Buy 100m 1m (t0.AddHours 5.0)
          mkTrade OrderSide.Sell 105m 1m (t0.AddHours 8.0) ]

    let equity = [ mkEquityPoint t0 1000m ]
    let result = BacktestMetrics.calculate 1000m trades equity
    Assert.Equal(TimeSpan.FromHours 2.0, result.AverageHoldingPeriod)
