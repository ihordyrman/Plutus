namespace Plutus.App.Pages.PipelineTraces

open System
open System.Data
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories
open Plutus.Core.Shared

type ExecutionSummary =
    { ExecutionId: string
      StartTime: DateTime
      Duration: TimeSpan
      StepCount: int
      Outcome: StepOutcome }

type ExecutionListData =
    { PipelineId: int
      Executions: ExecutionSummary list
      TotalCount: int
      Page: int
      PageSize: int }

type StepTrace =
    { StepTypeKey: string
      Outcome: StepOutcome
      Message: string
      StartTime: DateTime
      EndTime: DateTime
      Duration: TimeSpan
      ContextSnapshot: string }

type ExecutionDetail =
    { PipelineId: int
      ExecutionId: string
      Steps: StepTrace list
      TotalDuration: TimeSpan
      OverallOutcome: StepOutcome }

module Data =
    let private mapOutcome (value: int) : StepOutcome =
        match value with
        | 0 -> StepOutcome.Success
        | 1 -> StepOutcome.Stopped
        | _ -> StepOutcome.Failed

    let getExecutions
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (page: int)
        (pageSize: int)
        (ct: CancellationToken)
        : Task<ExecutionListData>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let skip = (page - 1) * pageSize
            let! rows = ExecutionLogRepository.getExecutionsByPipelineId db pipelineId skip pageSize ct
            let! total = ExecutionLogRepository.countExecutionsByPipelineId db pipelineId ct

            let executions =
                match rows with
                | Ok rows ->
                    rows
                    |> List.map (fun r ->
                        { ExecutionId = r.ExecutionId
                          StartTime = r.StartTime
                          Duration = r.EndTime - r.StartTime
                          StepCount = r.StepCount
                          Outcome = mapOutcome r.WorstOutcome })
                | Error _ -> []

            let totalCount =
                match total with
                | Ok c -> c
                | Error _ -> 0

            return
                { PipelineId = pipelineId
                  Executions = executions
                  TotalCount = totalCount
                  Page = page
                  PageSize = pageSize }
        }

    let getExecutionDetail
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (executionId: string)
        (ct: CancellationToken)
        : Task<ExecutionDetail option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! result = ExecutionLogRepository.getByExecutionId db executionId ct

            match result with
            | Error _ -> return None
            | Ok [] -> return None
            | Ok logs ->
                let steps =
                    logs
                    |> List.map (fun l ->
                        { StepTypeKey = l.StepTypeKey
                          Outcome = mapOutcome l.Outcome
                          Message = l.Message
                          StartTime = l.StartTime
                          EndTime = l.EndTime
                          Duration = l.EndTime - l.StartTime
                          ContextSnapshot = l.ContextSnapshot })

                let totalStart = steps |> List.map _.StartTime |> List.min
                let totalEnd = steps |> List.map _.EndTime |> List.max

                let overallOutcome =
                    if steps |> List.exists (fun s -> s.Outcome = StepOutcome.Failed) then
                        StepOutcome.Failed
                    elif steps |> List.exists (fun s -> s.Outcome = StepOutcome.Stopped) then
                        StepOutcome.Stopped
                    else
                        StepOutcome.Success

                return
                    Some
                        { PipelineId = pipelineId
                          ExecutionId = executionId
                          Steps = steps
                          TotalDuration = totalEnd - totalStart
                          OverallOutcome = overallOutcome }
        }

