module Warehouse.App.Pages.Balance

open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Markets.Services
open Warehouse.Core.Queries
open Warehouse.Core.Shared

module Data =
    let getTotalUsdt (scopeFactory: IServiceScopeFactory) : Task<decimal> =
        (DashboardQueries.create scopeFactory).TotalBalanceUsdt()

    let getMarketBalance
        (scopeFactory: IServiceScopeFactory)
        (marketType: MarketType)
        (logger: ILogger)
        (ct: CancellationToken)
        : Task<Result<decimal, string>>
        =
        task {
            use scope = scopeFactory.CreateScope()
            let balanceManager = scope.ServiceProvider.GetRequiredService<BalanceManager.T>()
            let! result = BalanceManager.getTotalUsdtValue balanceManager marketType ct

            return
                match result with
                | Ok value -> Ok value
                | Error err ->
                    let msg = Errors.serviceMessage err
                    logger.LogError("Error getting balance for {MarketType}: {Error}", marketType, msg)
                    Error msg
        }

module View =
    let formatUsdt value = Text.raw $"$%.2f{value}"
    let balanceText (value: decimal) = Text.raw (value.ToString "C")
    let balanceError = _span [ _class_ "text-red-500 text-sm" ] [ Text.raw "Failed to load" ]

module Handler =
    let total: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! total = Data.getTotalUsdt scopeFactory
                    return! Response.ofHtml (View.balanceText total) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Balances")
                    logger.LogError(ex, "Error getting total balance")
                    return! Response.ofHtml (View.balanceText 0m) ctx
            }

    let market (marketType: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Balances")
                    let marketTypeEnum = enum<MarketType> marketType
                    let! result = Data.getMarketBalance scopeFactory marketTypeEnum logger ctx.RequestAborted

                    return!
                        match result with
                        | Ok value -> Response.ofHtml (View.balanceText value) ctx
                        | Error _ -> Response.ofHtml View.balanceError ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Balances")
                    logger.LogError(ex, "Error getting market balance")
                    return! Response.ofHtml View.balanceError ctx
            }
