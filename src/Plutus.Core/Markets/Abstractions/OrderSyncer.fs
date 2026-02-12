namespace Plutus.Core.Markets.Abstractions

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared

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
