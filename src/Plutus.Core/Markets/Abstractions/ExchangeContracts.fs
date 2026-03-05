namespace Plutus.Core.Markets.Abstractions

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared

module BalanceProvider =
    open Errors

    type T =
        { MarketType: MarketType
          GetBalances: CancellationToken -> Task<Result<BalanceSnapshot, ServiceError>>
          GetBalance: string -> CancellationToken -> Task<Result<Balance, ServiceError>>
          GetTotalUsdtValue: CancellationToken -> Task<Result<decimal, ServiceError>> }

module OrderExecutor =
    open Errors
    type T = Okx of executeOrder: (Order -> CancellationToken -> Task<Result<string, ServiceError>>)

    let marketType =
        function
        | Okx _ -> MarketType.Okx

    let executeOrder (order: Order) (ct: CancellationToken) (provider: T) =
        match provider with
        | Okx execute -> execute order ct

    let tryFind (market: MarketType) (providers: T list) = providers |> List.tryFind (fun x -> marketType x = market)

module OrderSyncer =
    open Errors

    type OrderUpdate =
        { Status: OrderStatus
          Fee: decimal option
          AveragePrice: decimal option
          FilledQuantity: decimal option
          ExecutedAt: DateTime option
          CancelledAt: DateTime option }

    type T = Okx of getUpdate: (Order -> CancellationToken -> Task<Result<OrderUpdate option, ServiceError>>)

    let marketType =
        function
        | Okx _ -> MarketType.Okx

    let getUpdate (order: Order) (ct: CancellationToken) (syncer: T) =
        match syncer with
        | Okx get -> get order ct

    let tryFind (market: MarketType) (syncers: T list) = syncers |> List.tryFind (fun x -> marketType x = market)
