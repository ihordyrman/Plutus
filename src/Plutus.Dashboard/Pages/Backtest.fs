namespace Plutus.App.Pages.Backtest

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
open Plutus.Core.Backtesting
open Plutus.Core.Domain
open Plutus.Core.Repositories

type BacktestGridItem =
    { RunId: int
      PipelineInstrument: string
      Status: BacktestStatus
      StartDate: DateTime
      EndDate: DateTime
      IntervalMinutes: int
      InitialCapital: decimal
      FinalCapital: Nullable<decimal>
      TotalTrades: int
      WinRate: Nullable<decimal>
      ErrorMessage: string
      CreatedAt: DateTime }

type TradePair =
    { EntryTime: DateTime
      ExitTime: DateTime
      EntryPrice: decimal
      ExitPrice: decimal
      Quantity: decimal
      Pnl: decimal
      ExecutionId: string }

type ResultsViewModel =
    { Run: BacktestRun
      Pipeline: Pipeline
      Metrics: BacktestMetrics.Metrics
      EquityPoints: BacktestEquityPoint list
      TradePairs: TradePair list }

module Data =
    let getGridItems
        (db: IDbConnection)
        (offset: int)
        (limit: int)
        (ct: CancellationToken)
        : Task<Result<BacktestGridItem list * int, string>>
        =
        task {
            match! BacktestRepository.getAllRuns db offset limit ct with
            | Error err -> return Error $"Failed to load runs: {err}"
            | Ok(runs, total) ->
                let! items =
                    task {
                        let mutable result = []

                        for run in runs do
                            let! pipeline = PipelineRepository.getById db run.PipelineId ct

                            let instrument =
                                match pipeline with
                                | Ok p -> string p.Instrument
                                | Error _ -> $"Pipeline #{run.PipelineId}"

                            result <-
                                result
                                @ [ { RunId = run.Id
                                      PipelineInstrument = instrument
                                      Status = run.Status
                                      StartDate = run.StartDate
                                      EndDate = run.EndDate
                                      IntervalMinutes = run.IntervalMinutes
                                      InitialCapital = run.InitialCapital
                                      FinalCapital = run.FinalCapital
                                      TotalTrades = run.TotalTrades
                                      WinRate = run.WinRate
                                      ErrorMessage = run.ErrorMessage
                                      CreatedAt = run.CreatedAt } ]

                        return result
                    }

                return Ok(items, total)
        }

    let getRunWithPipeline
        (db: IDbConnection)
        (runId: int)
        (ct: CancellationToken)
        : Task<Result<BacktestRun * Pipeline, string>>
        =
        task {
            match! BacktestRepository.getRunById db runId ct with
            | Error err -> return Error $"Run not found: {err}"
            | Ok None -> return Error "Run not found"
            | Ok(Some run) ->
                match! PipelineRepository.getById db run.PipelineId ct with
                | Error err -> return Error $"Pipeline not found: {err}"
                | Ok pipeline -> return Ok(run, pipeline)
        }

    let getTradePairs (trades: BacktestTrade list) : TradePair list =
        trades
        |> List.sortBy _.CandleTime
        |> List.chunkBySize 2
        |> List.filter (fun chunk -> chunk.Length = 2)
        |> List.map (fun pair ->
            let buy = pair.[0]
            let sell = pair.[1]

            { EntryTime = buy.CandleTime
              ExitTime = sell.CandleTime
              EntryPrice = buy.Price
              ExitPrice = sell.Price
              Quantity = sell.Quantity
              Pnl = (sell.Price - buy.Price) * sell.Quantity
              ExecutionId = "" }
        )

