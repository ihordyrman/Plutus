namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain

type BacktestPosition = { EntryPrice: decimal; Quantity: decimal; EntryTime: DateTime; ExecutionId: string }

type SimState =
    { Balance: decimal
      Position: BacktestPosition option
      Trades: BacktestTrade list
      Equity: (DateTime * decimal) list
      TradeCount: int }

    static member empty: SimState = { Balance = 0m; Position = None; Trades = []; Equity = []; TradeCount = 0 }

type BacktestMetrics =
    { TotalReturn: decimal
      FinalCapital: decimal
      TotalTrades: int
      WinningTrades: int
      LosingTrades: int
      WinRate: decimal
      AverageWin: decimal
      AverageLoss: decimal
      ProfitFactor: decimal
      LargestWin: decimal
      LargestLoss: decimal
      MaxDrawdownPct: decimal
      SharpeRatio: decimal
      AverageHoldingPeriod: TimeSpan
      EquityCurve: (DateTime * decimal) list }

type BacktestResult =
    { RunId: int
      Metrics: BacktestMetrics
      Trades: BacktestTrade list
      EquityPoints: BacktestEquityPoint list }
