namespace Warehouse.App.Pages.AccountDetails

open System
open System.Data
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain

type AccountDetailsInfo =
    { Id: int
      MarketType: MarketType
      HasCredentials: bool
      IsSandbox: bool
      ApiKeyMasked: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module Data =
    open System.Threading
    open Warehouse.Core.Repositories

    let private maskApiKey (apiKey: string) =
        match apiKey with
        | "" -> "Not configured"
        | key when String.IsNullOrEmpty key -> "Not configured"
        | key when key.Length > 8 -> key.Substring(0, 4) + "****" + key.Substring(key.Length - 4)
        | _ -> "****"

    let getAccountDetails
        (scopeFactory: IServiceScopeFactory)
        (marketId: int)
        (ct: CancellationToken)
        : Task<AccountDetailsInfo option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! result = MarketRepository.getById db marketId ct

            match result with
            | Error _ -> return None
            | Ok data ->
                let apiKey = data.ApiKey
                let isSandbox = data.IsSandbox

                return
                    Some
                        { Id = data.Id
                          MarketType = data.Type
                          HasCredentials = true // is this needed? we always have apiKey
                          IsSandbox = isSandbox
                          ApiKeyMasked = maskApiKey apiKey
                          CreatedAt = data.CreatedAt
                          UpdatedAt = data.UpdatedAt }
        }

