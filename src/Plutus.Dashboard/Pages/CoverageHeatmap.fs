namespace Plutus.App.Pages.CoverageHeatmap

open System
open System.Data
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure
open Plutus.Core.Queries
open Plutus.Core.Repositories

type HeatmapCell = { WeekStart: DateTime; Coverage: float }

type InstrumentRow = { Instrument: string; Cells: HeatmapCell list }

type HeatmapData =
    { Instruments: InstrumentRow list
      Weeks: DateTime list
      Timeframe: string
      AvailableTimeframes: string list
      Page: int
      PageSize: int
      TotalInstruments: int }

module Shared =
    let expectedCandlesPerWeek (timeframe: string) =
        let minutesPerWeek = 7.0 * 24.0 * 60.0

        let intervalMinutes =
            match timeframe with
            | "1m" -> 1.0
            | "3m" -> 3.0
            | "5m" -> 5.0
            | "15m" -> 15.0
            | "30m" -> 30.0
            | "1H" -> 60.0
            | "2H" -> 120.0
            | "4H" -> 240.0
            | "6H" -> 360.0
            | "12H" -> 720.0
            | "1D" -> 1440.0
            | "1W" -> 10080.0
            | _ -> 60.0

        minutesPerWeek / intervalMinutes

module View =
    let private coverageColor (coverage: float) =
        if coverage <= 0.0 then "bg-slate-100"
        elif coverage <= 0.25 then "bg-red-300"
        elif coverage <= 0.50 then "bg-orange-300"
        elif coverage <= 0.75 then "bg-yellow-300"
        elif coverage < 1.0 then "bg-green-300"
        else "bg-green-500"

    let private timeframeDropdown (selected: string) (available: string list) =
        _select
            [ _name_ "timeframe"
              _class_
                  "px-3 py-1.5 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
              Hx.get "/coverage-heatmap"
              Hx.targetCss "#coverage-heatmap-container"
              Hx.swapInnerHtml
              Hx.includeThis ]
            [ for tf in available do
                  if tf = selected then
                      _option [ _value_ tf; _selected_ ] [ Text.raw tf ]
                  else
                      _option [ _value_ tf ] [ Text.raw tf ] ]

    let private legendBar =
        _div
            [ _class_ "flex items-center gap-1.5 text-xs text-slate-500" ]
            [ Text.raw "Less"
              _div [ _class_ "w-3 h-3 rounded-sm bg-slate-100" ] []
              _div [ _class_ "w-3 h-3 rounded-sm bg-red-300" ] []
              _div [ _class_ "w-3 h-3 rounded-sm bg-orange-300" ] []
              _div [ _class_ "w-3 h-3 rounded-sm bg-yellow-300" ] []
              _div [ _class_ "w-3 h-3 rounded-sm bg-green-300" ] []
              _div [ _class_ "w-3 h-3 rounded-sm bg-green-500" ] []
              Text.raw "More" ]

    let private monthLabels (weeks: DateTime list) =
        let labels =
            weeks
            |> List.mapi (fun i w ->
                let showLabel = i = 0 || w.Month <> (weeks.[i - 1]).Month

                (i, if showLabel then w.ToString("MMM") else "")
            )

        _div
            [ _class_ "flex ml-[120px]" ]
            [ for (_, label) in labels do
                  _div
                      [ _class_ "text-[10px] text-slate-400 text-center"; _style_ "width:14px;min-width:14px" ]
                      [ Text.raw label ] ]

    let private instrumentRow (weeks: DateTime list) (row: InstrumentRow) =
        let cellMap = row.Cells |> List.map (fun c -> c.WeekStart, c.Coverage) |> Map.ofList

        let encodedInstrument = Uri.EscapeDataString(row.Instrument)

        _div
            [ _class_ "flex items-center" ]
            [ _div
                  [ _class_ "w-[120px] min-w-[120px] flex items-center justify-end pr-3 gap-1" ]
                  [ _button
                        [ _class_ "text-slate-300 hover:text-red-500 text-xs leading-none"
                          _title_ $"Delete all data for {row.Instrument}"
                          Hx.delete $"/coverage-heatmap/{encodedInstrument}"
                          Hx.confirm $"Delete all candlestick data for {row.Instrument}?"
                          Hx.targetCss "#data-coverage-content"
                          Hx.swapInnerHtml
                          Hx.includeCss "[name=timeframe]" ]
                        [ Text.raw "\u00D7" ]
                    _span
                        [ _class_ "text-xs text-slate-600 truncate"; _title_ row.Instrument ]
                        [ Text.raw row.Instrument ] ]
              _div
                  [ _class_ "flex gap-px" ]
                  [ for w in weeks do
                        let coverage = cellMap |> Map.tryFind w |> Option.defaultValue 0.0
                        let pct = int (coverage * 100.0)
                        let weekStr = w.ToString("yyyy-MM-dd")
                        let color = coverageColor coverage

                        _div [ _class_ $"w-3 h-3 rounded-sm {color}"; _title_ $"{weekStr}: {pct}%%" ] [] ] ]

    let private paginationControls (data: HeatmapData) =
        let totalPages = int (Math.Ceiling(float data.TotalInstruments / float data.PageSize))

        if totalPages <= 1 then
            _div [] []
        else
            let hasPrev = data.Page > 1
            let hasNext = data.Page < totalPages
            let startRecord = (data.Page - 1) * data.PageSize + 1
            let endRecord = min (data.Page * data.PageSize) data.TotalInstruments
            let enabledBtnClass = "bg-white text-slate-600 border border-slate-200 hover:bg-slate-50"
            let disabledBtnClass = "bg-slate-50 text-slate-300 cursor-not-allowed"

            _div
                [ _class_ "flex items-center justify-between mt-3" ]
                [ _div
                      [ _class_ "text-xs text-slate-400" ]
                      [ Text.raw $"Showing {startRecord} to {endRecord} of {data.TotalInstruments} instruments" ]
                  _div
                      [ _class_ "flex gap-2" ]
                      [ _button
                            [ _type_ "button"
                              _class_
                                  $"px-3 py-1 text-xs font-medium rounded-md {if hasPrev then enabledBtnClass else disabledBtnClass}"
                              if hasPrev then
                                  Hx.get $"/coverage-heatmap?page={data.Page - 1}"
                                  Hx.targetCss "#coverage-heatmap-container"
                                  Hx.swapInnerHtml
                                  Hx.includeCss "[name=timeframe]"
                              else
                                  _disabled_ ]
                            [ Text.raw "Previous" ]
                        _span
                            [ _class_ "px-3 py-1 text-xs text-slate-400" ]
                            [ Text.raw $"Page {data.Page} of {totalPages}" ]
                        _button
                            [ _type_ "button"
                              _class_
                                  $"px-3 py-1 text-xs font-medium rounded-md {if hasNext then enabledBtnClass else disabledBtnClass}"
                              if hasNext then
                                  Hx.get $"/coverage-heatmap?page={data.Page + 1}"
                                  Hx.targetCss "#coverage-heatmap-container"
                                  Hx.swapInnerHtml
                                  Hx.includeCss "[name=timeframe]"
                              else
                                  _disabled_ ]
                            [ Text.raw "Next" ] ] ]

    let heatmap (data: HeatmapData) =
        _div
            []
            [ _div
                  [ _class_ "flex items-center justify-between mb-4" ]
                  [ _div
                        [ _class_ "flex items-center gap-3" ]
                        [ timeframeDropdown data.Timeframe data.AvailableTimeframes ]
                    legendBar ]
              match data.Instruments with
              | [] ->
                  _div
                      [ _class_ "text-sm text-slate-400 py-8 text-center" ]
                      [ Text.raw "No candlestick data available" ]
              | _ ->
                  _div
                      [ _class_ "overflow-x-auto" ]
                      [ monthLabels data.Weeks
                        _div
                            [ _class_ "space-y-px mt-1" ]
                            [ for row in data.Instruments do
                                  instrumentRow data.Weeks row ] ]

                  paginationControls data ]