module View =
    let private selectClass =
        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"

    let private closeModalButton =
        _button
            [ _type_ "button"
              _class_ "text-slate-400 hover:text-slate-600 transition-colors"
              Hx.get "/backtests/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div
            [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
              Hx.get "/backtests/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            []

    let private intervals =
        [ "1", "1 minute"
          "5", "5 minutes"
          "15", "15 minutes"
          "30", "30 minutes"
          "60", "1 hour"
          "240", "4 hours"
          "1440", "1 day" ]

    let configureModal (pipelineId: int) (instrument: string) =
        let thirtyDaysAgo = DateTime.UtcNow.AddDays(-30.0).ToString("yyyy-MM-dd")
        let today = DateTime.UtcNow.ToString("yyyy-MM-dd")

        _div
            [ _id_ "backtest-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
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
                              [ _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _h3
                                                [ _class_ "text-lg font-semibold text-slate-900" ]
                                                [ _i [ _class_ "fas fa-flask mr-2 text-slate-400" ] []
                                                  Text.raw "Backtest" ]
                                            closeModalButton ]
                                      _p
                                          [ _class_ "text-slate-500 text-sm mt-1" ]
                                          [ Text.raw $"Configure backtest for {instrument}" ] ]
                                _form
                                    [ _method_ "post"
                                      Hx.post "/backtests/run"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ _input [ _type_ "hidden"; _name_ "pipelineId"; _value_ (string pipelineId) ]
                                      _div
                                          [ _class_ "px-6 py-4 space-y-4 max-h-[60vh] overflow-y-auto" ]
                                          [ _div
                                                [ _class_ "grid grid-cols-2 gap-3" ]
                                                [ _div
                                                      []
                                                      [ _label
                                                            [ _for_ "startDate"
                                                              _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                                                            [ Text.raw "Start Date "
                                                              _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                                                        _input
                                                            [ _id_ "startDate"
                                                              _name_ "startDate"
                                                              _type_ "date"
                                                              _value_ thirtyDaysAgo
                                                              _class_ selectClass
                                                              _required_ ] ]
                                                  _div
                                                      []
                                                      [ _label
                                                            [ _for_ "endDate"
                                                              _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                                                            [ Text.raw "End Date "
                                                              _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                                                        _input
                                                            [ _id_ "endDate"
                                                              _name_ "endDate"
                                                              _type_ "date"
                                                              _value_ today
                                                              _class_ selectClass
                                                              _required_ ] ] ]
                                            _div
                                                []
                                                [ _label
                                                      [ _for_ "intervalMinutes"
                                                        _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                                                      [ Text.raw "Interval "
                                                        _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                                                  _select
                                                      [ _id_ "intervalMinutes"
                                                        _name_ "intervalMinutes"
                                                        _class_ selectClass ]
                                                      [ for (value, label) in intervals do
                                                            _option [ _value_ value ] [ Text.raw label ] ] ]
                                            _div
                                                []
                                                [ _label
                                                      [ _for_ "initialCapital"
                                                        _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                                                      [ Text.raw "Initial Capital (USDT) "
                                                        _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                                                  _input
                                                      [ _id_ "initialCapital"
                                                        _name_ "initialCapital"
                                                        _type_ "number"
                                                        _value_ "10000"
                                                        _class_ selectClass
                                                        _min_ "1"
                                                        _step_ "0.01"
                                                        _required_ ] ] ]
                                      _div
                                          [ _class_ "px-6 py-4 flex justify-end space-x-3 border-t border-slate-100" ]
                                          [ _button
                                                [ _type_ "button"
                                                  _class_
                                                      "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                                  Hx.get "/backtests/modal/close"
                                                  Hx.targetCss "#modal-container"
                                                  Hx.swapInnerHtml ]
                                                [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Cancel" ]
                                            _button
                                                [ _type_ "submit"
                                                  _class_
                                                      "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
                                                [ _i [ _class_ "fas fa-play mr-2" ] []; Text.raw "Run Backtest" ] ] ] ] ] ] ]

    let statusPolling (runId: int) =
        _div
            [ _id_ "backtest-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
              _role_ "dialog"
              Attr.create "aria-modal" "true" ]
            [ _div [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity" ] []
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center"
                                Hx.get $"/backtests/{runId}/status"
                                Hx.trigger "every 2s"
                                Hx.swap HxSwap.OuterHTML ]
                              [ _div
                                    [ _class_ "mb-4" ]
                                    [ _i [ _class_ "fas fa-spinner fa-spin text-3xl text-slate-400" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Running Backtest..." ]
                                _p
                                    [ _class_ "text-slate-500 text-sm" ]
                                    [ Text.raw "Processing candles and executing pipeline steps" ] ] ] ] ]

    let errorResult (message: string) =
        _div
            [ _id_ "backtest-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Backtest Failed" ]
                                _p [ _class_ "text-slate-600 mb-4" ] [ Text.raw message ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/backtests/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let private metricCard (label: string) (value: string) (colorClass: string) =
        _div
            [ _class_ "bg-white border border-slate-200 rounded-lg px-4 py-3 text-center" ]
            [ _div [ _class_ "text-xs text-slate-500 mb-1" ] [ Text.raw label ]
              _div [ _class_ $"text-lg font-semibold {colorClass}" ] [ Text.raw value ] ]

    let private formatPct (v: decimal) = $"{v:F2}%%"
    let private formatMoney (v: decimal) = $"{v:F2}"

    let private formatDuration (d: TimeSpan) =
        if d.TotalDays >= 1.0 then $"{int d.TotalDays}d {d.Hours}h"
        elif d.TotalHours >= 1.0 then $"{int d.TotalHours}h {d.Minutes}m"
        else $"{int d.TotalMinutes}m"

    let toUnixSeconds (dt: DateTime) = DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds()

    let candlesJson (candles: Candlestick list) =
        candles
        |> List.map (fun c ->
            let t = toUnixSeconds c.Timestamp
            $"""{{"time":{t},"open":{c.Open},"high":{c.High},"low":{c.Low},"close":{c.Close}}}"""
        )
        |> String.concat ","
        |> fun s -> $"[{s}]"

    let equityJsonUnix (points: BacktestEquityPoint list) =
        points
        |> List.map (fun p -> $"""{{"time":{toUnixSeconds p.CandleTime},"value":{p.Equity}}}""")
        |> String.concat ","
        |> fun s -> $"[{s}]"

    let markersJsonUnix (pairs: TradePair list) =
        pairs
        |> List.collect (fun p ->
            [ $"""{{"time":{toUnixSeconds p.EntryTime},"position":"belowBar","color":"#22c55e","shape":"arrowUp","text":"BUY"}}"""
              $"""{{"time":{toUnixSeconds p.ExitTime},"position":"aboveBar","color":"#ef4444","shape":"arrowDown","text":"SELL"}}""" ]
        )
        |> String.concat ","
        |> fun s -> $"[{s}]"

    let private chartScript (runId: int) =
        $"""(function() {{
    var el = document.getElementById('backtest-chart');
    if (!el || !window.LightweightCharts) return;
    var chart = LightweightCharts.createChart(el, {{
        height: 400,
        layout: {{ background: {{ color: '#fff' }}, textColor: '#64748b' }},
        grid: {{ vertLines: {{ color: '#f1f5f9' }}, horzLines: {{ color: '#f1f5f9' }} }},
        timeScale: {{ timeVisible: true }},
        rightPriceScale: {{ visible: true }}
    }});
    var candleSeries = chart.addCandlestickSeries({{
        upColor: '#22c55e', downColor: '#ef4444',
        borderUpColor: '#22c55e', borderDownColor: '#ef4444',
        wickUpColor: '#22c55e', wickDownColor: '#ef4444'
    }});
    var equitySeries = chart.addLineSeries({{
        color: '#3b82f6', lineWidth: 2,
        priceScaleId: 'equity',
        lastValueVisible: false,
        priceLineVisible: false
    }});
    chart.priceScale('equity').applyOptions({{
        scaleMargins: {{ top: 0.1, bottom: 0.1 }}
    }});
    fetch('/backtests/{runId}/chart-data')
        .then(function(r) {{ return r.json(); }})
        .then(function(data) {{
            if (data.candles && data.candles.length) candleSeries.setData(data.candles);
            if (data.equity && data.equity.length) equitySeries.setData(data.equity);
            if (data.markers && data.markers.length) candleSeries.setMarkers(data.markers);
            chart.timeScale().fitContent();
        }});
    chart.subscribeClick(function(param) {{
        if (!param.time) return;
        htmx.ajax('GET', '/backtests/{runId}/traces/by-time?t=' + param.time,
            {{target: '#trace-detail', swap: 'innerHTML'}});
    }});
    new ResizeObserver(function(entries) {{
        chart.applyOptions({{ width: entries[0].contentRect.width }});
    }}).observe(el);
}})();"""

    let resultsView (vm: ResultsViewModel) =
        let m = vm.Metrics

        let returnColor = if m.TotalReturn >= 0m then "text-green-600" else "text-red-600"

        _div
            [ _id_ "backtest-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
              _role_ "dialog"
              Attr.create "aria-modal" "true" ]
            [ modalBackdrop
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-6xl" ]
                              [
                                // Header
                                _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _div
                                                []
                                                [ _h3
                                                      [ _class_ "text-lg font-semibold text-slate-900" ]
                                                      [ _i [ _class_ "fas fa-flask mr-2 text-slate-400" ] []
                                                        Text.raw "Backtest Results" ]
                                                  _p
                                                      [ _class_ "text-slate-500 text-sm mt-1" ]
                                                      [ let startStr = vm.Run.StartDate.ToString("MMM dd, yyyy")
                                                        let endStr = vm.Run.EndDate.ToString("MMM dd, yyyy")
                                                        Text.raw $"{vm.Pipeline.Instrument} | {startStr} — {endStr}" ] ]
                                            _div
                                                [ _class_ "flex items-center gap-3" ]
                                                [ _a
                                                      [ _href_ $"/backtests/{vm.Run.Id}/export"
                                                        _class_
                                                            "px-3 py-1.5 text-sm font-medium text-slate-600 border border-slate-200 rounded-md hover:bg-slate-50" ]
                                                      [ _i [ _class_ "fas fa-download mr-1.5" ] []; Text.raw "Excel" ]
                                                  closeModalButton ] ] ]

                                // Content
                                _div
                                    [ _class_ "px-6 py-4 max-h-[80vh] overflow-y-auto space-y-6" ]
                                    [
                                      // Metrics strip
                                      _div
                                          [ _class_ "grid grid-cols-5 gap-3" ]
                                          [ metricCard "Total Return" (formatPct m.TotalReturn) returnColor
                                            metricCard
                                                "Win Rate"
                                                (formatPct m.WinRate)
                                                (if m.WinRate >= 50m then "text-green-600" else "text-slate-900")
                                            metricCard "Max Drawdown" (formatPct m.MaxDrawdownPct) "text-red-600"
                                            metricCard "Sharpe Ratio" (formatMoney m.SharpeRatio) "text-slate-900"
                                            metricCard "Total Trades" (string m.TotalTrades) "text-slate-900" ]

                                      // Extended metrics
                                      _div
                                          [ _class_ "grid grid-cols-2 gap-4" ]
                                          [ _div
                                                [ _class_ "bg-slate-50 rounded-lg p-4 space-y-2" ]
                                                [ _h4
                                                      [ _class_
                                                            "text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2" ]
                                                      [ Text.raw "Profit & Loss" ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Profit Factor" ]
                                                        _span
                                                            [ _class_ "font-medium text-slate-900" ]
                                                            [ Text.raw (formatMoney m.ProfitFactor) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Avg Win" ]
                                                        _span
                                                            [ _class_ "font-medium text-green-600" ]
                                                            [ Text.raw (formatMoney m.AverageWin) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Avg Loss" ]
                                                        _span
                                                            [ _class_ "font-medium text-red-600" ]
                                                            [ Text.raw (formatMoney m.AverageLoss) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Largest Win" ]
                                                        _span
                                                            [ _class_ "font-medium text-green-600" ]
                                                            [ Text.raw (formatMoney m.LargestWin) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Largest Loss" ]
                                                        _span
                                                            [ _class_ "font-medium text-red-600" ]
                                                            [ Text.raw (formatMoney m.LargestLoss) ] ] ]
                                            _div
                                                [ _class_ "bg-slate-50 rounded-lg p-4 space-y-2" ]
                                                [ _h4
                                                      [ _class_
                                                            "text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2" ]
                                                      [ Text.raw "Performance" ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Final Capital" ]
                                                        _span
                                                            [ _class_ "font-medium text-slate-900" ]
                                                            [ Text.raw (formatMoney m.FinalCapital) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Winning Trades" ]
                                                        _span
                                                            [ _class_ "font-medium text-green-600" ]
                                                            [ Text.raw (string m.WinningTrades) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span [ _class_ "text-slate-500" ] [ Text.raw "Losing Trades" ]
                                                        _span
                                                            [ _class_ "font-medium text-red-600" ]
                                                            [ Text.raw (string m.LosingTrades) ] ]
                                                  _div
                                                      [ _class_ "flex justify-between text-sm" ]
                                                      [ _span
                                                            [ _class_ "text-slate-500" ]
                                                            [ Text.raw "Avg Holding Period" ]
                                                        _span
                                                            [ _class_ "font-medium text-slate-900" ]
                                                            [ Text.raw (formatDuration m.AverageHoldingPeriod) ] ] ] ]

                                      // Chart
                                      _div
                                          [ _class_ "border border-slate-200 rounded-lg p-4" ]
                                          [ _h4
                                                [ _class_ "text-sm font-semibold text-slate-700 mb-3" ]
                                                [ Text.raw "Price & Equity" ]
                                            _div [ _id_ "backtest-chart"; _style_ "height:400px" ] []
                                            _script [] [ Text.raw (chartScript vm.Run.Id) ] ]

                                      // Trade table
                                      _div
                                          [ _class_ "border border-slate-200 rounded-lg" ]
                                          [ _h4
                                                [ _class_
                                                      "text-sm font-semibold text-slate-700 px-4 py-3 border-b border-slate-100" ]
                                                [ Text.raw "Trades" ]
                                            _div
                                                [ _id_ "backtest-trades-container"
                                                  Hx.get $"/backtests/{vm.Run.Id}/trades?page=1"
                                                  Hx.trigger "load"
                                                  Hx.swapInnerHtml ]
                                                [ _div
                                                      [ _class_ "flex justify-center py-4" ]
                                                      [ _i [ _class_ "fas fa-spinner fa-spin text-slate-300" ] [] ] ] ]

                                      // Executions
                                      _div
                                          [ _class_ "border border-slate-200 rounded-lg" ]
                                          [ _h4
                                                [ _class_
                                                      "text-sm font-semibold text-slate-700 px-4 py-3 border-b border-slate-100" ]
                                                [ Text.raw "Executions" ]
                                            _div
                                                [ _id_ "backtest-executions-container"
                                                  Hx.get $"/backtests/{vm.Run.Id}/executions?page=1"
                                                  Hx.trigger "load"
                                                  Hx.swapInnerHtml ]
                                                [ _div
                                                      [ _class_ "flex justify-center py-4" ]
                                                      [ _i [ _class_ "fas fa-spinner fa-spin text-slate-300" ] [] ] ] ]

                                      // Trace container
                                      _div [ _id_ "trace-detail" ] [] ] ] ] ] ]

    let tradeRows (runId: int) (pairs: TradePair list) (page: int) (pageSize: int) (totalPairs: int) =
        let totalPages = max 1 (int (Math.Ceiling(float totalPairs / float pageSize)))
        let hasPrev = page > 1
        let hasNext = page < totalPages

        _div
            []
            [ if pairs.IsEmpty then
                  _div [ _class_ "py-6 text-center text-slate-400 text-sm" ] [ Text.raw "No trades executed" ]
              else
                  _table
                      [ _class_ "w-full text-sm" ]
                      [ _thead
                            []
                            [ _tr
                                  [ _class_
                                        "text-left text-xs text-slate-500 uppercase tracking-wide border-b border-slate-100" ]
                                  [ _th [ _class_ "px-4 py-2" ] [ Text.raw "#" ]
                                    _th [ _class_ "px-4 py-2" ] [ Text.raw "Entry Time" ]
                                    _th [ _class_ "px-4 py-2" ] [ Text.raw "Exit Time" ]
                                    _th [ _class_ "px-4 py-2 text-right" ] [ Text.raw "Entry" ]
                                    _th [ _class_ "px-4 py-2 text-right" ] [ Text.raw "Exit" ]
                                    _th [ _class_ "px-4 py-2 text-right" ] [ Text.raw "Qty" ]
                                    _th [ _class_ "px-4 py-2 text-right" ] [ Text.raw "P&L" ]
                                    _th [ _class_ "px-4 py-2 w-10" ] [] ] ]
                        _tbody
                            []
                            [ let offset = (page - 1) * pageSize

                              for i, pair in pairs |> List.indexed do
                                  let pnlColor = if pair.Pnl >= 0m then "text-green-600" else "text-red-600"

                                  _tr
                                      [ _class_ "border-b border-slate-50 hover:bg-slate-50 transition-colors" ]
                                      [ _td
                                            [ _class_ "px-4 py-2 text-slate-400" ]
                                            [ Text.raw (string (offset + i + 1)) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-slate-700" ]
                                            [ Text.raw (pair.EntryTime.ToString("MMM dd, HH:mm")) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-slate-700" ]
                                            [ Text.raw (pair.ExitTime.ToString("MMM dd, HH:mm")) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-right font-mono text-slate-700" ]
                                            [ Text.raw (formatMoney pair.EntryPrice) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-right font-mono text-slate-700" ]
                                            [ Text.raw (formatMoney pair.ExitPrice) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-right font-mono text-slate-700" ]
                                            [ Text.raw $"{pair.Quantity:F4}" ]
                                        _td
                                            [ _class_ $"px-4 py-2 text-right font-mono font-medium {pnlColor}" ]
                                            [ Text.raw (formatMoney pair.Pnl) ]
                                        _td
                                            [ _class_ "px-4 py-2" ]
                                            [ _button
                                                  [ _type_ "button"
                                                    _class_ "text-slate-400 hover:text-slate-600 transition-colors"
                                                    Hx.get
                                                        $"/backtests/{runId}/traces/by-time?t={toUnixSeconds pair.EntryTime}"
                                                    Hx.targetCss "#trace-detail"
                                                    Hx.swapInnerHtml ]
                                                  [ _i [ _class_ "fas fa-list-ul text-xs" ] [] ] ] ] ] ]

              let enabledBtnClass = "bg-white text-slate-600 border border-slate-200 hover:bg-slate-50"

              let disabledBtnClass = "bg-slate-50 text-slate-300 cursor-not-allowed"

              _div
                  [ _class_ "flex items-center justify-between px-4 py-3 border-t border-slate-100" ]
                  [ _div [ _class_ "text-xs text-slate-400" ] [ Text.raw $"{totalPairs} trade(s)" ]
                    _div
                        [ _class_ "flex gap-2" ]
                        [ _button
                              [ _type_ "button"
                                _class_
                                    $"px-3 py-1 text-xs font-medium rounded-md {if hasPrev then enabledBtnClass else disabledBtnClass}"
                                if hasPrev then
                                    Hx.get $"/backtests/{runId}/trades?page={page - 1}"
                                    Hx.targetCss "#backtest-trades-container"
                                    Hx.swapInnerHtml
                                else
                                    _disabled_ ]
                              [ Text.raw "Previous" ]
                          _span
                              [ _class_ "px-3 py-1 text-xs text-slate-400" ]
                              [ Text.raw $"Page {page} of {totalPages}" ]
                          _button
                              [ _type_ "button"
                                _class_
                                    $"px-3 py-1 text-xs font-medium rounded-md {if hasNext then enabledBtnClass else disabledBtnClass}"
                                if hasNext then
                                    Hx.get $"/backtests/{runId}/trades?page={page + 1}"
                                    Hx.targetCss "#backtest-trades-container"
                                    Hx.swapInnerHtml
                                else
                                    _disabled_ ]
                              [ Text.raw "Next" ] ] ] ]

    let private outcomeBadge (outcome: int) =
        let cls, text =
            match outcome with
            | 0 -> "bg-emerald-50 text-emerald-700", "OK"
            | 1 -> "bg-amber-50 text-amber-700", "Stop"
            | _ -> "bg-red-50 text-red-700", "Fail"

        _span [ _class_ $"px-1.5 py-0.5 text-[10px] font-medium rounded {cls}" ] [ Text.raw text ]

    let executionList (runId: int) (summaries: ExecutionSummary list) (page: int) (pageSize: int) (total: int) =
        let totalPages = max 1 (int (Math.Ceiling(float total / float pageSize)))
        let hasPrev = page > 1
        let hasNext = page < totalPages

        _div
            []
            [ if summaries.IsEmpty then
                  _div [ _class_ "py-6 text-center text-slate-400 text-sm" ] [ Text.raw "No executions" ]
              else
                  _table
                      [ _class_ "w-full text-sm" ]
                      [ _thead
                            []
                            [ _tr
                                  [ _class_
                                        "text-left text-xs text-slate-500 uppercase tracking-wide border-b border-slate-100" ]
                                  [ _th [ _class_ "px-4 py-2" ] [ Text.raw "Time" ]
                                    _th [ _class_ "px-4 py-2" ] [ Text.raw "Execution ID" ]
                                    _th [ _class_ "px-4 py-2 text-right" ] [ Text.raw "Steps" ]
                                    _th [ _class_ "px-4 py-2" ] [ Text.raw "Outcome" ]
                                    _th [ _class_ "px-4 py-2 w-10" ] [] ] ]
                        _tbody
                            []
                            [ for s in summaries do
                                  _tr
                                      [ _class_ "border-b border-slate-50 hover:bg-slate-50" ]
                                      [ _td
                                            [ _class_ "px-4 py-2 text-slate-700" ]
                                            [ Text.raw (s.CandleTime.ToString("MMM dd, HH:mm")) ]
                                        _td
                                            [ _class_
                                                  "px-4 py-2 font-mono text-xs text-slate-500 truncate max-w-[12rem]" ]
                                            [ Text.raw (
                                                  if s.ExecutionId.Length > 12 then
                                                      s.ExecutionId.[..11] + "..."
                                                  else
                                                      s.ExecutionId
                                              ) ]
                                        _td
                                            [ _class_ "px-4 py-2 text-right text-slate-700" ]
                                            [ Text.raw (string s.StepCount) ]
                                        _td [ _class_ "px-4 py-2" ] [ outcomeBadge s.MaxOutcome ]
                                        _td
                                            [ _class_ "px-4 py-2" ]
                                            [ _button
                                                  [ _type_ "button"
                                                    _class_ "text-slate-400 hover:text-slate-600"
                                                    Hx.get
                                                        $"/backtests/{runId}/traces/by-time?t={toUnixSeconds s.CandleTime}"
                                                    Hx.targetCss "#trace-detail"
                                                    Hx.swapInnerHtml ]
                                                  [ _i [ _class_ "fas fa-eye text-xs" ] [] ] ] ] ] ]

              let enabledBtnClass = "bg-white text-slate-600 border border-slate-200 hover:bg-slate-50"

              let disabledBtnClass = "bg-slate-50 text-slate-300 cursor-not-allowed"

              _div
                  [ _class_ "flex items-center justify-between px-4 py-3 border-t border-slate-100" ]
                  [ _div [ _class_ "text-xs text-slate-400" ] [ Text.raw $"{total} execution(s)" ]
                    _div
                        [ _class_ "flex gap-2" ]
                        [ _button
                              [ _type_ "button"
                                _class_
                                    $"px-3 py-1 text-xs font-medium rounded-md {if hasPrev then enabledBtnClass else disabledBtnClass}"
                                if hasPrev then
                                    Hx.get $"/backtests/{runId}/executions?page={page - 1}"
                                    Hx.targetCss "#backtest-executions-container"
                                    Hx.swapInnerHtml
                                else
                                    _disabled_ ]
                              [ Text.raw "Previous" ]
                          _span
                              [ _class_ "px-3 py-1 text-xs text-slate-400" ]
                              [ Text.raw $"Page {page} of {totalPages}" ]
                          _button
                              [ _type_ "button"
                                _class_
                                    $"px-3 py-1 text-xs font-medium rounded-md {if hasNext then enabledBtnClass else disabledBtnClass}"
                                if hasNext then
                                    Hx.get $"/backtests/{runId}/executions?page={page + 1}"
                                    Hx.targetCss "#backtest-executions-container"
                                    Hx.swapInnerHtml
                                else
                                    _disabled_ ]
                              [ Text.raw "Next" ] ] ] ]

    let traceDetail (logs: BacktestExecutionLog list) =
        if logs.IsEmpty then
            _div [ _class_ "py-4 text-center text-slate-400 text-sm" ] [ Text.raw "No step logs" ]
        else
            let first = logs.Head

            let borderColor outcome =
                match outcome with
                | 0 -> "border-emerald-400"
                | 1 -> "border-amber-400"
                | _ -> "border-red-400"

            let outcomeIcon outcome =
                match outcome with
                | 0 -> "fas fa-check-circle text-emerald-500"
                | 1 -> "fas fa-stop-circle text-amber-500"
                | _ -> "fas fa-times-circle text-red-500"

            _div
                [ _class_ "border border-slate-200 rounded-lg p-4 space-y-3" ]
                [ _div
                      [ _class_ "flex items-center gap-3 pb-3 border-b border-slate-100" ]
                      [ _i [ _class_ "fas fa-list-ul text-slate-400" ] []
                        _span
                            [ _class_ "text-sm font-semibold text-slate-700" ]
                            [ Text.raw (first.CandleTime.ToString("MMM dd, HH:mm")) ]
                        _span [ _class_ "text-xs text-slate-400 font-mono truncate" ] [ Text.raw first.ExecutionId ] ]
                  _div
                      [ _class_ "space-y-2" ]
                      [ for log in logs do
                            _div
                                [ _class_ $"border-l-2 {borderColor log.Outcome} pl-3 py-1.5" ]
                                [ _div
                                      [ _class_ "flex items-center gap-2 text-sm" ]
                                      [ _i [ _class_ (outcomeIcon log.Outcome) ] []
                                        _span [ _class_ "font-medium text-slate-700" ] [ Text.raw log.StepTypeKey ]
                                        outcomeBadge log.Outcome
                                        _span
                                            [ _class_ "text-slate-500 text-xs flex-1 truncate" ]
                                            [ Text.raw (if String.IsNullOrEmpty log.Message then "" else log.Message) ] ]
                                  if not (String.IsNullOrEmpty log.Context) then
                                      Elem.create
                                          "details"
                                          [ _class_ "mt-1" ]
                                          [ Elem.create
                                                "summary"
                                                [ _class_ "text-xs text-slate-400 cursor-pointer hover:text-slate-600" ]
                                                [ Text.raw "Context" ]
                                            _pre
                                                [ _class_
                                                      "text-xs text-slate-500 bg-slate-50 rounded p-2 mt-1 overflow-x-auto max-h-40" ]
                                                [ Text.raw log.Context ] ] ] ] ]

    let private statusBadge (status: BacktestStatus) =
        let cls, text =
            match status with
            | BacktestStatus.Pending -> "bg-blue-50 text-blue-600", "Pending"
            | BacktestStatus.Running -> "bg-blue-50 text-blue-600", "Running"
            | BacktestStatus.Completed -> "bg-green-50 text-green-700", "Completed"
            | BacktestStatus.Failed -> "bg-red-50 text-red-700", "Failed"
            | BacktestStatus.Cancelled -> "bg-slate-50 text-slate-500", "Cancelled"
            | _ -> "bg-slate-50 text-slate-500", "Unknown"

        _span
            [ _class_ $"inline-flex items-center gap-1.5 px-2 py-0.5 text-xs font-medium rounded {cls}" ]
            [ if status = BacktestStatus.Pending || status = BacktestStatus.Running then
                  _i [ _class_ "fas fa-spinner fa-spin text-[10px]" ] []
              Text.raw text ]

    let private gridRow (item: BacktestGridItem) =
        let polling =
            if item.Status = BacktestStatus.Pending || item.Status = BacktestStatus.Running then
                [ Hx.get $"/backtests/{item.RunId}/row"; Hx.trigger "every 3s"; Hx.swap HxSwap.OuterHTML ]
            else
                []

        _tr
            ([ _id_ $"backtest-row-{item.RunId}"; _class_ "hover:bg-slate-50" ] @ polling)
            [ _td
                  [ _class_ "px-4 py-3 whitespace-nowrap" ]
                  [ _span [ _class_ "font-medium text-slate-900 text-sm" ] [ Text.raw item.PipelineInstrument ] ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw $"""{item.StartDate.ToString("MMM dd")} — {item.EndDate.ToString("MMM dd, yyyy")}""" ]
              _td [ _class_ "px-4 py-3 whitespace-nowrap" ] [ statusBadge item.Status ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ if item.Status = BacktestStatus.Completed then
                        let returnPct =
                            if item.InitialCapital > 0m && item.FinalCapital.HasValue then
                                (item.FinalCapital.Value - item.InitialCapital) / item.InitialCapital * 100m
                            else
                                0m

                        let returnColor = if returnPct >= 0m then "text-green-600" else "text-red-600"

                        _span
                            [ _class_ "flex items-center gap-3" ]
                            [ _span [ _class_ $"font-medium {returnColor}" ] [ Text.raw $"{returnPct:F2}%%" ]
                              if item.WinRate.HasValue then
                                  _span [ _class_ "text-slate-400" ] [ Text.raw $"WR {item.WinRate.Value:F1}%%" ]
                              _span [ _class_ "text-slate-400" ] [ Text.raw $"{item.TotalTrades} trades" ] ]
                    elif item.Status = BacktestStatus.Failed then
                        _span
                            [ _class_ "text-red-400 text-xs truncate max-w-[200px]" ]
                            [ Text.raw (if String.IsNullOrEmpty item.ErrorMessage then "Error" else item.ErrorMessage) ]
                    else
                        _span [ _class_ "text-slate-300" ] [ Text.raw "—" ] ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw (item.CreatedAt.ToString("MMM dd, HH:mm")) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-right text-sm" ]
                  [ if item.Status = BacktestStatus.Completed then
                        _button
                            [ _class_ "text-slate-400 hover:text-slate-600 mr-3"
                              Hx.get $"/backtests/{item.RunId}/results"
                              Hx.targetCss "#modal-container"
                              Hx.swapInnerHtml ]
                            [ Text.raw "Results" ]
                    _button
                        [ _class_ "text-slate-400 hover:text-red-500"
                          Hx.delete $"/backtests/{item.RunId}"
                          Hx.confirm "Are you sure you want to delete this backtest run?"
                          Hx.targetCss $"#backtest-row-{item.RunId}"
                          Hx.swapOuterHtml ]
                        [ Text.raw "Delete" ] ] ]

    let private gridTableHeader =
        _thead
            []
            [ _tr
                  []
                  [ for (text, align) in
                        [ "Instrument", "left"
                          "Date Range", "left"
                          "Status", "left"
                          "Metrics", "left"
                          "Created", "left"
                          "Actions", "right" ] do
                        _th
                            [ _class_
                                  $"px-4 py-3 text-{align} text-xs font-medium text-slate-400 uppercase tracking-wider" ]
                            [ Text.raw text ] ] ]

    let gridEmptyState =
        _tr
            []
            [ _td
                  [ _colspan_ "6"; _class_ "px-4 py-12 text-center" ]
                  [ _div
                        [ _class_ "text-slate-400" ]
                        [ _i [ _class_ "fas fa-flask text-3xl mb-3" ] []
                          _p [ _class_ "text-sm font-medium" ] [ Text.raw "No backtests yet" ]
                          _p [ _class_ "text-xs" ] [ Text.raw "Run a backtest from any pipeline to see results here" ] ] ] ]

    let grid (items: BacktestGridItem list) (total: int) =
        let rows =
            match items with
            | [] -> [ gridEmptyState ]
            | items -> items |> List.map gridRow

        _div
            [ _id_ "backtests-grid-container" ]
            [ _div
                  [ _class_ "overflow-x-auto" ]
                  [ _table
                        [ _class_ "min-w-full divide-y divide-slate-100" ]
                        [ gridTableHeader; _tbody [ _class_ "bg-white divide-y divide-slate-100" ] rows ] ]
              _div
                  [ _class_ "px-4 py-3 border-t border-slate-100" ]
                  [ _div [ _class_ "text-xs text-slate-400" ] [ Text.raw $"{total} backtest run(s)" ] ] ]

    let gridSection (items: BacktestGridItem list) (total: int) =
        _section
            []
            [ _div
                  [ _class_ "flex justify-between items-center mb-6" ]
                  [ _div
                        []
                        [ _h1 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "Backtests" ]
                          _p [ _class_ "text-slate-400 text-sm" ] [ Text.raw "Monitor backtest runs and results" ] ] ]
              _div [ _class_ "card overflow-hidden" ] [ grid items total ] ]

    let count (n: int) = Text.raw (string n)

    let gridRowView (item: BacktestGridItem) = gridRow item

module Handler =
    let private tryGetQueryInt (query: IQueryCollection) (key: string) (defaultValue: int) =
        match query.TryGetValue key with
        | true, values when values.Count > 0 ->
            match Int32.TryParse values.[0] with
            | true, v -> v
            | false, _ -> defaultValue
        | _ -> defaultValue

    let configureModal (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! pipeline = PipelineRepository.getById db pipelineId ctx.RequestAborted

                    match pipeline with
                    | Ok p -> return! Response.ofHtml (View.configureModal pipelineId (string p.Instrument)) ctx
                    | Error _ -> return! Response.ofHtml (View.errorResult "Pipeline not found") ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading pipeline {PipelineId} for backtest", pipelineId)
                    return! Response.ofHtml (View.errorResult "Failed to load pipeline") ctx
            }

    let closeModal: HttpHandler = Response.ofHtml (_div [] [])

    let grid: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! Data.getGridItems db 0 50 ctx.RequestAborted with
                    | Ok(items, total) -> return! Response.ofHtml (View.gridSection items total) ctx
                    | Error _ -> return! Response.ofHtml (View.gridSection [] 0) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading backtests grid")
                    return! Response.ofHtml (View.gridSection [] 0) ctx
            }

    let count: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! result = BacktestRepository.countRuns db ctx.RequestAborted

                    match result with
                    | Ok n -> return! Response.ofHtml (View.count n) ctx
                    | Error _ -> return! Response.ofHtml (View.count 0) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error getting backtests count")
                    return! Response.ofHtml (View.count 0) ctx
            }

    let delete (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! _ = BacktestRepository.deleteRun db runId ctx.RequestAborted
                    return! Response.ofEmpty ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error deleting backtest run {RunId}", runId)
                    return! Response.ofEmpty ctx
            }

    let row (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! BacktestRepository.getRunById db runId ctx.RequestAborted with
                    | Ok(Some run) ->
                        let! pipeline = PipelineRepository.getById db run.PipelineId ctx.RequestAborted

                        let instrument =
                            match pipeline with
                            | Ok p -> string p.Instrument
                            | Error _ -> $"Pipeline #{run.PipelineId}"

                        let item: BacktestGridItem =
                            { RunId = run.Id
                              PipelineInstrument = instrument
                              Status = run.Status
                              StartDate = run.StartDate
                              EndDate = run.EndDate
                              IntervalMinutes = run.IntervalMinutes
                              InitialCapital = run.InitialCapital
                              FinalCapital = run.FinalCapital
                              TotalTrades = run.TotalTrades
                              WinRate = run.WinRate
                              ErrorMessage = run.ErrorMessage
                              CreatedAt = run.CreatedAt }

                        return! Response.ofHtml (View.gridRowView item) ctx
                    | _ -> return! Response.ofEmpty ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading backtest row {RunId}", runId)
                    return! Response.ofEmpty ctx
            }

    let run: HttpHandler =
        fun ctx ->
            task {
                try
                    let! form = Request.getForm ctx
                    let pipelineId = form.TryGetInt "pipelineId" |> Option.defaultValue 0
                    let startDateStr = form.TryGetString "startDate" |> Option.defaultValue ""
                    let endDateStr = form.TryGetString "endDate" |> Option.defaultValue ""
                    let intervalMinutes = form.TryGetInt "intervalMinutes" |> Option.defaultValue 1

                    let initialCapital = form.TryGetFloat "initialCapital" |> Option.defaultValue 10000.0

                    if
                        pipelineId = 0 || String.IsNullOrWhiteSpace startDateStr || String.IsNullOrWhiteSpace endDateStr
                    then
                        return! Response.ofHtml (View.errorResult "All fields are required") ctx
                    else
                        let config: BacktestConfig =
                            { PipelineId = pipelineId
                              StartDate = DateTime.Parse(startDateStr)
                              EndDate = DateTime.Parse(endDateStr).AddDays(1.0)
                              IntervalMinutes = intervalMinutes
                              InitialCapital = decimal initialCapital }

                        let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                        use scope = scopeFactory.CreateScope()
                        use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                        match! BacktestRepository.createRun db config ctx.RequestAborted with
                        | Error err -> return! Response.ofHtml (View.errorResult $"Failed to create run: {err}") ctx
                        | Ok runId ->
                            let services = ctx.RequestServices

                            let _ =
                                Task.Run(
                                    Func<Task>(fun () ->
                                        task {
                                            try
                                                let! _ =
                                                    BacktestEngine.run scopeFactory runId config CancellationToken.None

                                                ()
                                            with ex ->
                                                use s = services.CreateScope()
                                                use db2 = s.ServiceProvider.GetRequiredService<IDbConnection>()

                                                let! _ =
                                                    BacktestRepository.updateRunResults
                                                        db2
                                                        { Id = runId
                                                          PipelineId = config.PipelineId
                                                          Status = BacktestStatus.Failed
                                                          StartDate = config.StartDate
                                                          EndDate = config.EndDate
                                                          IntervalMinutes = config.IntervalMinutes
                                                          InitialCapital = config.InitialCapital
                                                          FinalCapital = Nullable()
                                                          TotalTrades = 0
                                                          WinRate = Nullable()
                                                          MaxDrawdown = Nullable()
                                                          SharpeRatio = Nullable()
                                                          ErrorMessage = ex.Message
                                                          CreatedAt = DateTime.UtcNow
                                                          CompletedAt = Nullable DateTime.UtcNow }
                                                        CancellationToken.None

                                                ()
                                        }
                                        :> Task
                                    )
                                )

                            ctx.Response.Headers.Append("HX-Trigger", "backtestsUpdated")
                            return! Response.ofHtml (_div [] []) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error starting backtest")
                    return! Response.ofHtml (View.errorResult $"Failed to start backtest: {ex.Message}") ctx
            }

    let private buildResultsView (db: IDbConnection) (run: BacktestRun) (pipeline: Pipeline) (ct: CancellationToken) =
        task {
            let! tradesResult = BacktestRepository.getTradesByRun db run.Id ct
            let! equityResult = BacktestRepository.getEquityByRun db run.Id ct

            let trades = tradesResult |> Result.defaultValue []
            let equity = equityResult |> Result.defaultValue []
            let pairs = Data.getTradePairs trades
            let metrics = BacktestMetrics.calculate run.InitialCapital trades equity

            return
                View.resultsView
                    { Run = run; Pipeline = pipeline; Metrics = metrics; EquityPoints = equity; TradePairs = pairs }
        }

    let status (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! Data.getRunWithPipeline db runId ctx.RequestAborted with
                    | Error msg -> return! Response.ofHtml (View.errorResult msg) ctx
                    | Ok(run, pipeline) ->
                        match run.Status with
                        | BacktestStatus.Pending
                        | BacktestStatus.Running -> return! Response.ofHtml (View.statusPolling runId) ctx
                        | BacktestStatus.Completed ->
                            let! view = buildResultsView db run pipeline ctx.RequestAborted
                            return! Response.ofHtml view ctx
                        | _ ->
                            let msg =
                                if String.IsNullOrEmpty run.ErrorMessage then "Backtest failed" else run.ErrorMessage

                            return! Response.ofHtml (View.errorResult msg) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error checking backtest status {RunId}", runId)
                    return! Response.ofHtml (View.errorResult "Error checking status") ctx
            }

    let results (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! Data.getRunWithPipeline db runId ctx.RequestAborted with
                    | Error msg -> return! Response.ofHtml (View.errorResult msg) ctx
                    | Ok(run, pipeline) ->
                        let! view = buildResultsView db run pipeline ctx.RequestAborted
                        return! Response.ofHtml view ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading backtest results {RunId}", runId)
                    return! Response.ofHtml (View.errorResult "Error loading results") ctx
            }

    let trades (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let page = tryGetQueryInt ctx.Request.Query "page" 1
                    let pageSize = 20
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    let! tradesResult = BacktestRepository.getTradesByRun db runId ctx.RequestAborted
                    let allTrades = tradesResult |> Result.defaultValue []
                    let allPairs = Data.getTradePairs allTrades
                    let totalPairs = allPairs.Length

                    let pagedPairs = allPairs |> List.skip ((page - 1) * pageSize) |> List.truncate pageSize

                    return! Response.ofHtml (View.tradeRows runId pagedPairs page pageSize totalPairs) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading trades for run {RunId}", runId)

                    return!
                        Response.ofHtml
                            (_div
                                [ _class_ "py-4 text-center text-red-400 text-sm" ]
                                [ Text.raw "Error loading trades" ])
                            ctx
            }

    let executions (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let page = tryGetQueryInt ctx.Request.Query "page" 1
                    let pageSize = 25
                    let offset = (page - 1) * pageSize
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    let! result = BacktestRepository.getExecutionSummaries db runId offset pageSize ctx.RequestAborted

                    match result with
                    | Ok(summaries, total) ->
                        return! Response.ofHtml (View.executionList runId summaries page pageSize total) ctx
                    | Error _ ->
                        return!
                            Response.ofHtml
                                (_div
                                    [ _class_ "py-4 text-center text-red-400 text-sm" ]
                                    [ Text.raw "Error loading executions" ])
                                ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading executions for run {RunId}", runId)

                    return!
                        Response.ofHtml
                            (_div
                                [ _class_ "py-4 text-center text-red-400 text-sm" ]
                                [ Text.raw "Error loading executions" ])
                            ctx
            }

    let traces (runId: int) (executionId: string) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    let! logsResult = BacktestRepository.getLogsByExecution db runId executionId ctx.RequestAborted
                    let logs = logsResult |> Result.defaultValue []
                    return! Response.ofHtml (View.traceDetail logs) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading traces for execution {ExecutionId}", executionId)

                    return!
                        Response.ofHtml
                            (_div
                                [ _class_ "py-4 text-center text-red-400 text-sm" ]
                                [ Text.raw "Error loading traces" ])
                            ctx
            }

    let chartData (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! Data.getRunWithPipeline db runId ctx.RequestAborted with
                    | Error _ ->
                        ctx.Response.StatusCode <- 404
                        return! Response.ofPlainText "Not found" ctx
                    | Ok(run, pipeline) ->
                        let! candlesResult =
                            CandlestickRepository.query
                                db
                                pipeline.Instrument
                                pipeline.MarketType
                                "1m"
                                (Some run.StartDate)
                                (Some run.EndDate)
                                (Some 50000)
                                ctx.RequestAborted

                        let! tradesResult = BacktestRepository.getTradesByRun db run.Id ctx.RequestAborted
                        let! equityResult = BacktestRepository.getEquityByRun db run.Id ctx.RequestAborted
                        let candles = candlesResult |> Result.defaultValue [] |> List.rev
                        let trades = tradesResult |> Result.defaultValue []
                        let equity = equityResult |> Result.defaultValue []
                        let pairs = Data.getTradePairs trades

                        let json =
                            $"""{{"candles":{View.candlesJson candles},"equity":{View.equityJsonUnix equity},"markers":{View.markersJsonUnix pairs}}}"""

                        ctx.Response.ContentType <- "application/json"
                        return! Response.ofPlainText json ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading chart data {RunId}", runId)
                    ctx.Response.StatusCode <- 500
                    return! Response.ofPlainText """{"error":"Failed"}""" ctx
            }

    let tracesByTime (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let t = tryGetQueryInt ctx.Request.Query "t" 0
                    let targetTime = DateTimeOffset.FromUnixTimeSeconds(int64 t).UtcDateTime
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! logsResult = BacktestRepository.getLogsByRun db runId ctx.RequestAborted
                    let logs = logsResult |> Result.defaultValue []

                    match logs with
                    | [] -> return! Response.ofHtml (View.traceDetail []) ctx
                    | _ ->
                        let _, closest =
                            logs
                            |> List.groupBy _.ExecutionId
                            |> List.minBy (fun (_, g) -> abs (g.Head.CandleTime - targetTime).TotalSeconds)

                        return! Response.ofHtml (View.traceDetail closest) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error loading traces by time {RunId}", runId)

                    return!
                        Response.ofHtml
                            (_div [ _class_ "py-4 text-center text-red-400 text-sm" ] [ Text.raw "Error" ])
                            ctx
            }

    let export (runId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! Data.getRunWithPipeline db runId ctx.RequestAborted with
                    | Error _ ->
                        ctx.Response.StatusCode <- 404
                        return! Response.ofPlainText "Run not found" ctx
                    | Ok(run, pipeline) ->
                        let! tradesResult = BacktestRepository.getTradesByRun db run.Id ctx.RequestAborted
                        let! equityResult = BacktestRepository.getEquityByRun db run.Id ctx.RequestAborted
                        let! stepsResult = PipelineStepRepository.getByPipelineId db pipeline.Id ctx.RequestAborted
                        let trades = tradesResult |> Result.defaultValue []
                        let equity = equityResult |> Result.defaultValue []
                        let steps = stepsResult |> Result.defaultValue []
                        let metrics = BacktestMetrics.calculate run.InitialCapital trades equity

                        use ms = BacktestExport.generate run pipeline steps trades equity metrics

                        ctx.Response.ContentType <- "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"

                        ctx.Response.Headers.Append(
                            "Content-Disposition",
                            $"attachment; filename=backtest-{pipeline.Instrument}-{run.Id}.xlsx"
                        )

                        do! ms.CopyToAsync(ctx.Response.Body, ctx.RequestAborted)
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Backtest")
                    logger.LogError(ex, "Error exporting backtest {RunId}", runId)
                    ctx.Response.StatusCode <- 500
                    return! Response.ofPlainText "Export failed" ctx
            }