module View =
    let private closeModalButton =
        _button [
            _type_ "button"
            _class_ "text-white hover:text-gray-200 transition-colors"
            Hx.get "/accounts/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div [
            _class_ "fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"
            Hx.get "/accounts/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] []

    let private statusBadge (hasCredentials: bool) =
        if hasCredentials then
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-green-100 text-green-800" ] [
                _i [ _class_ "fas fa-check-circle mr-1" ] []
                Text.raw "Connected"
            ]
        else
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-gray-100 text-gray-600" ] [
                _i [ _class_ "fas fa-exclamation-circle mr-1" ] []
                Text.raw "Not Configured"
            ]

    let private modeBadge (isSandbox: bool) =
        if isSandbox then
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-yellow-100 text-yellow-800" ] [
                _i [ _class_ "fas fa-flask mr-1" ] []
                Text.raw "Sandbox"
            ]
        else
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-blue-100 text-blue-800" ] [
                _i [ _class_ "fas fa-bolt mr-1" ] []
                Text.raw "Live"
            ]

    let private infoRow (label: string) (content: XmlNode) =
        _div [] [
            _dt [ _class_ "text-sm text-gray-500" ] [ Text.raw label ]
            _dd [ _class_ "text-base font-medium text-gray-900 mt-1" ] [ content ]
        ]

    let private exchangeIcon (marketType: MarketType) =
        let (bgColor, iconColor) =
            match marketType with
            | MarketType.Okx -> "bg-blue-100", "text-blue-600"
            | MarketType.Binance -> "bg-yellow-100", "text-yellow-600"
            | MarketType.IBKR -> "bg-orange-100", "text-orange-600"
            | _ -> "bg-gray-100", "text-gray-600"

        _div [ _class_ $"w-16 h-16 {bgColor} rounded-xl flex items-center justify-center" ] [
            _i [ _class_ $"fas fa-exchange-alt text-2xl {iconColor}" ] []
        ]

    let private basicInfoSection (account: AccountDetailsInfo) =
        _div [] [
            _h3 [ _class_ "text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide" ] [
                Text.raw "Account Information"
            ]
            _dl [ _class_ "space-y-3" ] [
                infoRow "Exchange" (_span [] [ Text.raw (account.MarketType.ToString()) ])

                infoRow
                    "API Key"
                    (_code [ _class_ "text-sm bg-gray-100 px-2 py-1 rounded" ] [ Text.raw account.ApiKeyMasked ])

                infoRow "Mode" (modeBadge account.IsSandbox)

                infoRow "Status" (statusBadge account.HasCredentials)
            ]
        ]

    let private timestampsSection (account: AccountDetailsInfo) =
        _div [] [
            _h3 [ _class_ "text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide" ] [ Text.raw "Activity" ]
            _dl [ _class_ "space-y-3" ] [
                infoRow "Account ID" (_span [] [ Text.raw (string account.Id) ])
                infoRow "Connected" (_span [] [ Text.raw (account.CreatedAt.ToString("MMM dd, yyyy HH:mm")) ])
                infoRow "Last Updated" (_span [] [ Text.raw (account.UpdatedAt.ToString("MMM dd, yyyy HH:mm")) ])
            ]
        ]

    let modal (account: AccountDetailsInfo) =
        _div [
            _id_ "account-details-modal"
            _class_ "fixed inset-0 z-50 overflow-y-auto"
            Attr.create "aria-labelledby" "modal-title"
            Attr.create "role" "dialog"
            Attr.create "aria-modal" "true"
        ] [
            modalBackdrop

            _div [ _class_ "fixed inset-0 z-10 overflow-y-auto" ] [
                _div [ _class_ "flex min-h-full items-center justify-center p-4" ] [
                    _div [
                        _class_
                            "relative transform overflow-hidden rounded-xl bg-white shadow-2xl transition-all w-full max-w-2xl"
                    ] [
                        // header
                        _div [ _class_ "bg-gradient-to-r from-blue-600 to-blue-700 px-6 py-4" ] [
                            _div [ _class_ "flex items-center justify-between" ] [
                                _div [] [
                                    _h3 [ _id_ "modal-title"; _class_ "text-lg font-semibold text-white" ] [
                                        _i [ _class_ "fas fa-info-circle mr-2" ] []
                                        Text.raw "Account Details"
                                    ]
                                    _p [ _class_ "text-blue-100 text-sm mt-1" ] [
                                        Text.raw $"{account.MarketType} • ID: {account.Id}"
                                    ]
                                ]
                                _div [ _class_ "flex items-center space-x-3" ] [
                                    statusBadge account.HasCredentials
                                    closeModalButton
                                ]
                            ]
                        ]

                        // content
                        _div [ _class_ "px-6 py-6" ] [
                            // header
                            _div [ _class_ "flex items-center space-x-4 mb-6 pb-6 border-b" ] [
                                exchangeIcon account.MarketType
                                _div [] [
                                    _h2 [ _class_ "text-xl font-bold text-gray-900" ] [
                                        Text.raw (account.MarketType.ToString())
                                    ]
                                    _p [ _class_ "text-gray-500" ] [
                                        Text.raw (
                                            if account.IsSandbox then "Demo/Sandbox Account" else "Live Trading Account"
                                        )
                                    ]
                                ]
                            ]

                            _div [ _class_ "grid grid-cols-1 md:grid-cols-2 gap-6" ] [
                                basicInfoSection account
                                timestampsSection account
                            ]

                            // warning for sandbox
                            if account.IsSandbox then
                                _div [ _class_ "mt-6 bg-yellow-50 border border-yellow-200 rounded-md p-4" ] [
                                    _div [ _class_ "flex" ] [
                                        _div [ _class_ "flex-shrink-0" ] [
                                            _i [ _class_ "fas fa-info-circle text-yellow-600" ] []
                                        ]
                                        _div [ _class_ "ml-3" ] [
                                            _p [ _class_ "text-sm text-yellow-800" ] [
                                                Text.raw
                                                    "This account is in sandbox mode. Trades are simulated and do not use real funds."
                                            ]
                                        ]
                                    ]
                                ]
                        ]

                        // footer
                        _div [ _class_ "bg-gray-50 px-6 py-4 flex justify-between border-t" ] [
                            _button [
                                _type_ "button"
                                _class_
                                    "px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 font-medium rounded-lg transition-colors"
                                Hx.get "/accounts/modal/close"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml
                            ] [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Close" ]

                            _button [
                                _type_ "button"
                                _class_
                                    "px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg shadow-sm hover:shadow-md transition-all"
                                Hx.get $"/accounts/{account.Id}/edit/modal"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml
                            ] [ _i [ _class_ "fas fa-edit mr-2" ] []; Text.raw "Edit Account" ]
                        ]
                    ]
                ]
            ]
        ]

    let notFound =
        _div [ _id_ "account-details-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ] [
            modalBackdrop
            _div [ _class_ "fixed inset-0 z-10 overflow-y-auto" ] [
                _div [ _class_ "flex min-h-full items-center justify-center p-4" ] [
                    _div [
                        _class_
                            "relative transform overflow-hidden rounded-xl bg-white shadow-2xl w-full max-w-md p-6 text-center"
                    ] [
                        _div [
                            _class_ "mx-auto flex items-center justify-center h-16 w-16 rounded-full bg-red-100 mb-4"
                        ] [ _i [ _class_ "fas fa-exclamation-triangle text-3xl text-red-600" ] [] ]
                        _h3 [ _class_ "text-lg font-semibold text-gray-900 mb-2" ] [ Text.raw "Account Not Found" ]
                        _p [ _class_ "text-gray-600 mb-4" ] [ Text.raw "The requested account could not be found." ]
                        _button [
                            _type_ "button"
                            _class_
                                "px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 font-medium rounded-lg transition-colors"
                            Hx.get "/accounts/modal/close"
                            Hx.targetCss "#modal-container"
                            Hx.swapInnerHtml
                        ] [ Text.raw "Close" ]
                    ]
                ]
            ]
        ]

module Handler =
    let modal (marketId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! account = Data.getAccountDetails scopeFactory marketId ctx.RequestAborted

                    match account with
                    | Some a -> return! Response.ofHtml (View.modal a) ctx
                    | None -> return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("AccountDetails")
                    logger.LogError(ex, "Error getting account details for {MarketId}", marketId)
                    return! Response.ofHtml View.notFound ctx
            }
