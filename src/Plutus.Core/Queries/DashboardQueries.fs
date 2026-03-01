namespace Plutus.Core.Queries

open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open FsToolkit.ErrorHandling
open Plutus.Core.Markets.Services
open Plutus.Core.Repositories

module DashboardQueries =
    type T = { TotalBalanceUsdt: CancellationToken -> Task<decimal> }

    let logError fmt (log: ILogger) = Result.teeError (fun err -> log.LogError(fmt, box err))

    let private getTotalBalanceUsdt (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let balanceManager = scope.ServiceProvider.GetRequiredService<BalanceManager.T>()
            let log = scope.ServiceProvider.GetService<ILogger>()

            let! markets =
                MarketRepository.getAll db ct
                |> Task.map (logError "Error getting markets: {Error}" log >> Result.defaultValue [])

            let! balances =
                markets
                |> List.map (fun market ->
                    BalanceManager.getTotalUsdtValue balanceManager market.Type ct
                    |> Task.map (
                        logError $"Error getting balance for {market.Type}: {0}" log >> Result.defaultValue 0M
                    )
                )
                |> Task.WhenAll

            return balances |> Array.sum
        }

    let create (scopeFactory: IServiceScopeFactory) : T = { TotalBalanceUsdt = getTotalBalanceUsdt scopeFactory }
