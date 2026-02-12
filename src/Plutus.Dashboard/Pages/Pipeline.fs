namespace Plutus.App.Pages.Pipeline

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Repositories
open Plutus.Core.Shared

type PipelineListItem =
    { Id: int
      Symbol: string
      MarketType: MarketType
      Enabled: bool
      Tags: string list
      UpdatedAt: DateTime }

type PipelinesGridData =
    { Tags: string list
      MarketTypes: string list
      Pipelines: PipelineListItem list }

    static member Empty = { Tags = []; MarketTypes = []; Pipelines = [] }

type PipelineFilters =
    { SearchTerm: string option
      Tag: string option
      MarketType: string option
      Status: string option
      SortBy: string
      Page: int
      PageSize: int }

    static member Empty =
        { SearchTerm = None
          Tag = None
          MarketType = None
          Status = None
          SortBy = "symbol"
          Page = 1
          PageSize = 20 }

type PipelinesTableData =
    { Pipelines: PipelineListItem list
      TotalCount: int
      Page: int
      PageSize: int }

    static member Empty = { Pipelines = []; TotalCount = 0; Page = 1; PageSize = 20 }

module Data =
    let getTags (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<string list> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! tags = PipelineRepository.getAllTags db ct

            match tags with
            | Ok tags -> return tags
            | Error err ->
                let logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Pipelines")
                logger.LogError("Error getting pipeline tags: {Error}", Errors.serviceMessage err)
                return []
        }

    let getMarketTypes (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<string list> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! markets = MarketRepository.getAll db ct

            match markets with
            | Error err ->
                let logger =
                    scopeFactory
                        .CreateScope()
                        .ServiceProvider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Pipelines")

                logger.LogError("Error getting markets: {Error}", Errors.serviceMessage err)
                return []
            | Ok markets -> return markets |> List.map _.Type.ToString()
        }

    let getCount (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<int> =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! count = PipelineRepository.count db ct

            match count with
            | Ok count -> return count
            | Error err ->
                let logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Pipelines")
                logger.LogError("Error getting pipelines count: {Error}", Errors.serviceMessage err)
                return 0
        }

    let getGridData (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<PipelinesGridData> =
        task {
            let! tags = getTags scopeFactory ct
            let! marketTypes = getMarketTypes scopeFactory ct
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! pipelines = PipelineRepository.getAll db ct

            match pipelines with
            | Error _ -> return PipelinesGridData.Empty
            | Ok pipelines ->
                let pipelineItems =
                    pipelines
                    |> List.map (fun p ->
                        { Id = p.Id
                          Symbol = p.Symbol
                          MarketType = p.MarketType
                          Enabled = p.Enabled
                          Tags = p.Tags
                          UpdatedAt = p.UpdatedAt }
                    )

                return { Tags = tags; MarketTypes = marketTypes; Pipelines = pipelineItems }
        }

    let getFilteredPipelines
        (scopeFactory: IServiceScopeFactory)
        (filters: PipelineFilters)
        (ct: CancellationToken)
        : Task<PipelinesTableData>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let status =
                match filters.Status with
                | Some "enabled" -> Some PipelineStatus.Running
                | Some "disabled" -> Some PipelineStatus.Paused
                | _ -> None

            let searchFilters: PipelineSearchFilters =
                { SearchTerm = filters.SearchTerm
                  Tag = filters.Tag
                  MarketType = filters.MarketType
                  Status = status
                  SortBy = filters.SortBy }

            let skip = (filters.Page - 1) * filters.PageSize
            let! result = PipelineRepository.search db searchFilters skip filters.PageSize ct

            match result with
            | Error _ -> return PipelinesTableData.Empty
            | Ok result ->
                let pipelineItems =
                    result.Pipelines
                    |> List.map (fun p ->
                        { Id = p.Id
                          Symbol = p.Symbol
                          MarketType = p.MarketType
                          Enabled = p.Enabled
                          Tags = p.Tags
                          UpdatedAt = p.UpdatedAt }
                    )

                return
                    { Pipelines = pipelineItems
                      TotalCount = result.TotalCount
                      Page = filters.Page
                      PageSize = filters.PageSize }
        }

