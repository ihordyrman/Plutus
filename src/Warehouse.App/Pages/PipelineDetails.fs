namespace Warehouse.App.Pages.PipelineDetails

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Repositories

type PipelineDetailsInfo =
    { Id: int
      Symbol: string
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      Status: PipelineStatus
      Tags: string list
      StepsCount: int
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module Data =
    let getPipelineDetails
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (ct: CancellationToken)
        : Task<PipelineDetailsInfo option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! pipeline = PipelineRepository.getById db pipelineId ct

            match pipeline with
            | Ok pipeline ->
                let! steps = PipelineStepRepository.getByPipelineId db pipelineId ct

                let count =
                    match steps with
                    | Error _ -> 0
                    | Ok s -> s.Length

                return
                    Some
                        { Id = pipeline.Id
                          Symbol = pipeline.Symbol
                          MarketType = pipeline.MarketType
                          Enabled = pipeline.Enabled
                          ExecutionInterval = pipeline.ExecutionInterval
                          Status = pipeline.Status
                          Tags = pipeline.Tags
                          StepsCount = count
                          CreatedAt = pipeline.CreatedAt
                          UpdatedAt = pipeline.UpdatedAt }
            | Error _ -> return None
        }

module View =
    let private closeModalButton =
        _button [
            _type_ "button"
            _class_ "text-white hover:text-gray-200 transition-colors"
            Hx.get "/pipelines/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div [
            _class_ "fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"
            Hx.get "/pipelines/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] []

    let private statusBadge (enabled: bool) =
        if enabled then
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-green-100 text-green-800" ] [
                _i [ _class_ "fas fa-check-circle mr-1" ] []
                Text.raw "Enabled"
            ]
        else
            _span [ _class_ "px-3 py-1 rounded-full text-sm font-medium bg-gray-100 text-gray-600" ] [
                _i [ _class_ "fas fa-pause-circle mr-1" ] []
                Text.raw "Disabled"
            ]

    let private infoRow (label: string) (content: XmlNode) =
        _div [] [
            _dt [ _class_ "text-sm text-gray-500" ] [ Text.raw label ]
            _dd [ _class_ "text-base font-medium text-gray-900 mt-1" ] [ content ]
        ]

    let private basicInfoSection (pipeline: PipelineDetailsInfo) =
        _div [] [
            _h3 [ _class_ "text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide" ] [
                Text.raw "Basic Information"
            ]
            _dl [ _class_ "space-y-3" ] [
                infoRow
                    "Market Type"
                    (_span [ _class_ "inline-flex items-center px-3 py-1 rounded-md bg-blue-100 text-blue-800" ] [
                        _i [ _class_ "fas fa-exchange-alt mr-2" ] []
                        Text.raw (pipeline.MarketType.ToString())
                    ])

                infoRow "Symbol" (_span [] [ Text.raw pipeline.Symbol ])

                infoRow
                    "Status"
                    (if pipeline.Enabled then
                         _span [ _class_ "text-green-600" ] [
                             _i [ _class_ "fas fa-check-circle mr-1" ] []
                             Text.raw "Active"
                         ]
                     else
                         _span [ _class_ "text-gray-600" ] [
                             _i [ _class_ "fas fa-pause-circle mr-1" ] []
                             Text.raw "Inactive"
                         ])

                infoRow
                    "Execution Interval"
                    (_span [] [ Text.raw $"{int pipeline.ExecutionInterval.TotalMinutes} minutes" ])
            ]
        ]

    let private configSection (pipeline: PipelineDetailsInfo) =
        _div [] [
            _h3 [ _class_ "text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide" ] [
                Text.raw "Configuration"
            ]
            _dl [ _class_ "space-y-3" ] [
                infoRow "Pipeline ID" (_span [] [ Text.raw (string pipeline.Id) ])

                infoRow "Market Configuration" (_span [] [ Text.raw $"{pipeline.MarketType} / {pipeline.Symbol}" ])

                infoRow "Created" (_span [] [ Text.raw (pipeline.CreatedAt.ToString("MMM dd, yyyy HH:mm")) ])

                infoRow "Last Updated" (_span [] [ Text.raw (pipeline.UpdatedAt.ToString("MMM dd, yyyy HH:mm")) ])
            ]
        ]

    let private tagsSection (tags: string list) =
        _div [ _class_ "mt-4" ] [
            _h3 [ _class_ "text-sm font-semibold text-gray-700 mb-2 uppercase tracking-wide" ] [ Text.raw "Tags" ]
            if tags.IsEmpty then
                _p [ _class_ "text-sm text-gray-500 italic" ] [ Text.raw "No tags configured" ]
            else
                _div [ _class_ "flex flex-wrap gap-2" ] [
                    for tag in tags do
                        _span [ _class_ "px-2 py-1 text-xs bg-blue-100 text-blue-800 rounded-full" ] [ Text.raw tag ]
                ]
        ]

    let private stepsSection (pipelineId: int) (stepsCount: int) =
        _div [ _class_ "mt-6 pt-4 border-t" ] [
            _div [ _class_ "flex items-center justify-between mb-3" ] [
                _h3 [ _class_ "text-sm font-semibold text-gray-700 uppercase tracking-wide" ] [
                    _i [ _class_ "fas fa-project-diagram mr-2" ] []
                    Text.raw "Pipeline Steps"
                    if stepsCount > 0 then
                        _span [ _class_ "text-xs font-normal text-gray-500 ml-2" ] [
                            Text.raw $"({stepsCount} configured)"
                        ]
                ]
                _button [
                    _type_ "button"
                    _class_ "text-blue-600 hover:text-blue-800 text-sm"
                    Hx.get $"/pipelines/{pipelineId}/edit/modal"
                    Hx.targetCss "#modal-container"
                    Hx.swapInnerHtml
                ] [ _i [ _class_ "fas fa-cog mr-1" ] []; Text.raw "Configure" ]
            ]

            if stepsCount = 0 then
                _div [ _class_ "text-center py-6 text-gray-500" ] [
                    _i [ _class_ "fas fa-info-circle text-2xl mb-2" ] []
                    _p [] [ Text.raw "No pipeline steps configured yet" ]
                    _p [ _class_ "text-sm mt-1" ] [ Text.raw "Click \"Configure\" to set up trading strategies" ]
                ]
            else
                _div [ _class_ "text-center py-6 text-gray-600" ] [
                    _i [ _class_ "fas fa-check-circle text-green-600 text-2xl mb-2" ] []
                    _p [] [ Text.raw $"{stepsCount} pipeline step(s) configured" ]
                    _p [ _class_ "text-sm mt-1 text-gray-500" ] [ Text.raw "Click \"Configure\" to view and edit" ]
                ]
        ]

    let modal (pipeline: PipelineDetailsInfo) =
        _div [
            _id_ "pipeline-details-modal"
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
                        // Header
                        _div [ _class_ "bg-gradient-to-r from-blue-600 to-blue-700 px-6 py-4" ] [
                            _div [ _class_ "flex items-center justify-between" ] [
                                _div [] [
                                    _h3 [ _id_ "modal-title"; _class_ "text-lg font-semibold text-white" ] [
                                        _i [ _class_ "fas fa-info-circle mr-2" ] []
                                        Text.raw "Pipeline Details"
                                    ]
                                    _p [ _class_ "text-blue-100 text-sm mt-1" ] [
                                        Text.raw $"{pipeline.Symbol} • ID: {pipeline.Id}"
                                    ]
                                ]
                                _div [ _class_ "flex items-center space-x-3" ] [
                                    statusBadge pipeline.Enabled
                                    closeModalButton
                                ]
                            ]
                        ]

                        // Content
                        _div [ _class_ "px-6 py-4 max-h-[70vh] overflow-y-auto" ] [
                            _div [ _class_ "grid grid-cols-1 md:grid-cols-2 gap-6" ] [
                                basicInfoSection pipeline
                                configSection pipeline
                            ]

                            tagsSection pipeline.Tags
                            stepsSection pipeline.Id pipeline.StepsCount
                        ]

                        // Footer
                        _div [ _class_ "bg-gray-50 px-6 py-4 flex justify-between border-t" ] [
                            _button [
                                _type_ "button"
                                _class_
                                    "px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 font-medium rounded-lg transition-colors"
                                Hx.get "/pipelines/modal/close"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml
                            ] [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Close" ]

                            _button [
                                _type_ "button"
                                _class_
                                    "px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg shadow-sm hover:shadow-md transition-all"
                                Hx.get $"/pipelines/{pipeline.Id}/edit/modal"
                                Hx.targetCss "#modal-container"
                                Hx.swapInnerHtml
                            ] [ _i [ _class_ "fas fa-edit mr-2" ] []; Text.raw "Edit Pipeline" ]
                        ]
                    ]
                ]
            ]
        ]

    let notFound =
        _div [ _id_ "pipeline-details-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ] [
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
                        _h3 [ _class_ "text-lg font-semibold text-gray-900 mb-2" ] [ Text.raw "Pipeline Not Found" ]
                        _p [ _class_ "text-gray-600 mb-4" ] [ Text.raw "The requested pipeline could not be found." ]
                        _button [
                            _type_ "button"
                            _class_
                                "px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 font-medium rounded-lg transition-colors"
                            Hx.get "/pipelines/modal/close"
                            Hx.targetCss "#modal-container"
                            Hx.swapInnerHtml
                        ] [ Text.raw "Close" ]
                    ]
                ]
            ]
        ]

module Handler =
    let modal (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! pipeline = Data.getPipelineDetails scopeFactory pipelineId ctx.RequestAborted

                    match pipeline with
                    | Some p -> return! Response.ofHtml (View.modal p) ctx
                    | None -> return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineDetails")
                    logger.LogError(ex, "Error getting pipeline details for {PipelineId}", pipelineId)
                    return! Response.ofHtml View.notFound ctx
            }
