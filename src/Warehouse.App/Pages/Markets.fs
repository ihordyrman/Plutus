namespace Warehouse.App.Pages.Markets

open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Falco.Markup
open Falco.Htmx
open Warehouse.Core.Domain
open Warehouse.Core.Repositories

type MarketInfo = { Id: int; Type: MarketType; Name: string; Enabled: bool; HasCredentials: bool }

module Data =
    let getCount (scopeFactory: IServiceScopeFactory) : Task<int> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! count = MarketRepository.count db CancellationToken.None

            match count with
            | Ok count -> return count
            | Error _ -> return 0
        }


    let getActiveMarkets (scopeFactory: IServiceScopeFactory) : Task<MarketInfo list> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! markets = MarketRepository.getAll db CancellationToken.None

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
    let private addAccountButton =
        _button [
            _type_ "button"
            _class_
                "inline-flex items-center px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg shadow-sm hover:shadow-md transition-all duration-200"
            Hx.get "/accounts/modal"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] [ _i [ _class_ "fas fa-plus mr-2" ] []; Text.raw "Add Account" ]

    let emptyState =
        _div [ _class_ "bg-white rounded-xl border-2 border-dashed border-gray-300 p-12 text-center" ] [
            _div [ _class_ "inline-flex items-center justify-center w-16 h-16 bg-gray-100 rounded-full mb-4" ] [
                _i [ _class_ "fas fa-exchange-alt text-3xl text-gray-400" ] []
            ]
            _h3 [ _class_ "text-lg font-semibold text-gray-900 mb-2" ] [ Text.raw "No Accounts Yet" ]
            _p [ _class_ "text-gray-500 mb-4" ] [ Text.raw "Connect your first exchange account to start trading" ]
            _button [
                _type_ "button"
                _class_
                    "inline-flex items-center px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg transition-colors"
                Hx.get "/accounts/modal"
                Hx.targetCss "#modal-container"
                Hx.swapInnerHtml
            ] [ _i [ _class_ "fas fa-plus mr-2" ] []; Text.raw "Add Your First Account" ]
        ]

    let private marketPill (market: MarketInfo) =
        let (gradientFrom, gradientTo, iconBg) =
            match market.Type with
            | MarketType.Okx -> ("from-blue-500", "to-blue-600", "bg-blue-400")
            | MarketType.Binance -> ("from-yellow-500", "to-orange-500", "bg-yellow-400")
            | MarketType.IBKR -> ("from-orange-500", "to-red-500", "bg-orange-400")
            | _ -> ("from-gray-500", "to-gray-600", "bg-gray-400")

        let statusDotClass = if market.Enabled then "bg-green-400 animate-pulse" else "bg-gray-400"

        _div [
            _class_
                $"group relative bg-gradient-to-r {gradientFrom} {gradientTo} rounded-2xl px-5 py-3 shadow-lg hover:shadow-xl hover:scale-[1.02] transition-all duration-300 cursor-pointer"
            _id_ $"account-{market.Id}"
        ] [
            _div [ _class_ "flex items-center gap-4" ] [
                _div [
                    _class_
                        $"w-10 h-10 {iconBg} bg-opacity-30 rounded-xl flex items-center justify-center backdrop-blur-sm"
                ] [ _i [ _class_ "fas fa-exchange-alt text-white text-lg" ] [] ]

                // name and status
                _div [ _class_ "flex items-center gap-3" ] [
                    _span [ _class_ "text-white font-bold text-lg" ] [ Text.raw market.Name ]
                    _div [ _class_ "flex items-center gap-1.5" ] [
                        _div [ _class_ $"w-2 h-2 rounded-full {statusDotClass}" ] []
                        _span [ _class_ "text-white text-opacity-80 text-sm font-medium" ] [
                            Text.raw (if market.Enabled then "Active" else "Inactive")
                        ]
                    ]
                ]

                _span [ _class_ "text-white text-opacity-40" ] [ Text.raw "•" ]

                // balance
                _div [
                    Hx.get $"/balance/{int market.Type}"
                    Hx.trigger "load, every 60s"
                    Hx.swapInnerHtml
                    _class_ "text-white font-bold text-lg"
                ] [ _i [ _class_ "fas fa-spinner fa-spin text-white text-opacity-60 text-sm" ] [] ]

                // credential indicator
                if market.HasCredentials then
                    _div [ _class_ "ml-2"; _title_ "API Configured" ] [
                        _i [ _class_ "fas fa-key text-white text-opacity-60 text-sm" ] []
                    ]
                else
                    _div [ _class_ "ml-2"; _title_ "No API Credentials" ] [
                        _i [ _class_ "fas fa-exclamation-circle text-yellow-300 text-sm" ] []
                    ]

                // actions
                _div [
                    _class_
                        "ml-auto flex items-center gap-2 opacity-0 group-hover:opacity-100 transition-opacity duration-200"
                ] [
                    _button [
                        _type_ "button"
                        _class_
                            "w-8 h-8 bg-white bg-opacity-20 hover:bg-opacity-30 rounded-lg flex items-center justify-center transition-all"
                        _title_ "Details"
                        Hx.get $"/accounts/{market.Id}/details/modal"
                        Hx.targetCss "#modal-container"
                        Hx.swapInnerHtml
                    ] [ _i [ _class_ "fas fa-info text-white text-sm" ] [] ]
                    _button [
                        _type_ "button"
                        _class_
                            "w-8 h-8 bg-white bg-opacity-20 hover:bg-opacity-30 rounded-lg flex items-center justify-center transition-all"
                        _title_ "Edit"
                        Hx.get $"/accounts/{market.Id}/edit/modal"
                        Hx.targetCss "#modal-container"
                        Hx.swapInnerHtml
                    ] [ _i [ _class_ "fas fa-cog text-white text-sm" ] [] ]
                ]
            ]
        ]

    let grid (markets: MarketInfo list) =
        match markets with
        | [] -> emptyState
        | items ->
            _div [ _class_ "flex flex-wrap gap-4"; _id_ "accounts-grid" ] [
                for market in items do
                    marketPill market
            ]

    let count (n: int) = Text.raw (string n)

module Handler =
    let count: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! count = Data.getCount scopeFactory
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
                    let! markets = Data.getActiveMarkets scopeFactory
                    return! Response.ofHtml (View.grid markets) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Markets")
                    logger.LogError(ex, "Error getting markets grid")
                    return! Response.ofHtml View.emptyState ctx
            }
