namespace Plutus.App.Pages.CreatePipeline

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

type CreatePipelineInput =
    { MarketType: MarketType
      Symbol: string
      Tags: string list
      ExecutionInterval: TimeSpan
      Enabled: bool }

type CreateResult =
    | Success of symbol: string
    | ValidationError of message: string
    | ServerError of message: string

type FormDataInfo =
    { MarketType: int option
      Symbol: string option
      Tags: string option
      ExecutionInterval: int option
      Enabled: bool }

    static member Empty = { MarketType = None; Symbol = None; Tags = None; ExecutionInterval = None; Enabled = false }

module Data =
    let private marketTypes = [ MarketType.Okx; MarketType.Binance ]

    let getMarketTypes () : MarketType list = marketTypes

    let parseFormData (form: FormData) : FormDataInfo =
        { MarketType = form.TryGetInt "marketType"
          Symbol = form.TryGetString "symbol"
          Tags = form.TryGetString "tags"
          ExecutionInterval = form.TryGetInt "executionInterval"
          Enabled = form.TryGetString "enabled" |> Option.map (fun _ -> true) |> Option.defaultValue false }

    let validateAndCreate (formData: FormDataInfo) : Result<CreatePipelineInput, string> =
        match formData.Symbol with
        | None -> Error "Symbol is required"
        | Some symbol when String.IsNullOrWhiteSpace(symbol) -> Error "Symbol is required"
        | Some symbol ->
            let marketType =
                formData.MarketType |> Option.map enum<MarketType> |> Option.defaultValue MarketType.Okx

            let tags =
                formData.Tags
                |> Option.map (fun t ->
                    t.Split(',')
                    |> Array.map (fun s -> s.Trim())
                    |> Array.filter (not << String.IsNullOrWhiteSpace)
                    |> List.ofArray
                )
                |> Option.defaultValue []

            let interval =
                formData.ExecutionInterval
                |> Option.map (fun m -> TimeSpan.FromMinutes(float m))
                |> Option.defaultValue (TimeSpan.FromMinutes 5.0)

            Ok
                { MarketType = marketType
                  Symbol = symbol.Trim().ToUpperInvariant()
                  Tags = tags
                  ExecutionInterval = interval
                  Enabled = formData.Enabled }

    let createPipeline
        (scopeFactory: IServiceScopeFactory)
        (input: CreatePipelineInput)
        (ct: CancellationToken)
        : Task<CreateResult>
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let pipeline: Pipeline =
                    { Id = 0
                      Name = input.Symbol
                      Symbol = input.Symbol
                      MarketType = input.MarketType
                      Enabled = input.Enabled
                      ExecutionInterval = input.ExecutionInterval
                      LastExecutedAt = Nullable()
                      Status = PipelineStatus.Idle
                      Tags = input.Tags
                      CreatedAt = DateTime.UtcNow
                      UpdatedAt = DateTime.UtcNow }

                let! result = PipelineRepository.create db pipeline ct

                match result with
                | Ok created -> return Success created.Symbol
                | Error err ->
                    let message = Errors.serviceMessage err
                    return ServerError message
            with ex ->
                return ServerError $"Failed to create pipeline: {ex.Message}"
        }