module View =
    let private filterSelect name label options =
        _div
            [ _class_ "min-w-[150px]" ]
            [ _label [ _class_ "block text-xs font-medium text-slate-500 mb-1" ] [ Text.raw label ]
              _select
                  [ _name_ name
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                  [ yield _option [ _value_ "" ] [ Text.raw $"All {label}" ]
                    for opt in options do
                        yield _option [ _value_ opt ] [ Text.raw opt ] ] ]

    let private filterSelectLabeled name label options =
        _div
            [ _class_ "min-w-[150px]" ]
            [ _label [ _class_ "block text-xs font-medium text-slate-500 mb-1" ] [ Text.raw label ]
              _select
                  [ _name_ name
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                  [ yield _option [ _value_ "" ] [ Text.raw $"All {label}" ]
                    for (value, text) in options do
                        yield _option [ _value_ value ] [ Text.raw text ] ] ]

    let private sectionHeader =
        _div
            [ _class_ "flex justify-between items-center mb-6" ]
            [ _div
                  []
                  [ _h1 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "Trading Pipelines" ]
                    _p [ _class_ "text-slate-400 text-sm" ] [ Text.raw "Manage your automated trading pipelines" ] ]
              _button
                  [ _type_ "button"
                    Hx.get "/pipelines/modal"
                    Hx.targetCss "#modal-container"
                    Hx.swapInnerHtml
                    _class_
                        "inline-flex items-center px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 font-medium text-sm rounded-md transition-colors" ]
                  [ _i [ _class_ "fas fa-plus mr-2 text-slate-400" ] []; Text.raw "Add Pipeline" ] ]

    let private filterBar (data: PipelinesGridData) =
        _div
            [ _id_ "pipelines-filter-form"; _class_ "card mb-6" ]
            [ _form
                  [ Hx.get "/pipelines/table"
                    Hx.targetCss "#pipelines-table-container"
                    Hx.trigger "change, keyup delay:300ms from:input"
                    Hx.swapOuterHtml
                    Hx.includeThis ]
                  [ _input [ _type_ "hidden"; _name_ "page"; _value_ "1" ]
                    _div
                        [ _class_ "flex flex-wrap gap-4" ]
                        [ _div
                              [ _class_ "flex-1 min-w-[200px]" ]
                              [ _label
                                    [ _class_ "block text-xs font-medium text-slate-500 mb-1" ]
                                    [ Text.raw "Search Symbol" ]
                                _input
                                    [ _type_ "text"
                                      _name_ "searchTerm"
                                      Attr.create "placeholder" "Search by symbol..."
                                      _class_
                                          "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ] ]
                          filterSelect "filterTag" "Tag" data.Tags
                          filterSelect "filterAccount" "Account" data.MarketTypes
                          filterSelectLabeled
                              "filterStatus"
                              "Status"
                              [ ("enabled", "Enabled"); ("disabled", "Disabled") ]
                          filterSelectLabeled
                              "sortBy"
                              "Sort By"
                              [ ("symbol", "Symbol A-Z")
                                ("symbol-desc", "Symbol Z-A")
                                ("account", "Account A-Z")
                                ("account-desc", "Account Z-A")
                                ("status", "Status Asc")
                                ("status-desc", "Status Desc")
                                ("updated", "Oldest First")
                                ("updated-desc", "Newest First") ] ] ] ]

    let private tableHeader =
        _thead
            []
            [ _tr
                  []
                  [ for (text, align) in
                        [ "Symbol", "left"
                          "Account", "left"
                          "Status", "left"
                          "Tags", "left"
                          "Last Updated", "left"
                          "Actions", "right" ] do
                        _th
                            [ _class_
                                  $"px-4 py-3 text-{align} text-xs font-medium text-slate-400 uppercase tracking-wider" ]
                            [ Text.raw text ] ] ]

    let pipelineRow (pipeline: PipelineListItem) =
        let statusClass, statusText =
            if pipeline.Enabled then
                "bg-green-50 text-green-700", "Enabled"
            else
                "bg-slate-50 text-slate-500", "Disabled"

        _tr
            [ _id_ $"pipeline-{pipeline.Id}"; _class_ "hover:bg-slate-50" ]
            [ _td
                  [ _class_ "px-4 py-3 whitespace-nowrap" ]
                  [ _span [ _class_ "font-medium text-slate-900 text-sm" ] [ Text.raw pipeline.Symbol ] ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw (pipeline.MarketType.ToString()) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap" ]
                  [ _span [ _class_ $"px-2 py-0.5 text-xs font-medium rounded {statusClass}" ] [ Text.raw statusText ] ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap" ]
                  [ _div
                        [ _class_ "flex gap-1" ]
                        [ for tag in pipeline.Tags |> List.truncate 3 do
                              _span
                                  [ _class_ "px-2 py-0.5 text-xs bg-slate-100 text-slate-600 rounded" ]
                                  [ Text.raw tag ] ] ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw (pipeline.UpdatedAt.ToString("MMM dd, HH:mm")) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-right text-sm" ]
                  [ _button
                        [ _class_ "text-slate-400 hover:text-slate-600 mr-3"
                          Hx.get $"/pipelines/{pipeline.Id}/details/modal"
                          Hx.targetCss "#modal-container"
                          Hx.swapInnerHtml ]
                        [ Text.raw "View" ]

                    _button
                        [ _class_ "text-slate-400 hover:text-slate-600 mr-3"
                          Hx.get $"/pipelines/{pipeline.Id}/edit/modal"
                          Hx.targetCss "#modal-container"
                          Hx.swapInnerHtml ]
                        [ Text.raw "Edit" ]

                    _button
                        [ _class_ "text-slate-400 hover:text-slate-600 mr-3"
                          Hx.get $"/pipelines/{pipeline.Id}/traces/modal"
                          Hx.targetCss "#modal-container"
                          Hx.swapInnerHtml ]
                        [ Text.raw "Traces" ]

                    _button
                        [ _class_ "text-slate-400 hover:text-red-500"
                          Hx.delete $"/pipelines/{pipeline.Id}"
                          Hx.confirm "Are you sure you want to delete this pipeline?"
                          Hx.targetCss $"#pipeline-{pipeline.Id}"
                          Hx.swapOuterHtml ]
                        [ Text.raw "Delete" ] ] ]

    let emptyState =
        _tr
            []
            [ _td
                  [ Attr.create "colspan" "6"; _class_ "px-4 py-12 text-center" ]
                  [ _div
                        [ _class_ "text-slate-400" ]
                        [ _i [ _class_ "fas fa-robot text-3xl mb-3" ] []
                          _p [ _class_ "text-sm font-medium" ] [ Text.raw "No pipelines yet" ]
                          _p [ _class_ "text-xs" ] [ Text.raw "Create your first trading pipeline to get started" ] ] ] ]

    let loadingState =
        _tr
            []
            [ _td
                  [ Attr.create "colspan" "6"; _class_ "px-4 py-8 text-center text-slate-400" ]
                  [ _i [ _class_ "fas fa-spinner fa-spin text-lg mb-2" ] []
                    _p [ _class_ "text-sm" ] [ Text.raw "Loading pipelines..." ] ] ]

    let private paginationControls (data: PipelinesTableData) =
        let totalPages = int (Math.Ceiling(float data.TotalCount / float data.PageSize))
        let hasPrev = data.Page > 1
        let hasNext = data.Page < totalPages
        let startRecord = if data.TotalCount = 0 then 0 else (data.Page - 1) * data.PageSize + 1
        let endRecord = min (data.Page * data.PageSize) data.TotalCount

        let enabledBtnClass = "bg-white text-slate-600 border border-slate-200 hover:bg-slate-50"
        let disabledBtnClass = "bg-slate-50 text-slate-300 cursor-not-allowed"
        let prevBtnClass = if hasPrev then enabledBtnClass else disabledBtnClass
        let nextBtnClass = if hasNext then enabledBtnClass else disabledBtnClass

        _div
            [ _class_ "flex items-center justify-between px-4 py-3 border-t border-slate-100" ]
            [ _div
                  [ _class_ "text-xs text-slate-400" ]
                  [ Text.raw $"Showing {startRecord} to {endRecord} of {data.TotalCount} pipelines" ]
              _div
                  [ _class_ "flex gap-2" ]
                  [ _button
                        [ _type_ "button"
                          _class_ $"px-3 py-1 text-xs font-medium rounded-md {prevBtnClass}"
                          if hasPrev then
                              Hx.get $"/pipelines/table?page={data.Page - 1}"
                              Hx.targetCss "#pipelines-table-container"
                              Hx.swapOuterHtml
                              Attr.create "hx-include" "#pipelines-filter-form form"
                          else
                              Attr.create "disabled" "disabled" ]
                        [ Text.raw "Previous" ]
                    _span
                        [ _class_ "px-3 py-1 text-xs text-slate-400" ]
                        [ Text.raw $"Page {data.Page} of {totalPages}" ]
                    _button
                        [ _type_ "button"
                          _class_ $"px-3 py-1 text-xs font-medium rounded-md {nextBtnClass}"
                          if hasNext then
                              Hx.get $"/pipelines/table?page={data.Page + 1}"
                              Hx.targetCss "#pipelines-table-container"
                              Hx.swapOuterHtml
                              Attr.create "hx-include" "#pipelines-filter-form form"
                          else
                              Attr.create "disabled" "disabled" ]
                        [ Text.raw "Next" ] ] ]

    let tableBody (data: PipelinesTableData) =
        let rows =
            match data.Pipelines with
            | [] -> [ emptyState ]
            | pipelines -> pipelines |> List.map pipelineRow

        _div
            [ _id_ "pipelines-table-container" ]
            [ _div
                  [ _class_ "overflow-x-auto" ]
                  [ _table
                        [ _class_ "min-w-full divide-y divide-slate-100" ]
                        [ tableHeader; _tbody [ _class_ "bg-white divide-y divide-slate-100" ] rows ] ]
              paginationControls data ]

    let private pipelinesTable =
        _div
            [ _class_ "card overflow-hidden" ]
            [ _div
                  [ _id_ "pipelines-table-container"; Hx.get "/pipelines/table"; Hx.trigger "load"; Hx.swapOuterHtml ]
                  [ _div
                        [ _class_ "overflow-x-auto" ]
                        [ _table
                              [ _class_ "min-w-full divide-y divide-slate-100" ]
                              [ tableHeader; _tbody [ _class_ "bg-white divide-y divide-slate-100" ] [ loadingState ] ] ] ] ]

    let section (data: PipelinesGridData) = _section [] [ sectionHeader; filterBar data; pipelinesTable ]

    let count (n: int) = Text.raw (string n)

module Handler =
    let count: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! count = Data.getCount scopeFactory ctx.RequestAborted
                    return! Response.ofHtml (View.count count) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Pipelines")
                    logger.LogError(ex, "Error getting pipelines count")
                    return! Response.ofHtml (View.count 0) ctx
            }

    let grid: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! data = Data.getGridData scopeFactory ctx.RequestAborted
                    return! Response.ofHtml (View.section data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Pipelines")
                    logger.LogError(ex, "Error getting pipelines grid")
                    return! Response.ofHtml (View.section PipelinesGridData.Empty) ctx
            }

    let tryGetQueryStringValue (query: IQueryCollection) (key: string) : string option =
        match query.TryGetValue key with
        | true, values when values.Count > 0 && not (String.IsNullOrEmpty values.[0]) -> Some values.[0]
        | _ -> None

    let tryGetQueryStringInt (query: IQueryCollection) (key: string) (defaultValue: int) : int =
        match query.TryGetValue key with
        | true, values when values.Count > 0 ->
            match Int32.TryParse values.[0] with
            | true, v -> v
            | false, _ -> defaultValue
        | _ -> defaultValue

    let table: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()

                    let filters: PipelineFilters =
                        { SearchTerm = tryGetQueryStringValue ctx.Request.Query "searchTerm"
                          Tag = tryGetQueryStringValue ctx.Request.Query "filterTag"
                          MarketType = tryGetQueryStringValue ctx.Request.Query "filterAccount"
                          Status = tryGetQueryStringValue ctx.Request.Query "filterStatus"
                          SortBy = tryGetQueryStringValue ctx.Request.Query "sortBy" |> Option.defaultValue "symbol"
                          Page = tryGetQueryStringInt ctx.Request.Query "page" 1
                          PageSize = tryGetQueryStringInt ctx.Request.Query "pageSize" 20 }

                    let! data = Data.getFilteredPipelines scopeFactory filters ctx.RequestAborted
                    return! Response.ofHtml (View.tableBody data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Pipelines")
                    logger.LogError(ex, "Error getting filtered pipelines")
                    return! Response.ofHtml (View.tableBody PipelinesTableData.Empty) ctx
            }

    let delete (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    use scope = ctx.Plug<IServiceScopeFactory>().CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! _ = PipelineRepository.delete db pipelineId ctx.RequestAborted
                    return! Response.ofEmpty ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Pipelines")
                    logger.LogError(ex, "Error deleting pipeline {PipelineId}", pipelineId)
                    return! Response.ofEmpty ctx
            }
