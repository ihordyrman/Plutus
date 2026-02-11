namespace Plutus.App.Pages.CandlestickSync

open System
open Falco
open Falco.Htmx
open Falco.Markup
open Plutus.Core.Domain
open Plutus.Core.Workers

module View =
    let private selectClass =
        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"

    let private marketTypes = [ MarketType.Okx ]

    let private marketTypeField =
        _div
            []
            [ _label
                  [ _for_ "marketType"
                    _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                  [ Text.raw "Exchange "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
              _select
                  [ _id_ "marketType"
                    _name_ "marketType"
                    _class_ selectClass
                    Attr.create
                        "hx-on:change"
                        "var b=document.getElementById('syncBaseCurrency');if(b)htmx.trigger(b,'load')" ]
                  [ for mt in marketTypes do
                        _option [ _value_ (string (int mt)) ] [ Text.raw (mt.ToString()) ] ] ]

    let private symbolFields =
        _div
            [ _class_ "space-y-3" ]
            [ _div
                  []
                  [ _label
                        [ _for_ "syncBaseCurrency"
                          _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "Base Currency "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _select
                        [ _id_ "syncBaseCurrency"
                          _name_ "baseCurrency"
                          _class_ selectClass
                          Hx.get "/instruments/base-currencies"
                          Hx.trigger "load"
                          Hx.includeCss "[name='marketType']"
                          Hx.targetCss "#syncBaseCurrency"
                          Hx.swap HxSwap.InnerHTML
                          Attr.create
                              "hx-on::after-settle"
                              "var q=document.getElementById('syncQuoteCurrency');if(q){htmx.trigger(q,'change')}" ]
                        [ _option [ _value_ "" ] [ Text.raw "Loading..." ] ] ]
              _div
                  []
                  [ _label
                        [ _for_ "syncQuoteCurrency"
                          _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "Quote Currency "
                          _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _select
                        [ _id_ "syncQuoteCurrency"
                          _name_ "quoteCurrency"
                          _class_ selectClass
                          Hx.get "/instruments/quote-currencies"
                          Hx.trigger "change from:#syncBaseCurrency"
                          Hx.includeCss "[name='marketType'],[name='baseCurrency']"
                          Hx.targetCss "#syncQuoteCurrency"
                          Hx.swap HxSwap.InnerHTML ]
                        [ _option [ _value_ "" ] [ Text.raw "-- Select base first --" ] ] ] ]

    let private dateFields =
        let oneYearAgo = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd")
        let today = DateTime.UtcNow.ToString("yyyy-MM-dd")

        _div
            [ _class_ "grid grid-cols-2 gap-3" ]
            [ _div
                  []
                  [ _label
                        [ _for_ "fromDate"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "From Date "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _input
                        [ _id_ "fromDate"
                          _name_ "fromDate"
                          _type_ "date"
                          _value_ oneYearAgo
                          _class_ selectClass
                          Attr.create "required" "required" ] ]
              _div
                  []
                  [ _label
                        [ _for_ "toDate"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                        [ Text.raw "To Date "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                    _input
                        [ _id_ "toDate"
                          _name_ "toDate"
                          _type_ "date"
                          _value_ today
                          _class_ selectClass
                          Attr.create "required" "required" ] ] ]

    let private closeModalButton =
        _button
            [ _type_ "button"
              _class_ "text-slate-400 hover:text-slate-600 transition-colors"
              Hx.get "/candlestick-sync/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div
            [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
              Hx.get "/candlestick-sync/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            []

    let private cancelButton =
        _button
            [ _type_ "button"
              _class_ "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
              Hx.get "/candlestick-sync/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Cancel" ]

    let private submitButton =
        _button
            [ _type_ "submit"
              _class_
                  "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
            [ _i [ _class_ "fas fa-download mr-2" ] []; Text.raw "Start Sync" ]

    let modal =
        _div
            [ _id_ "sync-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
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
                              [ _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _h3
                                                [ _class_ "text-lg font-semibold text-slate-900" ]
                                                [ _i [ _class_ "fas fa-download mr-2 text-slate-400" ] []
                                                  Text.raw "New Candlestick Sync" ]
                                            closeModalButton ]
                                      _p
                                          [ _class_ "text-slate-500 text-sm mt-1" ]
                                          [ Text.raw "Download historical candlestick data" ] ]
                                _form
                                    [ _method_ "post"
                                      Hx.post "/candlestick-sync/start"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ _div
                                          [ _class_ "px-6 py-4 space-y-4 max-h-[60vh] overflow-y-auto" ]
                                          [ marketTypeField; symbolFields; dateFields ]
                                      _div
                                          [ _class_ "px-6 py-4 flex justify-end space-x-3 border-t border-slate-100" ]
                                          [ cancelButton; submitButton ] ] ] ] ] ]

    let closeModal = _div [] []

    let successResponse (symbol: string) =
        _div
            [ _id_ "sync-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ _div
                  [ Hx.get "/candlestick-sync/jobs"
                    Hx.targetCss "#sync-jobs-container"
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
                                _h3 [ _class_ "text-lg font-semibold text-slate-900 mb-2" ] [ Text.raw "Sync Started!" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw $"Candlestick sync for {symbol} has been started." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/candlestick-sync/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let errorResponse (message: string) =
        _div
            [ _id_ "sync-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
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
                                            Hx.get "/candlestick-sync/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Close" ]
                                      _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/candlestick-sync/modal"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Try Again" ] ] ] ] ] ]

    let private statusBadge (status: SyncJobManager.JobStatus) =
        let (bgClass, textClass, label) =
            match status with
            | SyncJobManager.Pending -> "bg-yellow-50", "text-yellow-700", "Pending"
            | SyncJobManager.Running -> "bg-blue-50", "text-blue-700", "Running"
            | SyncJobManager.Paused -> "bg-orange-50", "text-orange-700", "Paused"
            | SyncJobManager.Completed -> "bg-green-50", "text-green-700", "Completed"
            | SyncJobManager.Failed _ -> "bg-red-50", "text-red-700", "Failed"
            | SyncJobManager.Stopped -> "bg-slate-50", "text-slate-500", "Stopped"

        _span [ _class_ $"px-2 py-0.5 text-xs font-medium rounded {bgClass} {textClass}" ] [ Text.raw label ]

    let private progressPercent (progress: SyncJobManager.JobProgress) =
        if progress.EstimatedTotal <= 0 then
            0
        else
            min 100 (int (float progress.FetchedCount / float progress.EstimatedTotal * 100.0))

    let private progressBar (progress: SyncJobManager.JobProgress) =
        let pct = progressPercent progress

        _div
            [ _class_ "w-full bg-slate-100 rounded-full h-1.5" ]
            [ _div
                  [ _class_ "bg-blue-500 h-1.5 rounded-full transition-all"
                    Attr.create "style" $"width: {pct}%%" ]
                  [] ]

    let private jobActions (job: SyncJobManager.SyncJobState) =
        let btnClass = "px-2 py-1 text-xs font-medium rounded border transition-colors"

        match job.Status with
        | SyncJobManager.Running ->
            _div
                [ _class_ "flex gap-1" ]
                [ _button
                      [ _class_ $"{btnClass} border-orange-200 text-orange-600 hover:bg-orange-50"
                        Hx.post $"/candlestick-sync/jobs/{job.Id}/pause"
                        Hx.targetCss "#sync-jobs-container"
                        Hx.swapInnerHtml ]
                      [ _i [ _class_ "fas fa-pause mr-1" ] []; Text.raw "Pause" ]
                  _button
                      [ _class_ $"{btnClass} border-red-200 text-red-600 hover:bg-red-50"
                        Hx.post $"/candlestick-sync/jobs/{job.Id}/stop"
                        Hx.targetCss "#sync-jobs-container"
                        Hx.swapInnerHtml ]
                      [ _i [ _class_ "fas fa-stop mr-1" ] []; Text.raw "Stop" ] ]
        | SyncJobManager.Paused ->
            _div
                [ _class_ "flex gap-1" ]
                [ _button
                      [ _class_ $"{btnClass} border-blue-200 text-blue-600 hover:bg-blue-50"
                        Hx.post $"/candlestick-sync/jobs/{job.Id}/resume"
                        Hx.targetCss "#sync-jobs-container"
                        Hx.swapInnerHtml ]
                      [ _i [ _class_ "fas fa-play mr-1" ] []; Text.raw "Resume" ]
                  _button
                      [ _class_ $"{btnClass} border-red-200 text-red-600 hover:bg-red-50"
                        Hx.post $"/candlestick-sync/jobs/{job.Id}/stop"
                        Hx.targetCss "#sync-jobs-container"
                        Hx.swapInnerHtml ]
                      [ _i [ _class_ "fas fa-stop mr-1" ] []; Text.raw "Stop" ] ]
        | _ -> _div [] []

    let private jobRow (job: SyncJobManager.SyncJobState) =
        let pct = progressPercent job.Progress
        let fromStr = job.FromDate.ToString("yyyy-MM-dd")
        let toStr = job.ToDate.ToString("yyyy-MM-dd")
        let countStr = job.Progress.FetchedCount.ToString("N0")

        _div
            [ _class_ "flex items-center gap-4 px-4 py-3 border border-slate-100 rounded-lg bg-white" ]
            [ _div
                  [ _class_ "min-w-0 flex-1" ]
                  [ _div
                        [ _class_ "flex items-center gap-2 mb-1" ]
                        [ _span [ _class_ "font-medium text-sm text-slate-900" ] [ Text.raw job.Symbol ]
                          statusBadge job.Status
                          _span [ _class_ "text-xs text-slate-400" ] [ Text.raw $"{fromStr} → {toStr}" ] ]
                    _div
                        [ _class_ "flex items-center gap-3" ]
                        [ _div [ _class_ "flex-1" ] [ progressBar job.Progress ]
                          _span
                              [ _class_ "text-xs text-slate-500 whitespace-nowrap" ]
                              [ Text.raw $"{countStr} candles ({pct}%%)" ] ] ]
              jobActions job ]

    let jobsSection (jobs: SyncJobManager.SyncJobState list) =
        let hasActive =
            jobs
            |> List.exists (fun j ->
                match j.Status with
                | SyncJobManager.Running
                | SyncJobManager.Paused
                | SyncJobManager.Pending -> true
                | _ -> false
            )

        let pollingAttrs =
            if hasActive then
                [ Hx.get "/candlestick-sync/jobs"; Hx.trigger "every 2s"; Hx.swapInnerHtml ]
            else
                []

        _div
            (pollingAttrs)
            [ match jobs with
              | [] -> _div [ _class_ "text-sm text-slate-400 py-4 text-center" ] [ Text.raw "No sync jobs" ]
              | jobs ->
                  _div
                      [ _class_ "space-y-2" ]
                      [ for job in jobs do
                            jobRow job ] ]

module Handler =
    let modal: HttpHandler = Response.ofHtml View.modal
    let closeModal: HttpHandler = Response.ofHtml View.closeModal

    let start: HttpHandler =
        fun ctx ->
            task {
                try
                    let! form = Request.getForm ctx
                    let baseCurrency = form.TryGetString "baseCurrency" |> Option.defaultValue ""
                    let quoteCurrency = form.TryGetString "quoteCurrency" |> Option.defaultValue ""
                    let marketTypeInt = form.TryGetInt "marketType" |> Option.defaultValue 0
                    let fromDateStr = form.TryGetString "fromDate" |> Option.defaultValue ""
                    let toDateStr = form.TryGetString "toDate" |> Option.defaultValue ""

                    let symbol =
                        if String.IsNullOrWhiteSpace baseCurrency || String.IsNullOrWhiteSpace quoteCurrency then ""
                        else $"{baseCurrency}-{quoteCurrency}"

                    if String.IsNullOrWhiteSpace symbol then
                        return! Response.ofHtml (View.errorResponse "Base and quote currencies are required") ctx
                    elif String.IsNullOrWhiteSpace fromDateStr || String.IsNullOrWhiteSpace toDateStr then
                        return! Response.ofHtml (View.errorResponse "Date range is required") ctx
                    else
                        let marketType = enum<MarketType> marketTypeInt
                        let fromDate = DateTimeOffset(DateTime.Parse(fromDateStr), TimeSpan.Zero)
                        let toDate = DateTimeOffset(DateTime.Parse(toDateStr), TimeSpan.Zero).AddDays(1.0)

                        let manager = ctx.Plug<SyncJobManager.T>()
                        let _jobId = manager.startJob symbol marketType "1m" fromDate toDate
                        return! Response.ofHtml (View.successResponse symbol) ctx
                with ex ->
                    return! Response.ofHtml (View.errorResponse $"Failed to start sync: {ex.Message}") ctx
            }

    let jobs: HttpHandler =
        fun ctx ->
            task {
                let manager = ctx.Plug<SyncJobManager.T>()
                let jobs = manager.getJobs ()
                return! Response.ofHtml (View.jobsSection jobs) ctx
            }

    let pause (jobId: int) : HttpHandler =
        fun ctx ->
            task {
                let manager = ctx.Plug<SyncJobManager.T>()
                let _ = manager.pauseJob jobId
                let jobs = manager.getJobs ()
                return! Response.ofHtml (View.jobsSection jobs) ctx
            }

    let resume (jobId: int) : HttpHandler =
        fun ctx ->
            task {
                let manager = ctx.Plug<SyncJobManager.T>()
                let _ = manager.resumeJob jobId
                let jobs = manager.getJobs ()
                return! Response.ofHtml (View.jobsSection jobs) ctx
            }

    let stop (jobId: int) : HttpHandler =
        fun ctx ->
            task {
                let manager = ctx.Plug<SyncJobManager.T>()
                let _ = manager.stopJob jobId
                let jobs = manager.getJobs ()
                return! Response.ofHtml (View.jobsSection jobs) ctx
            }
