namespace Warehouse.Core.Pipelines.Trading

open Warehouse.Core.Domain
open Warehouse.Core.Markets.Abstractions

type TradingAction =
    | NoAction
    | Hold
    | Buy
    | Sell

type TradingContext =
    { PipelineId: int
      Symbol: string
      MarketType: MarketType
      CurrentPrice: decimal
      CurrentMarketData: MarketData option

      Action: TradingAction
      BuyPrice: decimal option
      Quantity: decimal option
      ActiveOrderId: int option

      Data: Map<string, obj> }

module TradingContext =
    let empty pipelineId symbol marketType =
        { PipelineId = pipelineId
          Symbol = symbol
          MarketType = marketType
          CurrentPrice = 0m
          CurrentMarketData = None
          Action = NoAction
          BuyPrice = None
          Quantity = None
          ActiveOrderId = None
          Data = Map.empty }

    let withAction action ctx = { ctx with Action = action }
    let withPrice price ctx = { ctx with CurrentPrice = price }
    let withData key value ctx = { ctx with Data = Map.add key (box value) ctx.Data }
    let getData<'a> key ctx = ctx.Data |> Map.tryFind key |> Option.map unbox<'a>
