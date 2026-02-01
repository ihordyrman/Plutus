namespace Warehouse.Core.Pipelines.Trading

open System
open System.Data
open Warehouse.Core.Pipelines.Core.Parameters
open Warehouse.Core.Pipelines.Core.Steps
open Microsoft.Extensions.DependencyInjection
open Warehouse.Core.Domain
open Warehouse.Core.Pipelines.Trading
open Warehouse.Core.Repositories

module CheckPosition =
    let checkPosition: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            fun ctx ct ->
                task {
                    use scope = services.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! PositionRepository.getOpen db ctx.PipelineId ct with
                    | Error err -> return Fail $"Error retrieving position: {err}"
                    | Ok None -> return Continue({ ctx with Action = NoAction }, "No open position")
                    | Ok(Some pos) ->
                        let ctx' =
                            { ctx with
                                BuyPrice = Some pos.EntryPrice
                                Quantity = Some pos.Quantity
                                ActiveOrderId = Some pos.BuyOrderId
                                Action = Hold }

                        return Continue(ctx', $"Position found - Entry: {pos.EntryPrice:F8}")
                }

        { Key = "check-position"
          Name = "Check Position"
          Description = "Checks if there is an open position for this pipeline."
          Category = StepCategory.Validation
          Icon = "fa-search-dollar"
          ParameterSchema = { Parameters = [] }
          Create = create }
