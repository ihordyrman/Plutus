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

    static member empty : SimState = { Balance = 0m; Position = None; Trades = []; Equity = []; TradeCount = 0 }
