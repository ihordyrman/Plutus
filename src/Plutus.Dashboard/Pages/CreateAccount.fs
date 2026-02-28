namespace Plutus.App.Pages.CreateAccount

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

type CreateAccountInput =
    { MarketType: MarketType; ApiKey: string; SecretKey: string; Passphrase: string; IsSandbox: bool }

type CreateResult =
    | Success of marketType: string
    | ValidationError of message: string
    | AlreadyExists of marketType: string
    | ServerError of message: string

type FormDataInfo =
    { MarketType: int option
      ApiKey: string option
      SecretKey: string option
      Passphrase: string option
      IsSandbox: bool }

    static member Empty = { MarketType = None; ApiKey = None; SecretKey = None; Passphrase = None; IsSandbox = true }

module Data =
    let private marketTypes = [ MarketType.Okx; MarketType.Binance ]

    let getMarketTypes () : MarketType list = marketTypes

    let parseFormData (form: FormData) : FormDataInfo =
        { MarketType = form.TryGetInt "marketType"
          ApiKey = form.TryGetString "apiKey"
          SecretKey = form.TryGetString "secretKey"
          Passphrase = form.TryGetString "passphrase"
          IsSandbox = form.TryGetString "isSandbox" |> Option.map (fun _ -> true) |> Option.defaultValue false }

    let validateAndCreate (formData: FormDataInfo) : Result<CreateAccountInput, string> =
        match formData.ApiKey, formData.SecretKey with
        | None, _ -> Error "API Key is required"
        | _, None -> Error "Secret Key is required"
        | Some apiKey, _ when String.IsNullOrWhiteSpace(apiKey) -> Error "API Key is required"
        | _, Some secretKey when String.IsNullOrWhiteSpace(secretKey) -> Error "Secret Key is required"
        | Some apiKey, Some secretKey ->
            let marketType =
                formData.MarketType |> Option.map enum<MarketType> |> Option.defaultValue MarketType.Okx

            let passphrase = formData.Passphrase |> Option.defaultValue ""

            Ok
                { MarketType = marketType
                  ApiKey = apiKey.Trim()
                  SecretKey = secretKey.Trim()
                  Passphrase = passphrase.Trim()
                  IsSandbox = formData.IsSandbox }

    let createAccount
        (scopeFactory: IServiceScopeFactory)
        (input: CreateAccountInput)
        (ct: CancellationToken)
        : Task<CreateResult>
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! existingMarket = MarketRepository.exists db input.MarketType ct

                match existingMarket with
                | Error err -> return ServerError $"Failed to check existing accounts: {err}"
                | Ok exist ->
                    if exist then
                        return AlreadyExists(string input.MarketType)
                    else
                        let! market =
                            MarketRepository.create
                                db
                                { Type = input.MarketType
                                  ApiKey = input.ApiKey
                                  SecretKey = input.SecretKey
                                  Passphrase = Some input.Passphrase
                                  IsSandbox = input.IsSandbox }
                                ct

                        match market with
                        | Error err -> return ServerError $"Failed to create account: {err}"
                        | Ok market -> return Success(string market.Type)
            with ex ->
                return ServerError $"Failed to create account: {ex.Message}"
        }

