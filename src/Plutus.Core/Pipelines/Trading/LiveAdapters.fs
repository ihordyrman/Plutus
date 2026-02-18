namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Ports
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

module LiveAdapters =
    let getPosition (scopeFactory: IServiceScopeFactory) : GetPosition =
        fun pipelineId ct ->
            task {
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                match! PositionRepository.getOpen db pipelineId ct with
                | Error err -> return Error(serviceMessage err)
                | Ok None -> return Ok None
                | Ok(Some pos) ->
                    return Ok(Some { EntryPrice = pos.EntryPrice; Quantity = pos.Quantity; OrderId = pos.BuyOrderId })
            }

    let tradeExecutor (scopeFactory: IServiceScopeFactory) : TradeExecutor =
        let placeOrder
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

        { ExecuteBuy =
            fun ctx tradeAmount ct ->
                task {
                    use scope = scopeFactory.CreateScope()
                    let services = scope.ServiceProvider

                    match! placeOrder services { ctx with Action = Buy; Quantity = Some tradeAmount } ct with
                    | Ok ctx' -> return Ok(ctx', $"Placed buy order for order ID {ctx'.ActiveOrderId.Value}.")
                    | Error err -> return Error(serviceMessage err)
                }
          ExecuteSell =
            fun ctx ct ->
                task {
                    use scope = scopeFactory.CreateScope()
                    let services = scope.ServiceProvider

                    match! placeOrder services { ctx with Action = Sell } ct with
                    | Ok ctx' -> return Ok(ctx', $"Placed sell order for order ID {ctx'.ActiveOrderId.Value}.")
                    | Error err -> return Error(serviceMessage err)
                } }
