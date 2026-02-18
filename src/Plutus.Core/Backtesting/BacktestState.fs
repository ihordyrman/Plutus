namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain

module BacktestState =

    type BacktestPosition = { EntryPrice: decimal; Quantity: decimal; EntryTime: DateTime; ExecutionId: string }

    type T =
        { mutable Balance: decimal
          mutable CurrentPosition: BacktestPosition option
          mutable Trades: BacktestTrade list
          mutable TradeCounter: int
          Config: BacktestConfig }

    let create (config: BacktestConfig) : T =
        { Balance = config.InitialCapital
          CurrentPosition = None
          Trades = []
          TradeCounter = 0
          Config = config }