module Data =
    let private pageSize = 15

    let private buildFromCoverage
        (coverage: WeeklyCoverage list)
        (totalInstruments: int)
        (selectedTimeframe: string)
        (availableTimeframes: string list)
        (page: int)
        =
        let expected = Shared.expectedCandlesPerWeek selectedTimeframe
        let grouped = coverage |> List.groupBy _.Instrument |> List.sortBy fst
        let totalPages = int (Math.Ceiling(float totalInstruments / float pageSize))
        let safePage = max 1 (min page totalPages)
        let offset = (safePage - 1) * pageSize

        let pageInstruments = grouped |> List.skip offset |> List.truncate pageSize

        let allWeeks = pageInstruments |> List.collect snd |> List.map _.WeekStart |> List.distinct |> List.sort

        let instrumentRows =
            pageInstruments
            |> List.map (fun (instrument, rows) ->
                let cells =
                    rows
                    |> List.map (fun r -> { WeekStart = r.WeekStart; Coverage = min 1.0 (float r.Count / expected) })

                { Instrument = instrument.ToString(); Cells = cells }
            )

        { Instruments = instrumentRows
          Weeks = allWeeks
          Timeframe = selectedTimeframe
          AvailableTimeframes = availableTimeframes
          Page = safePage
          PageSize = pageSize
          TotalInstruments = totalInstruments }

    let private selectTimeframe (timeframe: string option) (available: string list) =
        timeframe
        |> Option.bind (fun tf -> if available |> List.contains tf then Some tf else None)
        |> Option.orElseWith (fun () -> available |> List.tryHead)
        |> Option.defaultValue "1H"

    let loadFromCache (store: CacheStore.T) (timeframe: string option) (page: int) =
        match store.Get<CoverageHeatmapCache.CachedHeatmapData>(CoverageHeatmapCache.Key) with
        | Some cached ->
            let selectedTimeframe = selectTimeframe timeframe cached.Timeframes

            match cached.ByTimeframe |> Map.tryFind selectedTimeframe with
            | Some tfData ->
                Some(buildFromCoverage tfData.Coverage tfData.InstrumentCount selectedTimeframe cached.Timeframes page)
            | None ->
                Some
                    { Instruments = []
                      Weeks = []
                      Timeframe = selectedTimeframe
                      AvailableTimeframes = cached.Timeframes
                      Page = 1
                      PageSize = pageSize
                      TotalInstruments = 0 }
        | None -> None

    let loadFromDb
        (scopeFactory: IServiceScopeFactory)
        (timeframe: string option)
        (page: int)
        (ct: Threading.CancellationToken)
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let! timeframesResult = CandlestickRepository.getDistinctTimeframes db ct

            let availableTimeframes =
                match timeframesResult with
                | Ok tfs -> tfs
                | Error _ -> []

            let selectedTimeframe = selectTimeframe timeframe availableTimeframes
            let expected = Shared.expectedCandlesPerWeek selectedTimeframe

            match availableTimeframes with
            | [] ->
                return
                    { Instruments = []
                      Weeks = []
                      Timeframe = selectedTimeframe
                      AvailableTimeframes = []
                      Page = 1
                      PageSize = pageSize
                      TotalInstruments = 0 }
            | _ ->
                let! countResult = CandlestickRepository.getDistinctInstrumentCount db selectedTimeframe ct

                let totalInstruments =
                    match countResult with
                    | Ok c -> c
                    | Error _ -> 0

                let totalPages = int (Math.Ceiling(float totalInstruments / float pageSize))
                let safePage = max 1 (min page totalPages)
                let offset = (safePage - 1) * pageSize

                let! coverageResult =
                    CandlestickRepository.getWeeklyCoveragePaged db selectedTimeframe offset pageSize ct

                let coverage =
                    match coverageResult with
                    | Ok c -> c
                    | Error _ -> []

                let allWeeks = coverage |> List.map _.WeekStart |> List.distinct |> List.sort

                let instrumentRows =
                    coverage
                    |> List.groupBy _.Instrument
                    |> List.map (fun (instrument, rows) ->
                        let cells =
                            rows
                            |> List.map (fun r ->
                                { WeekStart = r.WeekStart; Coverage = min 1.0 (float r.Count / expected) }
                            )

                        { Instrument = instrument.ToString(); Cells = cells }
                    )

                return
                    { Instruments = instrumentRows
                      Weeks = allWeeks
                      Timeframe = selectedTimeframe
                      AvailableTimeframes = availableTimeframes
                      Page = safePage
                      PageSize = pageSize
                      TotalInstruments = totalInstruments }
        }

