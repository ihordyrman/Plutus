namespace Plutus.Core.Markets.Services

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Domain
open Plutus.Core.Shared

module BalanceManager =
    open Errors

    type T = private { Providers: Map<MarketType, BalanceProvider.T> }

    let create (providers: BalanceProvider.T list) : T =
        let map = providers |> List.map (fun x -> x.MarketType, x) |> Map.ofList
        { Providers = map }

    let private withProvider
        (manager: T)
        (marketType: MarketType)
        (f: BalanceProvider.T -> Task<Result<'a, ServiceError>>)
        =
        match Map.tryFind marketType manager.Providers with
        | Some provider -> f provider
        | None -> Task.FromResult(Error(NoProvider marketType))

    let getBalance (manager: T) (marketType: MarketType) (currency: string) (ct: CancellationToken) =
        withProvider manager marketType (fun x -> x.GetBalance currency ct)

    let getAllBalances (manager: T) (marketType: MarketType) (ct: CancellationToken) =
        withProvider manager marketType (fun x -> x.GetBalances ct)

    let getTotalUsdtValue (manager: T) (marketType: MarketType) (ct: CancellationToken) =
        withProvider manager marketType (fun x -> x.GetTotalUsdtValue ct)

    let getAccountBalance (manager: T) (marketType: MarketType) (ct: CancellationToken) =
        task {
            let! result = getAllBalances manager marketType ct

            return
                result
                |> Result.bind (fun snapshot ->
                    match snapshot.AccountSummary with
                    | Some x -> Ok x
                    | None -> Error(NotFound "Account summary")
                )
        }

    let getNonZeroBalances (manager: T) (marketType: MarketType) (ct: CancellationToken) =
        task {
            let! result = getAllBalances manager marketType ct

            return
                result
                |> Result.map (fun snapshot ->
                    let hasValue (b: Balance) = b.Total > 0m || b.Available > 0m || b.Frozen > 0m
                    let spot = snapshot.Spot |> Map.values |> Seq.filter hasValue |> List.ofSeq
                    let funding = snapshot.Funding |> Map.values |> Seq.filter hasValue |> List.ofSeq
                    spot @ funding
                )
        }
