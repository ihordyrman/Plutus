namespace Plutus.Core.Pipelines.Core

open System.Threading
open System.Threading.Tasks

module Ports =
    type PositionInfo = { EntryPrice: decimal; Quantity: decimal; OrderId: int }

    type GetPosition = int -> CancellationToken -> Task<Result<PositionInfo option, string>>

    type TradeExecutor =
        { ExecuteBuy: TradingContext -> decimal -> CancellationToken -> Task<Result<TradingContext * string, string>>
          ExecuteSell: TradingContext -> CancellationToken -> Task<Result<TradingContext * string, string>> }