module View =
    let private marketTypes = Data.getMarketTypes ()

    let private marketTypeField =
        _div
            []
            [ _label
                  [ _for_ "marketType"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Market Type "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _select
                  [ _id_ "marketType"
                    _name_ "marketType"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                    Attr.create
                        "hx-on:change"
                        "var b=document.getElementById('baseCurrency');if(b)htmx.trigger(b,'load')" ]
                  [ for marketType in marketTypes do
                        _option [ _value_ (string (int marketType)) ] [ Text.raw (marketType.ToString()) ] ] ]

    let private symbolFields =
        let selectClass =
            "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"

        _div
            [ _class_ "space-y-3" ]
            [ _input [ _id_ "symbol"; _name_ "symbol"; _type_ "hidden" ]
              _div
                  []
                  [ _label
                        [ _for_ "baseCurrency"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "Base Currency "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _select
                        [ _id_ "baseCurrency"
                          _name_ "baseCurrency"
                          _class_ selectClass
                          Hx.get "/instruments/base-currencies"
                          Hx.trigger "load"
                          Hx.includeCss "[name='marketType']"
                          Hx.targetCss "#baseCurrency"
                          Hx.swap HxSwap.InnerHTML
                          Attr.create
                              "hx-on::after-settle"
                              "var q=document.getElementById('quoteCurrency');if(q){htmx.trigger(q,'change')};var s=document.getElementById('symbol');if(s)s.value=''" ]
                        [ _option [ _value_ "" ] [ Text.raw "Loading..." ] ] ]
              _div
                  []
                  [ _label
                        [ _for_ "quoteCurrency"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "Quote Currency "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _select
                        [ _id_ "quoteCurrency"
                          _name_ "quoteCurrency"
                          _class_ selectClass
                          Hx.get "/instruments/quote-currencies"
                          Hx.trigger "change from:#baseCurrency"
                          Hx.includeCss "[name='marketType'],[name='baseCurrency']"
                          Hx.targetCss "#quoteCurrency"
                          Hx.swap HxSwap.InnerHTML
                          Attr.create
                              "hx-on::after-settle"
                              "var b=document.getElementById('baseCurrency'),q=document.getElementById('quoteCurrency'),s=document.getElementById('symbol');if(b&&q&&s&&b.value&&q.value)s.value=b.value+'-'+q.value;else if(s)s.value=''" ]
                        [ _option [ _value_ "" ] [ Text.raw "-- Select base first --" ] ] ] ]

    let private tagsField =
        _div
            []
            [ _label [ _for_ "tags"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ] [ Text.raw "Tags" ]
              _input
                  [ _id_ "tags"
                    _name_ "tags"
                    _type_ "text"
                    Attr.create "placeholder" "e.g., scalping, high-frequency, btc"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
              _p
                  [ _class_ "text-sm text-slate-500 mt-1" ]
                  [ Text.raw "Enter tags separated by commas. Tags help organize and filter pipelines." ] ]

    let private executionIntervalField =
        _div
            []
            [ _label
                  [ _for_ "executionInterval"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Execution Interval (minutes) "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _input
                  [ _id_ "executionInterval"
                    _name_ "executionInterval"
                    _type_ "number"
                    Attr.create "min" "1"
                    Attr.create "placeholder" "e.g., 5"
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                    Attr.create "required" "required" ]
              _p
                  [ _class_ "text-sm text-slate-500 mt-1" ]
                  [ Text.raw "Specify how often the pipeline should execute (in minutes)." ] ]

    let private enabledField =
        _div
            [ _class_ "flex items-center" ]
            [ _input
                  [ _id_ "enabled"
                    _name_ "enabled"
                    _type_ "checkbox"
                    _class_ "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded" ]
              _label
                  [ _for_ "enabled"; _class_ "ml-2 block text-sm text-slate-700" ]
                  [ Text.raw "Enable pipeline immediately" ] ]

    let private helpSection =
        _div
            [ _class_ "mt-4 bg-slate-50 border border-slate-200 rounded-md p-3" ]
            [ _h4
                  [ _class_ "text-xs font-semibold text-slate-700 mb-1" ]
                  [ _i [ _class_ "fas fa-info-circle mr-1" ] []; Text.raw "Tips" ]
              _ul
                  [ _class_ "text-xs text-slate-500 space-y-0.5" ]
                  [ _li [] [ Text.raw "• Select base and quote currencies to define the trading pair" ]
                    _li [] [ Text.raw "• Each market + symbol combination must be unique" ] ] ]

    let private closeModalButton =
        _button
            [ _type_ "button"
              _class_ "text-slate-400 hover:text-slate-600 transition-colors"
              Hx.get "/pipelines/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div
            [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
              Hx.get "/pipelines/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            []

    let private cancelButton =
        _button
            [ _type_ "button"
              _class_ "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
              Hx.get "/pipelines/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Cancel" ]

    let private submitButton =
        _button
            [ _type_ "submit"
              _class_
                  "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
            [ _i [ _class_ "fas fa-plus mr-2" ] []; Text.raw "Create Pipeline" ]

    let modal =
        _div
            [ _id_ "pipeline-modal"
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
                                // Header
                                _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _h3
                                                [ _id_ "modal-title"; _class_ "text-lg font-semibold text-slate-900" ]
                                                [ _i [ _class_ "fas fa-plus-circle mr-2 text-slate-400" ] []
                                                  Text.raw "Create New Pipeline" ]
                                            closeModalButton ]
                                      _p
                                          [ _class_ "text-slate-500 text-sm mt-1" ]
                                          [ Text.raw "Configure a new trading pipeline" ] ]

                                // Form
                                _form
                                    [ _method_ "post"
                                      Hx.post "/pipelines/create"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ _div
                                          [ _class_ "px-6 py-4 space-y-4 max-h-[60vh] overflow-y-auto" ]
                                          [ marketTypeField
                                            symbolFields
                                            tagsField
                                            executionIntervalField
                                            enabledField
                                            helpSection ]

                                      // Footer
                                      _div
                                          [ _class_ "px-6 py-4 flex justify-end space-x-3 border-t border-slate-100" ]
                                          [ cancelButton; submitButton ] ] ] ] ] ]

    let closeModal = _div [] []

    let successResponse (symbol: string) =
        _div
            [ _id_ "pipeline-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div
                  [ Hx.get "/pipelines/grid"
                    Hx.targetCss "#pipelines-container"
                    Hx.swapInnerHtml
                    Hx.trigger "load" ]
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
                                    [ Text.raw "Pipeline Created!" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw $"Pipeline for {symbol} has been created successfully." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/pipelines/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let errorResponse (message: string) =
        _div
            [ _id_ "pipeline-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
                                            Hx.get "/pipelines/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Close" ]
                                      _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/pipelines/modal"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Try Again" ] ] ] ] ] ]

    let createResult (result: CreateResult) =
        match result with
        | Success symbol -> successResponse symbol
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
                        let! result = Data.createPipeline scopeFactory input ctx.RequestAborted
                        return! Response.ofHtml (View.createResult result) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("CreatePipeline")
                    logger.LogError(ex, "Error creating pipeline")
                    return! Response.ofHtml (View.createResult (ServerError "An unexpected error occurred")) ctx
            }
