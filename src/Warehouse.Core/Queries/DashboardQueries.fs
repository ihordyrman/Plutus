namespace Warehouse.Core.Queries

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Markets.Services
open Warehouse.Core.Repositories

module DashboardQueries =
    type T = { TotalBalanceUsdt: unit -> Task<decimal> }

    let private getTotalBalanceUsdt (scopeFactory: IServiceScopeFactory) =
        task {
            use scope = scopeFactory.CreateScope()
            let repo = scope.ServiceProvider.GetRequiredService<MarketRepository.T>()
            let balanceManager = scope.ServiceProvider.GetRequiredService<BalanceManager.T>()
            let! markets = repo.GetAll CancellationToken.None

            match markets with
            | Error err ->
                let log = scope.ServiceProvider.GetService<ILogger>()
                log.LogError("Error getting markets: {Error}", err)
                return 0M
            | Ok markets ->
                let sum =
                    markets
                    |> Seq.map (fun market ->
                        task {
                            let! result =
                                (BalanceManager.getTotalUsdtValue balanceManager market.Type CancellationToken.None)

                            match result with
                            | Ok value -> return value
                            | Error err ->
                                let log = scope.ServiceProvider.GetService<ILogger>()
                                log.LogError("Error getting balance for {MarketType}: {Error}", market.Type, err)
                                return 0M
                        }
                    )
                    |> Array.ofSeq

                let! results = Task.WhenAll sum
                return results |> Array.sum
        }

    let create (scopeFactory: IServiceScopeFactory) : T =
        { TotalBalanceUsdt = fun () -> getTotalBalanceUsdt scopeFactory }
