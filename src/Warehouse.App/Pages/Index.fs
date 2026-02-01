module Warehouse.App.Pages.Index

open Falco
open Falco.Markup
open Falco.Htmx

[<Literal>]
let private Load = "load"

let private header =
    _header [ _class_ "bg-white shadow-sm border-b border-gray-200" ] [
        _div [ _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8" ] [
            _div [ _class_ "flex justify-between items-center h-16" ] [
                _div [ _class_ "flex items-center space-x-8" ] [
                    _h1 [ _class_ "text-xl font-bold text-gray-900" ] [
                        _a [ _href_ "/" ] [ Text.raw "Warehouse System" ]
                    ]
                    _nav [ _class_ "hidden md:flex space-x-4" ] [
                        _a [ _href_ "/"; _class_ "text-gray-700 hover:text-gray-900 font-medium" ] [
                            Text.raw "Dashboard"
                        ]
                    ]
                ]
                _div [ _class_ "flex items-center space-x-4" ] [
                    _span [ _class_ "text-sm text-gray-500" ] [ Text.raw "Status:" ]
                    SystemStatus.View.statusPlaceholder
                ]
            ]
        ]
    ]

let private statsCard icon label countEndpoint subLabel gradientClasses =
    _div [
        _class_
            $"relative overflow-hidden rounded-xl bg-gradient-to-br {gradientClasses} p-6 shadow-lg hover:shadow-xl transition-shadow duration-300"
    ] [
        _div [ _class_ "absolute top-0 right-0 -mt-4 -mr-4 h-24 w-24 rounded-full bg-white opacity-10" ] []
        _div [ _class_ "relative" ] [
            _div [ _class_ "flex items-center justify-between mb-2" ] [
                _div [ _class_ "p-3 bg-white bg-opacity-20 rounded-lg backdrop-blur-sm" ] [
                    _i [ _class_ $"fas {icon} text-2xl text-white" ] []
                ]
                _span [ _class_ "text-white text-opacity-80 text-sm font-medium" ] [ Text.raw label ]
            ]
            _div [ _class_ "mt-4" ] [
                _p [ _class_ "text-4xl font-bold text-white"; Hx.get countEndpoint; Hx.trigger Load; Hx.swapInnerHtml ] [
                    Text.raw "0"
                ]
                _p [ _class_ "text-white text-opacity-80 text-sm mt-1" ] [ Text.raw subLabel ]
            ]
        ]
    ]

let private marketsSection =
    _section [ _class_ "mb-10" ] [
        _div [ _class_ "flex justify-between items-center mb-6" ] [
            _div [] [
                _h2 [ _class_ "text-2xl font-bold text-gray-900" ] [ Text.raw "Market Accounts" ]
                _p [ _class_ "text-gray-500 text-sm mt-1" ] [ Text.raw "Manage your exchange connections" ]
            ]
            _button [
                _type_ "button"
                _class_
                    "inline-flex items-center px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg shadow-sm hover:shadow-md transition-all duration-200"
                Hx.get "/accounts/modal"
                Hx.targetCss "#modal-container"
                Hx.swapInnerHtml
            ] [ _i [ _class_ "fas fa-plus mr-2" ] []; Text.raw "Add Account" ]
        ]
        _div [ _id_ "accounts-container"; Hx.get "/markets/grid"; Hx.trigger Load; Hx.swapInnerHtml ] [
            _div [ _class_ "flex justify-center py-8" ] [
                _i [ _class_ "fas fa-spinner fa-spin text-gray-400 text-2xl" ] []
            ]
        ]
    ]

let private pipelinesSection =
    _div [ _id_ "pipelines-container"; Hx.get "/pipelines/grid"; Hx.trigger Load; Hx.swapInnerHtml ] [
        _div [ _class_ "flex justify-center py-8" ] [
            _i [ _class_ "fas fa-spinner fa-spin text-gray-400 text-2xl" ] []
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
            _i [ _class_ "fas fa-spinner fa-spin text-gray-400 text-2xl" ] []
        ]
    ]

let get: HttpHandler =
    let html =
        _html [] [
            _head [] [
                _meta [ Attr.create "charset" "utf-8" ]
                _meta [ _name_ "viewport"; Attr.create "content" "width=device-width, initial-scale=1" ]
                _title [] [ Text.raw "Warehouse Trading System" ]
                _link [ _href_ "./styles.css"; _rel_ "stylesheet" ]
                _link [
                    _href_ "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css"
                    _rel_ "stylesheet"
                ]
                _script [ _src_ HtmxScript.cdnSrc ] []
                _script [ _src_ "https://cdn.tailwindcss.com" ] []
            ]

            _body [ _class_ "min-h-screen bg-gray-50" ] [
                header

                _div [ _id_ "main-content"; _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8 py-6" ] [
                    _div [ _class_ "grid grid-cols-1 md:grid-cols-4 gap-6 mb-10" ] [
                        statsCard
                            "fa-shield-alt"
                            "Markets"
                            "/markets/count"
                            "Active Connections"
                            "from-blue-500 to-blue-600"
                        statsCard
                            "fa-robot"
                            "Pipelines"
                            "/pipelines/count"
                            "Total Pipelines"
                            "from-green-500 to-emerald-600"
                        statsCard "fa-receipt" "Orders" "/orders/count" "Total Orders" "from-orange-500 to-amber-600"
                        statsCard "fa-wallet" "Balance" "/balance/total" "Total Portfolio" "from-purple-500 to-pink-600"
                    ]

                    marketsSection
                    pipelinesSection
                    ordersSection
                ]

                _div [ _id_ "modal-container" ] []
            ]
        ]

    Response.ofHtml html
