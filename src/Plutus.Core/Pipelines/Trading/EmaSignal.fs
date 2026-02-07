namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps

module ExponentialMovingAverageSignal =
    let ema: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            { key = "ema-signal"
              execute =
                fun ctx ct ->
                    task {
                        // Placeholder logic for moving average signal
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, no new signal generated.")
                        | _ ->
                            if Random().NextDouble() > 0.5 then
                                return Continue({ ctx with Action = Buy }, "Moving average signal generated: BUY")
                            else
                                return Continue(ctx, "No signal generated")
                    } }

        { Key = "ema-signal"
          Name = "Exponential Moving Average Signal"
          Description = "Generates buy/sell signals based on exponential moving average calculations."
          Category = StepCategory.Signal
          Icon = "fa-chart-line"
          ParameterSchema = { Parameters = [] }
          Create = create }
