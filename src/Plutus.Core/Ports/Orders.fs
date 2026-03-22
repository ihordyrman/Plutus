namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type CreateOrderRequest =
    { PipelineId: PipelineId
      MarketType: MarketType
      Instrument: Instrument
      Side: OrderSide
      Quantity: PositiveDecimal
      Price: PositiveDecimal }

type OrderSearchFilters =
    { SearchTerm: string option
      Side: OrderSide option
      Status: OrderStatus option
      MarketType: MarketType option
      SortBy: string }

type OrderSearchResult =
    { Orders: Order list
      TotalCount: int }

type GetActiveOrders = CancellationToken -> Task<Result<Order list, ServiceError>>
type CreateOrder = CreateOrderRequest -> CancellationToken -> Task<Result<Order, ServiceError>>
type UpdateOrder = Order -> CancellationToken -> Task<Result<unit, ServiceError>>
type SearchOrders = OrderSearchFilters -> int -> int -> CancellationToken -> Task<Result<OrderSearchResult, ServiceError>>
type CountOrders = CancellationToken -> Task<Result<int, ServiceError>>

type OrderPorts =
    { GetActive: GetActiveOrders
      Create: CreateOrder
      Update: UpdateOrder
      Search: SearchOrders
      Count: CountOrders }
