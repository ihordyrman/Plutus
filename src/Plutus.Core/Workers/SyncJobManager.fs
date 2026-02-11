namespace Plutus.Core.Workers

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

module SyncJobManager =

    type JobStatus =
        | Pending
        | Running
        | Paused
        | Completed
        | Failed of string
        | Stopped

    type JobProgress =
        { FetchedCount: int
          EstimatedTotal: int
          CurrentTimestamp: DateTimeOffset
          StartedAt: DateTime
          LastUpdateAt: DateTime }

    type SyncJobState =
        { Id: int
          Symbol: string
          MarketType: MarketType
          Timeframe: string
          FromDate: DateTimeOffset
          ToDate: DateTimeOffset
          Status: JobStatus
          Progress: JobProgress
          CreatedAt: DateTime }

    type private SyncMessage =
        | StartJob of string * MarketType * string * DateTimeOffset * DateTimeOffset * AsyncReplyChannel<int>
        | StopJob of int * AsyncReplyChannel<bool>
        | PauseJob of int * AsyncReplyChannel<bool>
        | ResumeJob of int * AsyncReplyChannel<bool>
        | UpdateProgress of int * (SyncJobState -> SyncJobState)
        | GetJobs of AsyncReplyChannel<SyncJobState list>

    let private historyBoundaryDays = 30

    let private runSyncLoop
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (post: SyncMessage -> unit)
        (jobId: int)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (fromDate: DateTimeOffset)
        (toDate: DateTimeOffset)
        (cts: CancellationTokenSource)
        (pauseEvent: ManualResetEventSlim)
        =
        Task.Run(fun () ->
            task {
                try
                    use scope = scopeFactory.CreateScope()
                    let http = scope.ServiceProvider.GetRequiredService<Http.T>()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let ct = cts.Token

                    let mutable cursor = toDate
                    let mutable keepGoing = true

                    post (UpdateProgress(jobId, fun s -> { s with Status = Running }))

                    while keepGoing && not ct.IsCancellationRequested && cursor > fromDate do
                        pauseEvent.Wait(ct)

                        let daysAgo = (DateTimeOffset.UtcNow - cursor).TotalDays

                        let fetch =
                            if daysAgo > float historyBoundaryDays then
                                http.getHistoryCandlesticks
                            else
                                http.getCandlesticks

                        let afterMs = cursor.ToUnixTimeMilliseconds().ToString()

                        let! result =
                            fetch
                                symbol
                                { Bar = Some timeframe; After = Some afterMs; Before = None; Limit = Some 100 }

                        match result with
                        | Ok candles when candles.Length > 0 ->
                            let mapped =
                                candles |> Array.map (CandlestickSync.toCandlestick symbol timeframe) |> Array.toList

                            let! _ = CandlestickRepository.save db mapped ct

                            post (
                                UpdateProgress(
                                    jobId,
                                    fun s ->
                                        { s with
                                            Progress =
                                                { s.Progress with
                                                    FetchedCount = s.Progress.FetchedCount + candles.Length
                                                    CurrentTimestamp = cursor
                                                    LastUpdateAt = DateTime.UtcNow } }
                                )
                            )

                            cursor <- cursor.AddMinutes(-100.0)
                        | Ok _ -> keepGoing <- false
                        | Error err ->
                            logger.LogError("Sync job {JobId} fetch failed: {Error}", jobId, serviceMessage err)
                            keepGoing <- false

                    if not ct.IsCancellationRequested then
                        post (UpdateProgress(jobId, fun s -> { s with Status = Completed }))
                with
                | :? OperationCanceledException -> post (UpdateProgress(jobId, fun s -> { s with Status = Stopped }))
                | ex ->
                    logger.LogError(ex, "Sync job {JobId} failed", jobId)
                    post (UpdateProgress(jobId, fun s -> { s with Status = Failed ex.Message }))
            }
            :> Task
        )
        |> ignore

    type T =
        { startJob: string -> MarketType -> string -> DateTimeOffset -> DateTimeOffset -> int
          stopJob: int -> bool
          pauseJob: int -> bool
          resumeJob: int -> bool
          getJobs: unit -> SyncJobState list }

    let create (scopeFactory: IServiceScopeFactory) (logger: ILogger) : T =
        let mutable nextId = 1

        let agent =
            MailboxProcessor<SyncMessage>.Start(fun inbox ->
                let rec loop
                    (jobs: Map<int, SyncJobState>)
                    (ctss: Map<int, CancellationTokenSource>)
                    (pauses: Map<int, ManualResetEventSlim>)
                    =
                    async {
                        let! msg = inbox.Receive()

                        match msg with
                        | StartJob(symbol, marketType, timeframe, fromDate, toDate, reply) ->
                            let id = nextId
                            nextId <- nextId + 1

                            let now = DateTime.UtcNow
                            let estimatedMinutes = int (toDate - fromDate).TotalMinutes

                            let job =
                                { Id = id
                                  Symbol = symbol
                                  MarketType = marketType
                                  Timeframe = timeframe
                                  FromDate = fromDate
                                  ToDate = toDate
                                  Status = Pending
                                  Progress =
                                    { FetchedCount = 0
                                      EstimatedTotal = estimatedMinutes
                                      CurrentTimestamp = toDate
                                      StartedAt = now
                                      LastUpdateAt = now }
                                  CreatedAt = now }

                            let cts = new CancellationTokenSource()
                            let pauseEvent = new ManualResetEventSlim(true)

                            runSyncLoop
                                scopeFactory
                                logger
                                inbox.Post
                                id
                                symbol
                                marketType
                                timeframe
                                fromDate
                                toDate
                                cts
                                pauseEvent

                            reply.Reply id

                            return! loop (Map.add id job jobs) (Map.add id cts ctss) (Map.add id pauseEvent pauses)

                        | StopJob(id, reply) ->
                            match Map.tryFind id ctss, Map.tryFind id pauses with
                            | Some cts, Some pause ->
                                pause.Set()
                                cts.Cancel()
                                reply.Reply true
                                return! loop jobs ctss pauses
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | PauseJob(id, reply) ->
                            match Map.tryFind id pauses, Map.tryFind id jobs with
                            | Some pause, Some job when job.Status = Running ->
                                pause.Reset()

                                let jobs = Map.add id { job with Status = Paused } jobs

                                reply.Reply true
                                return! loop jobs ctss pauses
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | ResumeJob(id, reply) ->
                            match Map.tryFind id pauses, Map.tryFind id jobs with
                            | Some pause, Some job when job.Status = Paused ->
                                pause.Set()

                                let jobs = Map.add id { job with Status = Running } jobs

                                reply.Reply true
                                return! loop jobs ctss pauses
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | UpdateProgress(id, updater) ->
                            let jobs =
                                match Map.tryFind id jobs with
                                | Some job -> Map.add id (updater job) jobs
                                | None -> jobs

                            return! loop jobs ctss pauses

                        | GetJobs reply ->
                            let sorted = jobs |> Map.values |> Seq.sortByDescending _.CreatedAt |> Seq.toList

                            reply.Reply sorted
                            return! loop jobs ctss pauses
                    }

                loop Map.empty Map.empty Map.empty
            )

        { startJob =
            fun symbol marketType timeframe fromDate toDate ->
                agent.PostAndReply(fun reply -> StartJob(symbol, marketType, timeframe, fromDate, toDate, reply))
          stopJob = fun id -> agent.PostAndReply(fun reply -> StopJob(id, reply))
          pauseJob = fun id -> agent.PostAndReply(fun reply -> PauseJob(id, reply))
          resumeJob = fun id -> agent.PostAndReply(fun reply -> ResumeJob(id, reply))
          getJobs = fun () -> agent.PostAndReply(fun reply -> GetJobs reply) }
