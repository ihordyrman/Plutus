namespace Plutus.Core.Pipelines.Core

open Plutus.Core.Domain

type TradingAction =
    | NoAction
    | Hold
    | Buy
    | Sell

type TradingContext =
    { PipelineId: int
      ExecutionId: string
      Symbol: string
      MarketType: MarketType
      CurrentPrice: decimal
      Action: TradingAction
      BuyPrice: decimal option
      Quantity: decimal option
      ActiveOrderId: int option

      SignalWeights: Map<string, decimal>
      Data: Map<string, obj> }

module TradingContext =
    open System.Text.Json
    open System.Text.Json.Serialization

    // see no reason to save whole guid, just need some random string to identify execution in logs
    let private getRandomExecutionId = fun () -> System.Guid.CreateVersion7().ToString().Substring(24, 12)

    let empty pipelineId symbol marketType =
        { PipelineId = pipelineId
          ExecutionId = getRandomExecutionId ()
          Symbol = symbol
          MarketType = marketType
          CurrentPrice = 0m
          Action = NoAction
          BuyPrice = None
          Quantity = None
          ActiveOrderId = None
          SignalWeights = Map.empty
          Data = Map.empty }

    let withAction action ctx = { ctx with Action = action }
    let withPrice price ctx = { ctx with CurrentPrice = price }
    let withSignalWeight key value ctx = { ctx with SignalWeights = Map.add key value ctx.SignalWeights }
    let withData key value ctx = { ctx with Data = Map.add key (box value) ctx.Data }
    let getData<'a> key ctx = ctx.Data |> Map.tryFind key |> Option.map unbox<'a>

    let serializeForLog (ctx: TradingContext) : string =
        let snapshot =
            {| Action =
                match ctx.Action with
                | NoAction -> "NoAction"
                | Hold -> "Hold"
                | Buy -> "Buy"
                | Sell -> "Sell"
               BuyPrice = ctx.BuyPrice
               Quantity = ctx.Quantity
               ActiveOrderId = ctx.ActiveOrderId
               CurrentPrice = ctx.CurrentPrice
               SignalWeights = ctx.SignalWeights |}

        JsonSerializer.Serialize(snapshot, JsonSerializerOptions(WriteIndented = false))
