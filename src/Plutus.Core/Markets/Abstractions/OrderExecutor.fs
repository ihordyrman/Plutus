namespace Plutus.Core.Markets.Abstractions

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared

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
