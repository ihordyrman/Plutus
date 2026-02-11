module Plutus.App.Pages.Index

open Falco
open Falco.Markup
open Falco.Htmx

[<Literal>]
let private Load = "load"

let private header =
    _header [ _class_ "bg-white border-b border-slate-200" ] [
        _div [ _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8" ] [
            _div [ _class_ "flex justify-between items-center h-16" ] [
                _div [ _class_ "flex items-center space-x-8" ] [
                    _h1 [ _class_ "text-sm font-semibold tracking-tight text-slate-900" ] [
                        _a [ _href_ "/" ] [ Text.raw "Plutus System" ]
                    ]
                    _nav [ _class_ "hidden md:flex space-x-2" ] [
                        _a [
                            _href_ "/"
                            _class_ "text-slate-500 hover:bg-slate-100 px-2 py-1 rounded-md text-sm font-medium"
                        ] [ Text.raw "Dashboard" ]
                    ]
                ]
                _div [ _class_ "flex items-center space-x-4" ] [
                    _a [
                        _href_ "/logout"
                        _class_ "text-slate-400 hover:text-slate-600 text-sm flex items-center gap-1.5 transition-colors"
                    ] [
                        _i [ _class_ "fas fa-sign-out-alt text-xs" ] []
                        Text.raw "Logout"
                    ]
                    _div [ _class_ "h-4 w-px bg-slate-200" ] []
                    _span [ _class_ "text-sm text-slate-400" ] [ Text.raw "Status:" ]
                    SystemStatus.View.statusPlaceholder
                ]
            ]
        ]
    ]

let private statsPill icon label countEndpoint subLabel iconColorClass bgColorClass =
    _div [
        _class_ $"flex-1 min-w-[180px] group flex items-center gap-3 px-4 py-2.5 rounded-lg {bgColorClass} border border-slate-200 hover:border-slate-300 hover:shadow-sm transition-all cursor-default"
    ] [
        _div [ _class_ $"w-8 h-8 rounded-lg flex items-center justify-center bg-white shadow-sm" ] [
            _i [ _class_ $"fas {icon} text-sm {iconColorClass}" ] []
        ]
        _div [ _class_ "flex flex-col" ] [
            _div [ _class_ "flex items-center gap-2" ] [
                _span [
                    _class_ "text-lg font-semibold text-slate-900"
                    Hx.get countEndpoint
                    Hx.trigger Load
                    Hx.swapInnerHtml
                ] [ Text.raw "0" ]
                _span [ _class_ "text-xs font-medium text-slate-500" ] [ Text.raw label ]
            ]
            _span [ _class_ "text-[10px] text-slate-400 leading-tight" ] [ Text.raw subLabel ]
        ]
    ]

let private marketsSection =
    _section [ _class_ "mb-10" ] [
        _div [ _class_ "flex justify-between items-center mb-6" ] [
            _div [] [
                _h2 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "Market Accounts" ]
                _p [ _class_ "text-slate-400 text-sm mt-1" ] [ Text.raw "Manage your exchange connections" ]
            ]
            _button [
                _type_ "button"
                _class_
                    "inline-flex items-center px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 font-medium text-sm rounded-md transition-colors"
                Hx.get "/accounts/modal"
                Hx.swapInnerHtml
            ] [ _i [ _class_ "fas fa-plus mr-2 text-slate-400" ] []; Text.raw "Add Account" ]
        ]
        _div [ _id_ "accounts-container"; Hx.get "/markets/grid"; Hx.trigger Load; Hx.swapInnerHtml ] [
            _div [ _class_ "flex justify-center py-8" ] [
                _i [ _class_ "fas fa-spinner fa-spin text-slate-300 text-lg" ] []
            ]
        ]
    ]

let private pipelinesSection =
    _div [ _id_ "pipelines-container"; Hx.get "/pipelines/grid"; Hx.trigger Load; Hx.swapInnerHtml ] [
        _div [ _class_ "flex justify-center py-8" ] [
            _i [ _class_ "fas fa-spinner fa-spin text-slate-300 text-lg" ] []
        ]
    ]

let private ordersSection =
    _div [
        _id_ "orders-container"
        _class_ "mt-10"
        Hx.get "/orders/grid"
        Hx.trigger Load
        Hx.swapInnerHtml
    ] [
        _div [ _class_ "flex justify-center py-8" ] [
            _i [ _class_ "fas fa-spinner fa-spin text-slate-300 text-lg" ] []
        ]
    ]

let private syncSection =
    _section [ _class_ "mt-10" ] [
        _div [ _class_ "flex justify-between items-center mb-4" ] [
            _div [] [
                _h2 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "Candlestick Sync" ]
                _p [ _class_ "text-slate-400 text-sm mt-1" ] [
                    Text.raw "Download historical data for backtesting"
                ]
            ]
            _button [
                _type_ "button"
                _class_
                    "inline-flex items-center px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 font-medium text-sm rounded-md transition-colors"
                Hx.get "/candlestick-sync/modal"
                Hx.targetCss "#modal-container"
                Hx.swapInnerHtml
            ] [ _i [ _class_ "fas fa-download mr-2 text-slate-400" ] []; Text.raw "New Sync" ]
        ]
        _div [
            _id_ "sync-jobs-container"
            Hx.get "/candlestick-sync/jobs"
            Hx.trigger Load
            Hx.swapInnerHtml
        ] [
            _div [ _class_ "flex justify-center py-4" ] [
                _i [ _class_ "fas fa-spinner fa-spin text-slate-300 text-lg" ] []
            ]
        ]
    ]

let get: HttpHandler =
    let html =
        _html [] [
            _head [] [
                _meta [ Attr.create "charset" "utf-8" ]
                _meta [ _name_ "viewport"; Attr.create "content" "width=device-width, initial-scale=1" ]
                _title [] [ Text.raw "Plutus Trading System" ]
                _link [
                    _href_ "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
                    _rel_ "stylesheet"
                ]
                _link [ _href_ "./styles.css"; _rel_ "stylesheet" ]
                _link [
                    _href_ "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css"
                    _rel_ "stylesheet"
                ]
                _script [ _src_ HtmxScript.cdnSrc ] []
                _script [ _src_ "https://cdn.tailwindcss.com" ] []
            ]

            _body [ _class_ "min-h-screen bg-white" ] [
                header

                _div [ _id_ "main-content"; _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8 py-6" ] [
                    _div [
                        _class_ "flex flex-wrap items-center gap-3 mb-8 pb-6 border-b border-slate-200"
                    ] [
                        statsPill "fa-link" "Markets" "/markets/count" "Active connections" "text-indigo-600" "bg-indigo-50/50"
                        statsPill "fa-robot" "Pipelines" "/pipelines/count" "Trading bots" "text-violet-600" "bg-violet-50/50"
                        statsPill "fa-receipt" "Orders" "/orders/count" "Total orders" "text-orange-600" "bg-orange-50/50"
                        statsPill "fa-wallet" "Balance" "/balance/total" "Portfolio value" "text-teal-600" "bg-teal-50/50"
                    ]

                    marketsSection
                    pipelinesSection
                    ordersSection
                    syncSection
                ]

                _div [ _id_ "modal-container" ] []
            ]
        ]

    Response.ofHtml html
