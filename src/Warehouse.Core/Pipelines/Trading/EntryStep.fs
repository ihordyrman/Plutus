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

    let private placeOrder
        (services: IServiceProvider)
        (ctx: TradingContext)
        (ct: CancellationToken)
        : Task<Result<TradingContext, ServiceError>>
        =
        taskResult {
            let side, positionStatus, price =
                match ctx.Action with
                | Buy -> OrderSide.Buy, PositionStatus.Open, ctx.BuyPrice.Value
                | Sell -> OrderSide.Sell, PositionStatus.Closed, ctx.CurrentPrice
                | _ -> failwith "Invalid action for placing order"

            let executors = services.GetRequiredService<OrderExecutor.T list>()

            let! executor =
                OrderExecutor.tryFind ctx.MarketType executors
                |> Result.requireSome (ServiceError.NotFound $"No executor for {ctx.MarketType}")

            let! order =
                Transaction.execute
                    services
                    (fun db txn ->
                        OrderRepository.create
                            db
                            txn
                            { PipelineId = ctx.PipelineId
                              Symbol = ctx.Symbol
                              MarketType = ctx.MarketType
                              Quantity = ctx.Quantity.Value
                              Side = side
                              Price = price }
                            ct
                    )

            do!
                Transaction.execute
                    services
                    (fun db txn ->
                        taskResult {
                            let! exchangeOrderId = OrderExecutor.executeOrder order ct executor

                            let placedOrder =
                                { order with
                                    ExchangeOrderId = exchangeOrderId
                                    Status = OrderStatus.Placed
                                    PlacedAt = Nullable DateTime.UtcNow }

                            let! _ = OrderRepository.update db txn placedOrder ct

                            let! _ =
                                PositionRepository.create
                                    db
                                    txn
                                    { PipelineId = ctx.PipelineId
                                      BuyOrderId = order.Id
                                      EntryPrice = order.Price.Value
                                      Quantity = order.Quantity
                                      Status = positionStatus
                                      Symbol = order.Symbol }
                                    ct

                            return ()
                        }
                    )

            return { ctx with ActiveOrderId = Some order.Id }
        }

    let entry: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let tradeAmount = params' |> ValidatedParams.getDecimal "tradeAmount" 100m

            fun ctx ct ->
                task {
                    match ctx.ActiveOrderId, ctx.Action with
                    | None, Buy ->
                        match! placeOrder services { ctx with Quantity = Some tradeAmount } ct with
                        | Ok ctx -> return Continue(ctx, $"Placed buy order for order ID {ctx.ActiveOrderId.Value}.")
                        | Error err -> return Fail $"Error placing buy order: {err}"
                    | Some _, Sell ->
                        match! placeOrder services ctx ct with
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