module Handler =
    let heatmap: HttpHandler =
        fun ctx ->
            task {
                try
                    let store = ctx.Plug<CacheStore.T>()
                    let query = Request.getQuery ctx
                    let timeframe = query.TryGetString "timeframe"
                    let page = query.TryGetInt "page" |> Option.defaultValue 1

                    let! data =
                        match Data.loadFromCache store timeframe page with
                        | Some cached -> task { return cached }
                        | None ->
                            let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                            Data.loadFromDb scopeFactory timeframe page ctx.RequestAborted

                    return! Response.ofHtml (View.heatmap data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("CoverageHeatmap")
                    logger.LogError(ex, "Error loading coverage heatmap")

                    return!
                        Response.ofHtml
                            (_div
                                [ _class_ "text-sm text-red-500 py-4 text-center" ]
                                [ Text.raw "Failed to load coverage data" ])
                            ctx
            }

    let deleteInstrument: HttpHandler =
        fun ctx ->
            task {
                try
                    let route = Request.getRoute ctx
                    let instrument = route.GetString "instrument"
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()

                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! _ = CandlestickRepository.deleteAllByInstrument db instrument ctx.RequestAborted

                    let query = Request.getQuery ctx
                    let timeframe = query.TryGetString "timeframe"
                    let page = query.TryGetInt "page" |> Option.defaultValue 1

                    let! data = Data.loadFromDb scopeFactory timeframe page ctx.RequestAborted
                    return! Response.ofHtml (View.heatmap data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("CoverageHeatmap")
                    logger.LogError(ex, "Error deleting candlestick data")

                    return!
                        Response.ofHtml
                            (_div
                                [ _class_ "text-sm text-red-500 py-4 text-center" ]
                                [ Text.raw "Failed to delete candlestick data" ])
                            ctx
            }
