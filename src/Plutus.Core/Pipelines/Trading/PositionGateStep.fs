namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Ports

module PositionGateStep =
    let positionGate (getPosition: GetPosition) : StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =

            { key = "position-gate-step"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | None, NoAction ->
                            match! getPosition ctx.PipelineId ct with
                            | Error err -> return Stop $"Error retrieving position: {err}"
                            | Ok(Some pos) ->
                                return
                                    Continue(
                                        { ctx with ActiveOrderId = Some pos.OrderId },
                                        "Open position exists, setting action to Hold"
                                    )
                            | Ok None ->
                                return Continue(ctx, $"No active orders or positions, ready to place entry order.")
                        | _ -> return Continue(ctx, "Already have an active order or action in progress")
                    } }

        { Key = "position-gate-step"
          Name = "Position Gate Step"
          Description = "Determines if an entry trade should be placed based on existing positions and orders."
          Category = StepCategory.Validation
          Icon = "fa-sign-in-alt"
          ParameterSchema = { Parameters = [] }
          Create = create }
