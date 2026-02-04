namespace Warehouse.Core.Pipelines.Trading

open System
open Warehouse.Core.Pipelines.Core.Parameters
open Warehouse.Core.Pipelines.Core.Steps

module VwapSignal =
    let vwap: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =

            fun ctx ct ->
                task {
                    // Placeholder logic for VWAP signal
                    match ctx.ActiveOrderId, ctx.Action with
                    | Some _, Hold -> return Continue(ctx, "Holding position, no new signal generated.")
                    | _ ->
                        if Random().NextDouble() > 0.5 then
                            return Continue({ ctx with Action = Buy }, "VWAP signal generated: BUY")
                        else
                            return Continue(ctx, "No signal generated")
                }

        { Key = "vwap-signal"
          Name = "VWAP Signal"
          Description = "Generates buy/sell signals based on volume-weighted average price (VWAP) calculations."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema = { Parameters = [] }
          Create = create }
