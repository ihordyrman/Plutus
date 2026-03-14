namespace Plutus.App.Pages.AccountDetails

open System
open System.Data
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain

type AccountDetailsInfo = { Id: int; MarketType: MarketType; IsSandbox: bool; CreatedAt: DateTime; UpdatedAt: DateTime }

module Data =
    open System.Threading
    open Plutus.Core.Infrastructure
    open Plutus.Core.Repositories

    let getAccountDetails
        (scopeFactory: IServiceScopeFactory)
        (credentialsSettings: MarketSettings)
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
                let isSandbox =
                    credentialsSettings.Credentials
                    |> Array.tryFind (fun c -> c.MarketType = string data.Type)
                    |> Option.map (fun c -> c.IsSandbox)
                    |> Option.defaultValue false

                return
                    Some
                        { Id = data.Id
                          MarketType = data.Type
                          IsSandbox = isSandbox
                          CreatedAt = data.CreatedAt
                          UpdatedAt = data.UpdatedAt }
        }

module View =
    let private closeModalButton =
        _button
            [ _type_ "button"
              _class_ "text-slate-400 hover:text-slate-600 transition-colors"
              Hx.get "/accounts/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div
            [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
              Hx.get "/accounts/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            []

    let private modeBadge (isSandbox: bool) =
        if isSandbox then
            _span
                [ _class_ "px-3 py-1 rounded text-sm font-medium bg-yellow-50 text-yellow-700" ]
                [ _i [ _class_ "fas fa-flask mr-1" ] []; Text.raw "Sandbox" ]
        else
            _span
                [ _class_ "px-3 py-1 rounded text-sm font-medium bg-slate-100 text-slate-700" ]
                [ _i [ _class_ "fas fa-bolt mr-1" ] []; Text.raw "Live" ]

    let private infoRow (label: string) (content: XmlNode) =
        _div
            []
            [ _dt [ _class_ "text-sm text-slate-500" ] [ Text.raw label ]
              _dd [ _class_ "text-base font-medium text-slate-900 mt-1" ] [ content ] ]

    let private exchangeIcon (marketType: MarketType) =
        let (bgColor, iconColor) =
            match marketType with
            | MarketType.Okx -> "bg-slate-100", "text-slate-500"
            | MarketType.Binance -> "bg-slate-100", "text-slate-500"
            | MarketType.IBKR -> "bg-slate-100", "text-slate-500"
            | _ -> "bg-slate-100", "text-slate-500"

        _div
            [ _class_ $"w-16 h-16 {bgColor} rounded-md flex items-center justify-center" ]
            [ _i [ _class_ $"fas fa-exchange-alt text-2xl {iconColor}" ] [] ]

    let private basicInfoSection (account: AccountDetailsInfo) =
        _div
            []
            [ _h3
                  [ _class_ "text-sm font-semibold text-slate-700 mb-3 uppercase tracking-wide" ]
                  [ Text.raw "Account Information" ]
              _dl
                  [ _class_ "space-y-3" ]
                  [ infoRow "Exchange" (_span [] [ Text.raw (string account.MarketType) ])
                    infoRow "Mode" (modeBadge account.IsSandbox) ] ]

    let private timestampsSection (account: AccountDetailsInfo) =
        _div
            []
            [ _h3
                  [ _class_ "text-sm font-semibold text-slate-700 mb-3 uppercase tracking-wide" ]
                  [ Text.raw "Activity" ]
              _dl
                  [ _class_ "space-y-3" ]
                  [ infoRow "Account ID" (_span [] [ Text.raw (string account.Id) ])
                    infoRow "Connected" (_span [] [ Text.raw (account.CreatedAt.ToString("MMM dd, yyyy HH:mm")) ])
                    infoRow "Last Updated" (_span [] [ Text.raw (account.UpdatedAt.ToString("MMM dd, yyyy HH:mm")) ]) ] ]

    let modal (account: AccountDetailsInfo) =
        _div
            [ _id_ "account-details-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
              Attr.create "aria-labelledby" "modal-title"
              _role_ "dialog"
              Attr.create "aria-modal" "true" ]
            [ modalBackdrop

              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-2xl" ]
                              [ _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _div
                                                []
                                                [ _h3
                                                      [ _id_ "modal-title"
                                                        _class_ "text-lg font-semibold text-slate-900" ]
                                                      [ _i [ _class_ "fas fa-info-circle mr-2 text-slate-400" ] []
                                                        Text.raw "Account Details" ]
                                                  _p
                                                      [ _class_ "text-slate-500 text-sm mt-1" ]
                                                      [ Text.raw $"{account.MarketType} • ID: {account.Id}" ] ]
                                            closeModalButton ] ]

                                _div
                                    [ _class_ "px-6 py-6" ]
                                    [ _div
                                          [ _class_ "flex items-center space-x-4 mb-6 pb-6 border-b" ]
                                          [ exchangeIcon account.MarketType
                                            _div
                                                []
                                                [ _h2
                                                      [ _class_ "text-xl font-bold text-slate-900" ]
                                                      [ Text.raw (string account.MarketType) ]
                                                  _p
                                                      [ _class_ "text-slate-500" ]
                                                      [ Text.raw (
                                                            if account.IsSandbox then
                                                                "Demo/Sandbox Account"
                                                            else
                                                                "Live Trading Account"
                                                        ) ] ] ]

                                      _div
                                          [ _class_ "grid grid-cols-1 md:grid-cols-2 gap-6" ]
                                          [ basicInfoSection account; timestampsSection account ] ]

                                _div
                                    [ _class_ "px-6 py-4 flex justify-end border-t border-slate-100" ]
                                    [ _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/accounts/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Close" ] ] ] ] ] ]

    let notFound =
        _div
            [ _id_ "account-details-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ modalBackdrop
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-exclamation-triangle text-3xl text-red-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Account Not Found" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw "The requested account could not be found." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/accounts/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

module Handler =
    open Microsoft.Extensions.Options
    open Plutus.Core.Infrastructure

    let modal (marketId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let credentialsSettings = ctx.Plug<IOptions<MarketSettings>>().Value
                    let! account = Data.getAccountDetails scopeFactory credentialsSettings marketId ctx.RequestAborted

                    match account with
                    | Some a -> return! Response.ofHtml (View.modal a) ctx
                    | None -> return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("AccountDetails")
                    logger.LogError(ex, "Error getting account details for {MarketId}", marketId)
                    return! Response.ofHtml View.notFound ctx
            }
