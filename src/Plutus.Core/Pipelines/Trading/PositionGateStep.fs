namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Repositories

/// Initial step to determine if an entry trade should be placed
module PositionGateStep =
    let positionGate: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =

            { key = "position-gate-step"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | None, NoAction ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                            match! PositionRepository.getOpen db ctx.PipelineId ct with
                            | Error err -> return Stop $"Error retrieving position: {err}"
                            | Ok(Some position) when position.Status = PositionStatus.Open ->
                                return
                                    Continue(
                                        { ctx with ActiveOrderId = Some position.BuyOrderId },
                                        "Open position exists, setting action to Hold"
                                    )
                            | Ok _ ->
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
