namespace Plutus.Core.Queries

open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Markets.Services
open Plutus.Core.Repositories

module DashboardQueries =
    type T = { TotalBalanceUsdt: CancellationToken -> Task<decimal> }

    let private getTotalBalanceUsdt (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let balanceManager = scope.ServiceProvider.GetRequiredService<BalanceManager.T>()
            let! markets = MarketRepository.getAll db ct

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
                            let! result = (BalanceManager.getTotalUsdtValue balanceManager market.Type ct)

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

    let create (scopeFactory: IServiceScopeFactory) : T = { TotalBalanceUsdt = getTotalBalanceUsdt scopeFactory }