module View =
    let private outcomeBadge (outcome: StepOutcome) =
        let cls, text =
            match outcome with
            | StepOutcome.Success -> "bg-emerald-50 text-emerald-700", "Success"
            | StepOutcome.Stopped -> "bg-amber-50 text-amber-700", "Stopped"
            | StepOutcome.Failed -> "bg-red-50 text-red-700", "Failed"

        _span [ _class_ $"px-2 py-0.5 text-xs font-medium rounded {cls}" ] [ Text.raw text ]

    let private formatDuration (d: TimeSpan) =
        if d.TotalSeconds < 1.0 then $"{int d.TotalMilliseconds}ms"
        elif d.TotalMinutes < 1.0 then $"{d.TotalSeconds:F1}s"
        else $"{int d.TotalMinutes}m {d.Seconds}s"

    let private closeModalButton =
        _button [
            _type_ "button"
            _class_ "text-gray-400 hover:text-gray-600 transition-colors"
            Hx.get "/pipelines/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div [
            _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
            Hx.get "/pipelines/modal/close"
            Hx.targetCss "#modal-container"
            Hx.swapInnerHtml
        ] []

    let private contextDiff (prev: string option) (current: string) =
        try
            let currentDoc = JsonDocument.Parse(current)
            let currentProps = currentDoc.RootElement.EnumerateObject() |> Seq.toList

            match prev with
            | None ->
                _pre [ _class_ "text-xs bg-gray-50 border border-gray-100 p-3 rounded-md overflow-x-auto max-h-48 text-gray-600 font-mono" ] [
                    Text.raw (JsonSerializer.Serialize(currentDoc.RootElement, JsonSerializerOptions(WriteIndented = true)))
                ]
            | Some prevJson ->
                let prevDoc = JsonDocument.Parse(prevJson)
                let prevMap =
                    prevDoc.RootElement.EnumerateObject()
                    |> Seq.map (fun p -> p.Name, p.Value.GetRawText())
                    |> Map.ofSeq

                let currentMap =
                    currentProps |> List.map (fun p -> p.Name, p.Value.GetRawText()) |> Map.ofList

                let allKeys =
                    Set.union (prevMap |> Map.keys |> Set.ofSeq) (currentMap |> Map.keys |> Set.ofSeq)
                    |> Set.toList
                    |> List.sort

                let lines =
                    allKeys
                    |> List.choose (fun key ->
                        match Map.tryFind key prevMap, Map.tryFind key currentMap with
                        | Some old, Some curr when old = curr -> None
                        | Some old, Some curr ->
                            Some [
                                _div [ _class_ "text-red-600" ] [ Text.raw $"- {key}: {old}" ]
                                _div [ _class_ "text-green-600" ] [ Text.raw $"+ {key}: {curr}" ]
                            ]
                        | None, Some curr ->
                            Some [ _div [ _class_ "text-green-600" ] [ Text.raw $"+ {key}: {curr}" ] ]
                        | Some old, None ->
                            Some [ _div [ _class_ "text-red-600" ] [ Text.raw $"- {key}: {old}" ] ]
                        | None, None -> None)
                    |> List.concat

                if lines.IsEmpty then
                    _div [ _class_ "text-xs text-gray-400 italic" ] [ Text.raw "No context changes" ]
                else
                    _pre [ _class_ "text-xs bg-gray-50 border border-gray-100 p-3 rounded-md overflow-x-auto max-h-48 font-mono" ] lines
        with _ ->
            _pre [ _class_ "text-xs bg-gray-50 border border-gray-100 p-3 rounded-md overflow-x-auto max-h-48 text-gray-600 font-mono" ] [
                Text.raw current
            ]

    let executionList (data: ExecutionListData) =
        let totalPages = max 1 (int (Math.Ceiling(float data.TotalCount / float data.PageSize)))
        let hasPrev = data.Page > 1
        let hasNext = data.Page < totalPages

        _div [ _id_ "traces-list" ] [
            if data.Executions.IsEmpty then
                _div [ _class_ "py-12 text-center text-gray-400" ] [
                    _i [ _class_ "fas fa-search text-3xl mb-3" ] []
                    _p [ _class_ "text-sm font-medium" ] [ Text.raw "No executions yet" ]
                    _p [ _class_ "text-xs" ] [ Text.raw "This pipeline hasn't been executed" ]
                ]
            else
                _div [ _class_ "space-y-1" ] [
                    for exec in data.Executions do
                        _div [
                            _class_
                                "px-4 py-3 border border-gray-200 rounded-md bg-white hover:bg-gray-50 cursor-pointer flex items-center justify-between transition-colors"
                            Hx.get $"/pipelines/{data.PipelineId}/traces/{exec.ExecutionId}"
                            Hx.targetCss "#traces-content"
                            Hx.swapInnerHtml
                        ] [
                            _div [ _class_ "flex items-center gap-3" ] [
                                _span [ _class_ "font-mono text-sm font-medium text-gray-900" ] [
                                    Text.raw (exec.ExecutionId.Substring(0, min 8 exec.ExecutionId.Length))
                                ]
                                _span [ _class_ "text-xs text-gray-400" ] [
                                    Text.raw (exec.StartTime.ToString("MMM dd, HH:mm:ss"))
                                ]
                            ]
                            _div [ _class_ "flex items-center gap-3" ] [
                                _span [ _class_ "text-xs text-gray-400" ] [
                                    _i [ _class_ "fas fa-layer-group mr-1" ] []
                                    Text.raw $"{exec.StepCount} steps"
                                ]
                                _span [ _class_ "text-xs text-gray-400 font-mono" ] [
                                    _i [ _class_ "fas fa-clock mr-1" ] []
                                    Text.raw (formatDuration exec.Duration)
                                ]
                                outcomeBadge exec.Outcome
                                _i [ _class_ "fas fa-chevron-right text-xs text-gray-300" ] []
                            ]
                        ]
                ]

                let enabledBtnClass = "bg-white text-gray-600 border border-gray-200 hover:bg-gray-50"
                let disabledBtnClass = "bg-gray-50 text-gray-300 cursor-not-allowed"

                _div [ _class_ "flex items-center justify-between px-4 py-3 border-t border-gray-100" ] [
                    _div [ _class_ "text-xs text-gray-400" ] [
                        Text.raw $"{data.TotalCount} execution(s)"
                    ]
                    _div [ _class_ "flex gap-2" ] [
                        _button [
                            _type_ "button"
                            _class_
                                $"px-3 py-1 text-xs font-medium rounded-md {if hasPrev then enabledBtnClass else disabledBtnClass}"
                            if hasPrev then
                                Hx.get $"/pipelines/{data.PipelineId}/traces/list?page={data.Page - 1}"
                                Hx.targetCss "#traces-list"
                                Hx.swapOuterHtml
                            else
                                Attr.create "disabled" "disabled"
                        ] [ Text.raw "Previous" ]
                        _span [ _class_ "px-3 py-1 text-xs text-gray-400" ] [
                            Text.raw $"Page {data.Page} of {totalPages}"
                        ]
                        _button [
                            _type_ "button"
                            _class_
                                $"px-3 py-1 text-xs font-medium rounded-md {if hasNext then enabledBtnClass else disabledBtnClass}"
                            if hasNext then
                                Hx.get $"/pipelines/{data.PipelineId}/traces/list?page={data.Page + 1}"
                                Hx.targetCss "#traces-list"
                                Hx.swapOuterHtml
                            else
                                Attr.create "disabled" "disabled"
                        ] [ Text.raw "Next" ]
                    ]
                ]
        ]

    let executionDetail (detail: ExecutionDetail) =
        let totalMs = detail.TotalDuration.TotalMilliseconds
        let executionStart = if detail.Steps.IsEmpty then DateTime.MinValue else (detail.Steps |> List.map _.StartTime |> List.min)

        _div [] [
            _div [ _class_ "flex items-center gap-3 pb-4 mb-4 border-b border-gray-100" ] [
                _button [
                    _type_ "button"
                    _class_ "p-1.5 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded transition-colors"
                    Hx.get $"/pipelines/{detail.PipelineId}/traces/modal"
                    Hx.targetCss "#modal-container"
                    Hx.swapInnerHtml
                ] [ _i [ _class_ "fas fa-arrow-left" ] [] ]
                _div [ _class_ "flex items-center gap-2" ] [
                    _span [ _class_ "font-mono text-sm font-medium text-gray-900" ] [ Text.raw detail.ExecutionId ]
                    outcomeBadge detail.OverallOutcome
                ]
                _span [ _class_ "text-xs text-gray-400 ml-auto" ] [
                    _i [ _class_ "fas fa-clock mr-1" ] []
                    Text.raw (formatDuration detail.TotalDuration)
                ]
            ]

            _div [ _class_ "space-y-2" ] [
                for i, step in detail.Steps |> List.indexed do
                    let barColor =
                        match step.Outcome with
                        | StepOutcome.Success -> "bg-emerald-400"
                        | StepOutcome.Stopped -> "bg-amber-400"
                        | StepOutcome.Failed -> "bg-red-400"

                    let offsetPct =
                        if totalMs > 0.0 then
                            (step.StartTime - executionStart).TotalMilliseconds / totalMs * 100.0
                        else
                            0.0

                    let widthPct =
                        if totalMs > 0.0 then
                            max 0.5 (step.Duration.TotalMilliseconds / totalMs * 100.0)
                        else
                            100.0

                    let prevSnapshot =
                        if i > 0 then Some detail.Steps.[i - 1].ContextSnapshot else None

                    let stepId = $"step-detail-{i}"
                    let chevronId = $"step-chevron-{i}"

                    _div [ _class_ "border border-gray-200 rounded-md bg-white transition-colors" ] [
                        _div [
                            _class_ "flex items-center gap-3 px-4 py-3 cursor-pointer hover:bg-gray-50 transition-colors"
                            Attr.create
                                "onclick"
                                $"document.getElementById('{stepId}').classList.toggle('hidden');document.getElementById('{chevronId}').classList.toggle('rotate-90')"
                        ] [
                            _i [
                                _id_ chevronId
                                _class_ "fas fa-chevron-right text-xs text-gray-300 transition-transform"
                            ] []
                            _span [ _class_ "w-44 truncate text-sm font-medium text-gray-900" ] [
                                Text.raw step.StepTypeKey
                            ]
                            _div [ _class_ "flex-1 h-4 bg-gray-100 rounded-full relative overflow-hidden" ] [
                                _div [
                                    _class_ $"absolute top-0 h-full rounded-full {barColor}"
                                    Attr.create "style" $"left:{offsetPct:F1}%%;width:{widthPct:F1}%%"
                                ] []
                            ]
                            _span [ _class_ "w-16 text-xs text-gray-400 text-right font-mono" ] [
                                Text.raw (formatDuration step.Duration)
                            ]
                            outcomeBadge step.Outcome
                        ]
                        _div [ _id_ stepId; _class_ "hidden border-t border-gray-100" ] [
                            _div [ _class_ "px-4 py-3 space-y-3" ] [
                                if not (String.IsNullOrWhiteSpace step.Message) then
                                    _div [] [
                                        _div [ _class_ "text-xs font-medium text-gray-400 uppercase tracking-wide mb-1" ] [
                                            Text.raw "Message"
                                        ]
                                        _p [ _class_ "text-sm text-gray-700" ] [ Text.raw step.Message ]
                                    ]
                                _div [] [
                                    _div [ _class_ "text-xs font-medium text-gray-400 uppercase tracking-wide mb-1" ] [
                                        Text.raw "Context"
                                    ]
                                    contextDiff prevSnapshot step.ContextSnapshot
                                ]
                            ]
                        ]
                    ]
            ]
        ]

    let modal (data: ExecutionListData) =
        _div [
            _id_ "pipeline-traces-modal"
            _class_ "fixed inset-0 z-50 overflow-y-auto"
            Attr.create "role" "dialog"
            Attr.create "aria-modal" "true"
        ] [
            modalBackdrop
            _div [ _class_ "fixed inset-0 z-10 overflow-y-auto" ] [
                _div [ _class_ "flex min-h-full items-center justify-center p-4" ] [
                    _div [
                        _class_
                            "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-5xl"
                    ] [
                        _div [ _class_ "border-b border-gray-100 px-6 py-4" ] [
                            _div [ _class_ "flex items-center justify-between" ] [
                                _h3 [ _class_ "text-lg font-semibold text-gray-900" ] [
                                    _i [ _class_ "fas fa-timeline mr-2 text-gray-400" ] []
                                    Text.raw "Execution Traces"
                                ]
                                closeModalButton
                            ]
                        ]
                        _div [ _id_ "traces-content"; _class_ "px-6 py-4 max-h-[75vh] overflow-y-auto" ] [
                            executionList data
                        ]
                    ]
                ]
            ]
        ]

