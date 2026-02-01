namespace Warehouse.Core.Pipelines.Trading

open System
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Warehouse.Core.Domain
open Warehouse.Core.Infrastructure
open Warehouse.Core.Markets.Abstractions
open Warehouse.Core.Pipelines.Core.Parameters
open Warehouse.Core.Pipelines.Core.Steps
open Warehouse.Core.Pipelines.Trading
open Warehouse.Core.Repositories
open Warehouse.Core.Shared.Errors

module EntryStep =
    let private validateContext (ctx: TradingContext) =
        if String.IsNullOrWhiteSpace ctx.Symbol then Error "Symbol required"
        elif ctx.Quantity.IsNone then Error "Quantity required"
        else Ok ctx

    let private buy
        (services: IServiceProvider)
        (ctx: TradingContext)
        (ct: CancellationToken)
        : Task<Result<TradingContext, ServiceError>>
        =
        Transaction.execute
            services
            (fun db txn ->
                taskResult {
                    let! order =
                        OrderRepository.create
                            db
                            txn
                            { PipelineId = ctx.PipelineId
                              Symbol = ctx.Symbol
                              MarketType = ctx.MarketType
                              Quantity = ctx.Quantity.Value
                              Side = OrderSide.Buy
                              Price = ctx.BuyPrice.Value }
                            ct

                    use scope = services.CreateScope()
                    let executor = scope.ServiceProvider.GetService<OrderExecutor.T>()
                    executor


                    let! _ =
                        PositionRepository.create
                            db
                            txn
                            { PipelineId = ctx.PipelineId
                              BuyOrderId = order.Id
                              EntryPrice = order.Price.Value
                              Quantity = order.Quantity
                              Status = PositionStatus.Open
                              Symbol = order.Symbol }
                            ct

                    return { ctx with ActiveOrderId = Some order.Id }
                }
            )

    let private sell
        (services: IServiceProvider)
        (ctx: TradingContext)
        (ct: CancellationToken)
        : Task<Result<TradingContext, ServiceError>>
        =
        failwith "Not implemented"

    let entryStep: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let tradeAmount = params' |> ValidatedParams.getDecimal "tradeAmount" 100m

            fun ctx ct ->
                task {
                    match ctx.ActiveOrderId, ctx.Action with
                    | None, Buy ->
                        match! buy services ctx ct with
                        | Ok ctx -> return Continue(ctx, $"Placed buy order for order ID {ctx.ActiveOrderId.Value}.")
                        | Error err -> return Fail $"Error placing buy order: {err}"
                    | Some orderId, TradingAction.Sell ->
                        match! sell services ctx ct with
                        | Ok ctx -> return Continue(ctx, $"Placed sell order for order ID {ctx.ActiveOrderId.Value}.")
                        | Error err -> return Fail $"Error placing sell order: {err}"
                    | _ -> return Continue(ctx, "No action taken.")
                }

        { Key = "entry-step"
          Name = "Entry Step"
          Description = "Places an entry trade based on the defined strategy."
          Category = StepCategory.Execution
          Icon = "fa-sign-in-alt"
          ParameterSchema =
            { Parameters =
                [ { Key = "tradeAmount"
                    Name = "Trade Amount (USDT)"
                    Description = "Amount in USDT to trade per order"
                    Type = Decimal(Some 1m, Some 100000m)
                    Required = true
                    DefaultValue = Some(DecimalValue 100m)
                    Group = Some "Order Settings" } ] }
          Create = create }
