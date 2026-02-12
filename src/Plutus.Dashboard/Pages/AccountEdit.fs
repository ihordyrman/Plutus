namespace Plutus.App.Pages.AccountEdit

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Repositories
open Plutus.Core.Shared

type EditAccountViewModel =
    { Id: int; MarketType: MarketType; ApiKeyMasked: string; HasPassphrase: bool; IsSandbox: bool }

type EditFormData =
    { ApiKey: string option
      SecretKey: string option
      Passphrase: string option
      IsSandbox: bool }

    static member Empty = { ApiKey = None; SecretKey = None; Passphrase = None; IsSandbox = true }

type EditResult =
    | Success
    | ValidationError of message: string
    | NotFoundError
    | ServerError of message: string

module Data =
    let private maskApiKey (apiKey: string) =
        if String.IsNullOrEmpty apiKey then ""
        elif apiKey.Length > 8 then apiKey.Substring(0, 4) + "****" + apiKey.Substring(apiKey.Length - 4)
        else "****"

    let getEditViewModel
        (scopeFactory: IServiceScopeFactory)
        (marketId: int)
        (ct: CancellationToken)
        : Task<EditAccountViewModel option>
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! result = MarketRepository.getById db marketId ct

                match result with
                | Error _ -> return None
                | Ok market ->
                    return
                        Some
                            { Id = market.Id
                              MarketType = market.Type
                              ApiKeyMasked = maskApiKey market.ApiKey
                              HasPassphrase =
                                market.Passphrase
                                |> Option.map (fun c -> not (String.IsNullOrEmpty c))
                                |> Option.defaultValue false
                              IsSandbox = market.IsSandbox }
            with _ ->
                return None
        }

    let parseFormData (form: FormData) : EditFormData =
        { ApiKey = form.TryGetString "apiKey"
          SecretKey = form.TryGetString "secretKey"
          Passphrase = form.TryGetString "passphrase"
          IsSandbox = form.TryGetString "isSandbox" |> Option.map (fun _ -> true) |> Option.defaultValue false }

    let updateAccount
        (scopeFactory: IServiceScopeFactory)
        (marketId: int)
        (formData: EditFormData)
        (ct: CancellationToken)
        : Task<EditResult>
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! existingMarket = MarketRepository.getById db marketId ct

                match existingMarket with
                | Error(Errors.NotFound _) -> return NotFoundError
                | Error err -> return ServerError(Errors.serviceMessage err)
                | Ok _ ->
                    let updateRequest: UpdateMarketRequest =
                        { ApiKey = formData.ApiKey |> Option.filter (String.IsNullOrWhiteSpace >> not)
                          SecretKey = formData.SecretKey |> Option.filter (String.IsNullOrWhiteSpace >> not)
                          Passphrase = formData.Passphrase
                          IsSandbox = Some formData.IsSandbox }

                    let! updateResult = MarketRepository.update db marketId updateRequest ct

                    match updateResult with
                    | Ok _ -> return Success
                    | Error err -> return ServerError(Errors.serviceMessage err)
            with ex ->
                return ServerError $"Failed to update account: {ex.Message}"
        }

    let deleteAccount (scopeFactory: IServiceScopeFactory) (marketId: int) (ct: CancellationToken) : Task<bool> =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! result = MarketRepository.delete db marketId ct

                match result with
                | Ok() -> return true
                | Error _ -> return false
            with _ ->
                return false
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

    let private apiKeyField (maskedValue: string) =
        _div
            []
            [ _label
                  [ _for_ "apiKey"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "API Key" ]
              _input
                  [ _id_ "apiKey"
                    _name_ "apiKey"
                    _type_ "password"
                    Attr.create
                        "placeholder"
                        (if String.IsNullOrEmpty maskedValue then "Enter API key" else maskedValue)
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
              _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw "Leave blank to keep current value" ] ]

    let private secretKeyField =
        _div
            []
            [ _label
                  [ _for_ "secretKey"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Secret Key" ]
              _input
                  [ _id_ "secretKey"
                    _name_ "secretKey"
                    _type_ "password"
                    Attr.create "placeholder" "Enter new secret key (or leave blank)"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
              _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw "Leave blank to keep current value" ] ]

    let private passphraseField (hasPassphrase: bool) =
        _div
            []
            [ _label
                  [ _for_ "passphrase"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Passphrase" ]
              _input
                  [ _id_ "passphrase"
                    _name_ "passphrase"
                    _type_ "password"
                    Attr.create
                        "placeholder"
                        (if hasPassphrase then "Enter new passphrase (or leave blank)" else "Enter passphrase")
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
              _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw "Required for OKX accounts" ] ]

    let private sandboxField (isSandbox: bool) =
        _div
            [ _class_ "flex items-center" ]
            [ _input
                  [ _id_ "isSandbox"
                    _name_ "isSandbox"
                    _type_ "checkbox"
                    _class_ "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded"
                    if isSandbox then
                        Attr.create "checked" "checked" ]
              _label [ _for_ "isSandbox"; _class_ "ml-2 block text-sm text-slate-700" ] [ Text.raw "Sandbox/Demo mode" ] ]

    let private dangerZone (marketId: int) =
        _div
            [ _class_ "mt-6 pt-4 border-t border-slate-200" ]
            [ _h4
                  [ _class_ "text-sm font-semibold text-red-700 mb-3" ]
                  [ _i [ _class_ "fas fa-exclamation-triangle mr-2" ] []; Text.raw "Danger Zone" ]
              _p
                  [ _class_ "text-sm text-slate-600 mb-3" ]
                  [ Text.raw "Removing this account will disconnect it from all pipelines." ]
              _button
                  [ _type_ "button"
                    _class_
                        "px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-medium rounded-md text-sm transition-colors"
                    Hx.delete $"/accounts/{marketId}"
                    Hx.confirm "Are you sure you want to delete this account? This action cannot be undone."
                    Hx.targetCss "#modal-container"
                    Hx.swapInnerHtml ]
                  [ _i [ _class_ "fas fa-trash mr-2" ] []; Text.raw "Delete Account" ] ]

    let modal (vm: EditAccountViewModel) =
        _div
            [ _id_ "account-edit-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
              Attr.create "aria-labelledby" "modal-title"
              Attr.create "role" "dialog"
              Attr.create "aria-modal" "true" ]
            [ modalBackdrop

              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-lg" ]
                              [
                                // header
                                _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _div
                                                []
                                                [ _h3
                                                      [ _id_ "modal-title"
                                                        _class_ "text-lg font-semibold text-slate-900" ]
                                                      [ _i [ _class_ "fas fa-edit mr-2 text-slate-400" ] []
                                                        Text.raw "Edit Account" ]
                                                  _p
                                                      [ _class_ "text-slate-500 text-sm mt-1" ]
                                                      [ Text.raw $"{vm.MarketType} • ID: {vm.Id}" ] ]
                                            closeModalButton ] ]

                                // form
                                _form
                                    [ _method_ "post"
                                      Hx.post $"/accounts/{vm.Id}/edit"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ _div
                                          [ _class_ "px-6 py-4 space-y-4 max-h-[60vh] overflow-y-auto" ]
                                          [ _div
                                                []
                                                [ _label
                                                      [ _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                                                      [ Text.raw "Exchange" ]
                                                  _div
                                                      [ _class_
                                                            "w-full px-3 py-2 border border-slate-200 rounded-md bg-slate-50 text-slate-700" ]
                                                      [ _i [ _class_ "fas fa-exchange-alt mr-2" ] []
                                                        Text.raw (vm.MarketType.ToString()) ] ]

                                            apiKeyField vm.ApiKeyMasked
                                            secretKeyField
                                            passphraseField vm.HasPassphrase
                                            sandboxField vm.IsSandbox

                                            dangerZone vm.Id ]

                                      // footer
                                      _div
                                          [ _class_ "px-6 py-4 flex justify-end space-x-3 border-t border-slate-100" ]
                                          [ _button
                                                [ _type_ "button"
                                                  _class_
                                                      "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                                  Hx.get "/accounts/modal/close"
                                                  Hx.targetCss "#modal-container"
                                                  Hx.swapInnerHtml ]
                                                [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Cancel" ]
                                            _button
                                                [ _type_ "submit"
                                                  _class_
                                                      "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
                                                [ _i [ _class_ "fas fa-save mr-2" ] []; Text.raw "Save Changes" ] ] ] ] ] ] ]

    let successResponse (marketId: int) =
        _div
            [ _id_ "account-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity" ] []
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-check text-3xl text-green-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Account Updated!" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw "Your changes have been saved successfully." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/accounts/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml
                                      Attr.create "hx-on::after-request" "htmx.trigger('#accounts-container', 'load')" ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let deletedResponse =
        _div
            [ _id_ "account-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity" ] []
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-check text-3xl text-green-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Account Deleted" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw "The account has been removed successfully." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/accounts/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml
                                      Attr.create "hx-on::after-request" "htmx.trigger('#accounts-container', 'load')" ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let errorResponse (message: string) (marketId: int) =
        _div
            [ _id_ "account-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity" ] []
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
                                _h3 [ _class_ "text-lg font-semibold text-slate-900 mb-2" ] [ Text.raw "Error" ]
                                _p [ _class_ "text-slate-600 mb-4" ] [ Text.raw message ]
                                _div
                                    [ _class_ "flex justify-center space-x-3" ]
                                    [ _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/accounts/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Close" ]
                                      _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                            Hx.get $"/accounts/{marketId}/edit/modal"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Try Again" ] ] ] ] ] ]

    let notFound =
        _div
            [ _id_ "account-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
    let modal (marketId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! vm = Data.getEditViewModel scopeFactory marketId ctx.RequestAborted

                    match vm with
                    | Some v -> return! Response.ofHtml (View.modal v) ctx
                    | None -> return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("AccountEdit")
                    logger.LogError(ex, "Error getting account edit view for {MarketId}", marketId)
                    return! Response.ofHtml View.notFound ctx
            }

    let update (marketId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let! form = Request.getForm ctx
                    let formData = Data.parseFormData form
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! result = Data.updateAccount scopeFactory marketId formData ctx.RequestAborted

                    match result with
                    | Success -> return! Response.ofHtml (View.successResponse marketId) ctx
                    | ValidationError msg -> return! Response.ofHtml (View.errorResponse msg marketId) ctx
                    | NotFoundError -> return! Response.ofHtml View.notFound ctx
                    | ServerError msg -> return! Response.ofHtml (View.errorResponse msg marketId) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("AccountEdit")
                    logger.LogError(ex, "Error updating account {MarketId}", marketId)
                    return! Response.ofHtml (View.errorResponse "An unexpected error occurred" marketId) ctx
            }

    let delete (marketId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! deleted = Data.deleteAccount scopeFactory marketId ctx.RequestAborted

                    if deleted then
                        return! Response.ofHtml View.deletedResponse ctx
                    else
                        return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("AccountEdit")
                    logger.LogError(ex, "Error deleting account {MarketId}", marketId)
                    return! Response.ofHtml (View.errorResponse "Failed to delete account" marketId) ctx
            }