module View =
    let private marketTypes = Data.getMarketTypes ()

    let private marketTypeField =
        _div
            []
            [ _label
                  [ _for_ "marketType"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Exchange "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _select
                  [ _id_ "marketType"
                    _name_ "marketType"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                  [ for marketType in marketTypes do
                        _option [ _value_ (string (int marketType)) ] [ Text.raw (string marketType) ] ] ]

    let private apiKeyField =
        _div
            []
            [ _label
                  [ _for_ "apiKey"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "API Key "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _input
                  [ _id_ "apiKey"
                    _name_ "apiKey"
                    _type_ "password"
                    _placeholder_ "Enter your API key"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                    _required_ ] ]

    let private secretKeyField =
        _div
            []
            [ _label
                  [ _for_ "secretKey"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Secret Key "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _input
                  [ _id_ "secretKey"
                    _name_ "secretKey"
                    _type_ "password"
                    _placeholder_ "Enter your secret key"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                    _required_ ] ]

    let private passphraseField =
        _div
            []
            [ _label
                  [ _for_ "passphrase"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Passphrase" ]
              _input
                  [ _id_ "passphrase"
                    _name_ "passphrase"
                    _type_ "password"
                    _placeholder_ "Enter passphrase (if required)"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
              _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw "Required for OKX accounts" ] ]

    let private sandboxField =
        _div
            [ _class_ "flex items-center" ]
            [ _input
                  [ _id_ "isSandbox"
                    _name_ "isSandbox"
                    _type_ "checkbox"
                    _class_ "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded"
                    _checked_ ]
              _label [ _for_ "isSandbox"; _class_ "ml-2 block text-sm text-slate-700" ] [ Text.raw "Sandbox/Demo mode" ] ]

    let private helpSection =
        _div
            [ _class_ "mt-4 bg-slate-50 border border-slate-200 rounded-md p-3" ]
            [ _h4
                  [ _class_ "text-xs font-semibold text-slate-700 mb-1" ]
                  [ _i [ _class_ "fas fa-exclamation-triangle mr-1" ] []; Text.raw "Security Notice" ]
              _ul
                  [ _class_ "text-xs text-slate-500 space-y-0.5" ]
                  [ _li [] [ Text.raw "• API credentials are stored securely" ]
                    _li [] [ Text.raw "• Use API keys with limited permissions" ]
                    _li [] [ Text.raw "• Start with sandbox mode for testing" ] ] ]

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

    let private cancelButton =
        _button
            [ _type_ "button"
              _class_ "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
              Hx.get "/accounts/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Cancel" ]

    let private submitButton =
        _button
            [ _type_ "submit"
              _class_
                  "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
            [ _i [ _class_ "fas fa-plus mr-2" ] []; Text.raw "Add Account" ]

    let modal =
        _div
            [ _id_ "account-modal"
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
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-lg" ]
                              [
                                // header
                                _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _h3
                                                [ _id_ "modal-title"; _class_ "text-lg font-semibold text-slate-900" ]
                                                [ _i [ _class_ "fas fa-plus-circle mr-2 text-slate-400" ] []
                                                  Text.raw "Add Exchange Account" ]
                                            closeModalButton ]
                                      _p
                                          [ _class_ "text-slate-500 text-sm mt-1" ]
                                          [ Text.raw "Connect your exchange API credentials" ] ]

                                // form
                                _form
                                    [ _method_ "post"
                                      Hx.post "/accounts/create"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ _div
                                          [ _class_ "px-6 py-4 space-y-4 max-h-[60vh] overflow-y-auto" ]
                                          [ marketTypeField
                                            apiKeyField
                                            secretKeyField
                                            passphraseField
                                            sandboxField
                                            helpSection ]

                                      // Footer
                                      _div
                                          [ _class_ "px-6 py-4 flex justify-end space-x-3 border-t border-slate-100" ]
                                          [ cancelButton; submitButton ] ] ] ] ] ]

    let closeModal = _div [] []

    let successResponse (marketType: string) =
        _div
            [ _id_ "account-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div
                  [ Hx.get "/markets/grid"; Hx.targetCss "#accounts-container"; Hx.swapInnerHtml; Hx.trigger "load" ]
                  []
              _div [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity" ] []
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
                                    [ Text.raw "Account Added!" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw $"{marketType} account has been connected successfully." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/accounts/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let alreadyExistsResponse (marketType: string) =
        _div
            [ _id_ "account-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-yellow-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-exclamation text-3xl text-yellow-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Account Exists" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw $"A {marketType} account is already configured. You can edit it instead." ]
                                _div
                                    [ _class_ "flex justify-center space-x-3" ]
                                    [ _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/accounts/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Close" ] ] ] ] ] ]

    let errorResponse (message: string) =
        _div
            [ _id_ "account-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
                                            Hx.get "/accounts/modal"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Try Again" ] ] ] ] ] ]

    let createResult (result: CreateResult) =
        match result with
        | Success marketType -> successResponse marketType
        | AlreadyExists marketType -> alreadyExistsResponse marketType
        | ValidationError msg -> errorResponse msg
        | ServerError msg -> errorResponse msg

module Handler =
    let modal: HttpHandler = Response.ofHtml View.modal
    let closeModal: HttpHandler = Response.ofHtml View.closeModal

    let create: HttpHandler =
        fun ctx ->
            task {
                try
                    let! form = Request.getForm ctx
                    let formData = Data.parseFormData form

                    match Data.validateAndCreate formData with
                    | Error msg -> return! Response.ofHtml (View.createResult (ValidationError msg)) ctx
                    | Ok input ->
                        let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                        let! result = Data.createAccount scopeFactory input ctx.RequestAborted
                        return! Response.ofHtml (View.createResult result) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("CreateAccount")
                    logger.LogError(ex, "Error creating account")
                    return! Response.ofHtml (View.createResult (ServerError "An unexpected error occurred")) ctx
            }
