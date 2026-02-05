namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps

module EwmacSignal =
    let ewmac: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =

            fun ctx ct ->
                task {
                    // Placeholder logic for EWMA signal
                    match ctx.ActiveOrderId, ctx.Action with
                    | Some _, Hold -> return Continue(ctx, "Holding position, no new signal generated.")
                    | _ ->
                        if Random().NextDouble() > 0.5 then
                            return Continue({ ctx with Action = Buy }, "EWMA signal generated: BUY")
                        else
                            return Continue(ctx, "No signal generated")
                }

        { Key = "ewmac-signal"
          Name = "EWMA Signal"
          Description =
            "Generates buy/sell signals based on exponentially weighted moving average convergence divergence (EWMA) calculations."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema = { Parameters = [] }
          Create = create }