module Handler =
    let private tryGetQueryInt (query: IQueryCollection) (key: string) (defaultValue: int) =
        match query.TryGetValue key with
        | true, values when values.Count > 0 ->
            match Int32.TryParse values.[0] with
            | true, v -> v
            | false, _ -> defaultValue
        | _ -> defaultValue

    let tracesModal (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! data = Data.getExecutions scopeFactory pipelineId 1 20 ctx.RequestAborted
                    return! Response.ofHtml (View.modal data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineTraces")
                    logger.LogError(ex, "Error loading traces for pipeline {PipelineId}", pipelineId)

                    let empty =
                        { PipelineId = pipelineId
                          Executions = []
                          TotalCount = 0
                          Page = 1
                          PageSize = 20 }

                    return! Response.ofHtml (View.modal empty) ctx
            }

    let tracesList (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let page = tryGetQueryInt ctx.Request.Query "page" 1
                    let! data = Data.getExecutions scopeFactory pipelineId page 20 ctx.RequestAborted
                    return! Response.ofHtml (View.executionList data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineTraces")
                    logger.LogError(ex, "Error loading traces list for pipeline {PipelineId}", pipelineId)

                    let empty =
                        { PipelineId = pipelineId
                          Executions = []
                          TotalCount = 0
                          Page = 1
                          PageSize = 20 }

                    return! Response.ofHtml (View.executionList empty) ctx
            }

    let executionDetail (pipelineId: int) (executionId: string) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! detail = Data.getExecutionDetail scopeFactory pipelineId executionId ctx.RequestAborted

                    match detail with
                    | Some d -> return! Response.ofHtml (View.executionDetail d) ctx
                    | None ->
                        return!
                            Response.ofHtml
                                (_div [ _class_ "py-8 text-center text-gray-400" ] [
                                    _p [ _class_ "text-sm" ] [ Text.raw "Execution not found" ]
                                ])
                                ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineTraces")
                    logger.LogError(ex, "Error loading execution detail {ExecutionId}", executionId)

                    return!
                        Response.ofHtml
                            (_div [ _class_ "py-8 text-center text-red-400" ] [
                                _p [ _class_ "text-sm" ] [ Text.raw "Error loading execution details" ]
                            ])
                            ctx
            }
