namespace Plutus.App.Pages.Markets

open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Falco.Markup
open Falco.Htmx
open Plutus.Core.Domain
open Plutus.Core.Repositories

type MarketInfo = { Id: int; Type: MarketType; Name: string; Enabled: bool; HasCredentials: bool }

module Data =
    let getCount (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<int> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! count = MarketRepository.count db ct

            match count with
            | Ok count -> return count
            | Error _ -> return 0
        }


    let getActiveMarkets (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<MarketInfo list> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! markets = MarketRepository.getAll db ct

            match markets with
            | Error _ -> return []
            | Ok markets ->
                return
                    markets
                    |> List.map (fun x ->
                        { Id = x.Id
                          Type = x.Type
                          Name = x.Type.ToString()
                          Enabled = true
                          HasCredentials = true // tf is this
                        }
                    )
        }

module View =
    let emptyState =
        _div
            [ _class_ "bg-white rounded-md border-2 border-dashed border-slate-200 p-12 text-center" ]
            [ _div
                  [ _class_ "inline-flex items-center justify-center w-12 h-12 bg-slate-50 rounded-md mb-4" ]
                  [ _i [ _class_ "fas fa-exchange-alt text-xl text-slate-400" ] [] ]
              _h3 [ _class_ "text-sm font-semibold text-slate-900 mb-2" ] [ Text.raw "No Accounts Yet" ]
              _p
                  [ _class_ "text-slate-400 text-sm mb-4" ]
                  [ Text.raw "Connect your first exchange account to start trading" ]
              _button
                  [ _type_ "button"
                    _class_
                        "inline-flex items-center px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 font-medium text-sm rounded-md transition-colors"
                    Hx.get "/accounts/modal"
                    Hx.targetCss "#modal-container"
                    Hx.swapInnerHtml ]
                  [ _i [ _class_ "fas fa-plus mr-2 text-slate-400" ] []; Text.raw "Add Your First Account" ] ]

    let private marketPill (market: MarketInfo) =
        let statusDotClass = if market.Enabled then "bg-green-400" else "bg-slate-300"

        _div
            [ _class_
                  "group bg-white border border-slate-200 rounded-md px-4 py-3 hover:bg-slate-50 transition-colors cursor-pointer"
              _id_ $"account-{market.Id}" ]
            [ _div
                  [ _class_ "flex items-center gap-3" ]
                  [ _div
                        [ _class_ "w-8 h-8 bg-slate-100 rounded-md flex items-center justify-center" ]
                        [ _i [ _class_ "fas fa-exchange-alt text-slate-500 text-sm" ] [] ]

                    // name and status
                    _div
                        [ _class_ "flex items-center gap-3" ]
                        [ _span [ _class_ "text-slate-900 font-medium text-sm" ] [ Text.raw market.Name ]
                          _div
                              [ _class_ "flex items-center gap-1.5" ]
                              [ _div [ _class_ $"w-2 h-2 rounded-full {statusDotClass}" ] []
                                _span
                                    [ _class_ "text-slate-400 text-xs" ]
                                    [ Text.raw (if market.Enabled then "Active" else "Inactive") ] ] ]

                    _span [ _class_ "text-slate-200" ] [ Text.raw "|" ]

                    // balance
                    _div
                        [ Hx.get $"/balance/{int market.Type}"
                          Hx.trigger "load, every 60s"
                          Hx.swapInnerHtml
                          _class_ "text-slate-900 font-medium text-sm" ]
                        [ _i [ _class_ "fas fa-spinner fa-spin text-slate-300 text-xs" ] [] ]

                    // credential indicator
                    if market.HasCredentials then
                        _div
                            [ _class_ "ml-2"; _title_ "API Configured" ]
                            [ _i [ _class_ "fas fa-key text-slate-300 text-xs" ] [] ]
                    else
                        _div
                            [ _class_ "ml-2"; _title_ "No API Credentials" ]
                            [ _i [ _class_ "fas fa-exclamation-circle text-yellow-400 text-xs" ] [] ]

                    // actions
                    _div
                        [ _class_
                              "ml-auto flex items-center gap-2 opacity-0 group-hover:opacity-100 transition-opacity duration-200" ]
                        [ _button
                              [ _type_ "button"
                                _class_
                                    "w-7 h-7 bg-slate-100 hover:bg-slate-200 rounded-md flex items-center justify-center transition-colors"
                                _title_ "Details"
                                Hx.get $"/accounts/{market.Id}/details/modal"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml ]
                              [ _i [ _class_ "fas fa-info text-slate-500 text-xs" ] [] ]
                          _button
                              [ _type_ "button"
                                _class_
                                    "w-7 h-7 bg-slate-100 hover:bg-slate-200 rounded-md flex items-center justify-center transition-colors"
                                _title_ "Edit"
                                Hx.get $"/accounts/{market.Id}/edit/modal"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml ]
                              [ _i [ _class_ "fas fa-cog text-slate-500 text-xs" ] [] ] ] ] ]

    let grid (markets: MarketInfo list) =
        match markets with
        | [] -> emptyState
        | items ->
            _div
                [ _class_ "flex flex-wrap gap-3"; _id_ "accounts-grid" ]
                [ for market in items do
                      marketPill market ]

    let count (n: int) = Text.raw (string n)

module Handler =
    let count: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! count = Data.getCount scopeFactory ctx.RequestAborted
                    return! Response.ofHtml (View.count count) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Markets")
                    logger.LogError(ex, "Error getting markets count")
                    return! Response.ofHtml (View.count 0) ctx
            }

    let grid: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! markets = Data.getActiveMarkets scopeFactory ctx.RequestAborted
                    return! Response.ofHtml (View.grid markets) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Markets")
                    logger.LogError(ex, "Error getting markets grid")
                    return! Response.ofHtml View.emptyState ctx
            }
