namespace Plutus.Core.Pipelines.Trading

open System
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

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
                | Buy -> OrderSide.Buy, PositionStatus.Open, ctx.CurrentPrice
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
            let buyThreshold = params' |> ValidatedParams.getDecimal "buyThreshold" 0.5m
            let sellThreshold = params' |> ValidatedParams.getDecimal "sellThreshold" -0.5m

            { key = "entry-step"
              execute =
                fun ctx ct ->
                    task {
                        let totalWeight = ctx.SignalWeights |> Map.values |> Seq.sum

                        let action =
                            if totalWeight > buyThreshold then Buy
                            elif totalWeight < sellThreshold then Sell
                            else ctx.Action

                        match ctx.ActiveOrderId, action with
                        | None, Buy ->
                            match! placeOrder services { ctx with Action = Buy; Quantity = Some tradeAmount } ct with
                            | Ok ctx ->
                                return
                                    Continue(
                                        ctx,
                                        $"Placed buy order for order ID {ctx.ActiveOrderId.Value}. (totalWeight={totalWeight:F2})"
                                    )
                            | Error err -> return Fail $"Error placing buy order: {err}"
                        | Some _, Sell ->
                            match! placeOrder services { ctx with Action = Sell } ct with
                            | Ok ctx ->
                                return
                                    Continue(
                                        ctx,
                                        $"Placed sell order for order ID {ctx.ActiveOrderId.Value}. (totalWeight={totalWeight:F2})"
                                    )
                            | Error err -> return Fail $"Error placing sell order: {err}"
                        | _ -> return Continue(ctx, $"No action taken. (totalWeight={totalWeight:F2})")
                    } }

        { Key = "entry-step"
          Name = "Entry Step"
          Description = "Places an entry trade based on aggregated signal weights."
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
                    Group = Some "Order Settings" }
                  { Key = "buyThreshold"
                    Name = "Buy Threshold"
                    Description = "Minimum total signal weight to trigger a buy"
                    Type = Decimal(Some -100m, Some 100m)
                    Required = false
                    DefaultValue = Some(DecimalValue 0.5m)
                    Group = Some "Thresholds" }
                  { Key = "sellThreshold"
                    Name = "Sell Threshold"
                    Description = "Maximum total signal weight to trigger a sell"
                    Type = Decimal(Some -100m, Some 100m)
                    Required = false
                    DefaultValue = Some(DecimalValue -0.5m)
                    Group = Some "Thresholds" } ] }
          Create = create }
