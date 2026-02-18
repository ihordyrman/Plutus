namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Ports

module CheckPosition =
    let checkPosition (getPosition: GetPosition) : StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            { key = "check-position"
              execute =
                fun ctx ct ->
                    task {
                        match! getPosition ctx.PipelineId ct with
                        | Error err -> return Fail $"Error retrieving position: {err}"
                        | Ok None -> return Continue({ ctx with Action = NoAction }, "No open position")
                        | Ok(Some pos) ->
                            let ctx' =
                                { ctx with
                                    BuyPrice = Some pos.EntryPrice
                                    Quantity = Some pos.Quantity
                                    ActiveOrderId = Some pos.OrderId
                                    Action = Hold }

                            return Continue(ctx', $"Position found - Entry: {pos.EntryPrice:F8}")
                    } }

        { Key = "check-position"
          Name = "Check Position"
          Description = "Checks if there is an open position for this pipeline."
          Category = StepCategory.Validation
          Icon = "fa-search-dollar"
          ParameterSchema = { Parameters = [] }
          Create = create }
