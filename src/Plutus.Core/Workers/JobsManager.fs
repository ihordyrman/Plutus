namespace Plutus.Core.Workers

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Exchanges.Okx
open Plutus.Core.Repositories
open Plutus.Core.Shared.Errors

module JobsManager =

    type JobStatus =
        | Pending
        | Running
        | Paused
        | Completed
        | Failed of string
        | Stopped

    let private toDbStatus =
        function
        | Pending -> SyncJobStatus.Pending
        | Running -> SyncJobStatus.Running
        | Paused -> SyncJobStatus.Paused
        | Completed -> SyncJobStatus.Completed
        | Failed _ -> SyncJobStatus.Failed
        | Stopped -> SyncJobStatus.Stopped

    let private fromDbStatus (status: int) (errorMessage: string) =
        match enum<SyncJobStatus> status with
        | SyncJobStatus.Pending -> Pending
        | SyncJobStatus.Running -> Running
        | SyncJobStatus.Paused -> Paused
        | SyncJobStatus.Completed -> Completed
        | SyncJobStatus.Failed -> Failed(errorMessage |> Option.ofObj |> Option.defaultValue "Unknown error")
        | SyncJobStatus.Stopped -> Stopped
        | _ -> Stopped

    type JobProgress =
        { FetchedCount: int
          EstimatedTotal: int
          CurrentTimestamp: DateTimeOffset
          StartedAt: DateTime
          LastUpdateAt: DateTime }

    type SyncJobState =
        { Id: int
          Instrument: Instrument
          MarketType: MarketType
          Interval: Interval
          FromDate: DateTimeOffset
          ToDate: DateTimeOffset
          Status: JobStatus
          Progress: JobProgress
          CreatedAt: DateTime }

    let private fromDbJob (job: SyncJob) : SyncJobState =
        { Id = job.Id
          Instrument = job.Instrument
          MarketType = job.MarketType
          Interval = job.Interval
          FromDate = job.FromDate
          ToDate = job.ToDate
          Status = fromDbStatus job.Status job.ErrorMessage
          Progress =
            { FetchedCount = job.FetchedCount
              EstimatedTotal = job.EstimatedTotal
              CurrentTimestamp = job.CurrentCursor
              StartedAt = job.StartedAt
              LastUpdateAt = job.LastUpdateAt }
          CreatedAt = job.CreatedAt }

    type private SyncMessage =
        | StartJob of Instrument * MarketType * Interval * DateTimeOffset * DateTimeOffset * AsyncReplyChannel<int>
        | StopJob of int * AsyncReplyChannel<bool>
        | PauseJob of int * AsyncReplyChannel<bool>
        | ResumeJob of int * AsyncReplyChannel<bool>
        | RemoveJob of int * AsyncReplyChannel<bool>
        | UpdateProgress of int * (SyncJobState -> SyncJobState)
        | GetJobs of AsyncReplyChannel<SyncJobState list>

    let private historyBoundaryDays = 1

    let private withDb
        (scopeFactory: IServiceScopeFactory)
        (f: IDbConnection -> CancellationToken -> Task<Result<'a, ServiceError>>)
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            return! f db CancellationToken.None
        }

    let private persistStatus (scopeFactory: IServiceScopeFactory) (jobId: int) (status: JobStatus) =
        task {
            let errorMsg =
                match status with
                | Failed msg -> Some msg
                | _ -> None

            let! result =
                withDb scopeFactory (fun db -> SyncJobRepository.updateStatus db jobId (toDbStatus status) errorMsg)

            match result with
            | Error err ->
                let logger = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ILogger>()
                logger.LogError("Failed to persist status for job {JobId}: {Error}", jobId, serviceMessage err)
            | _ -> ()
        }

    let private persistProgress
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (jobId: int)
        (fetchedCount: int)
        (cursor: DateTimeOffset)
        =
        task {
            let! result = withDb scopeFactory (fun db -> SyncJobRepository.updateProgress db jobId fetchedCount cursor)

            match result with
            | Error err ->
                logger.LogError("Failed to persist progress for job {JobId}: {Error}", jobId, serviceMessage err)
            | _ -> ()
        }

    type private LoopState = { Cursor: DateTimeOffset; FetchedCount: int; LastError: string option }

    let private runSyncLoop
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (post: SyncMessage -> unit)
        (jobId: int)
        (instrument: Instrument)
        (interval: Interval)
        (fromDate: DateTimeOffset)
        (startCursor: DateTimeOffset)
        (startFetchedCount: int)
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

                    let state = { Cursor = startCursor; FetchedCount = startFetchedCount; LastError = None }

                    let setRunning (s: SyncJobState) = { s with Status = Running }
                    post (UpdateProgress(jobId, setRunning))
                    do! persistStatus scopeFactory jobId Running

                    let rec fetchLoop (state: LoopState) : Task<LoopState> =
                        task {
                            match state.LastError, ct.IsCancellationRequested, state.Cursor > fromDate with
                            | Some _, _, _ -> return state
                            | _, true, _ -> return state
                            | _, _, false -> return state
                            | None, false, true ->
                                pauseEvent.Wait(ct)

                                let fetch =
                                    match (DateTimeOffset.UtcNow - state.Cursor).TotalDays with
                                    | x when x > float historyBoundaryDays -> http.getHistoryCandlesticks
                                    | _ -> http.getCandlesticks

                                let! result =
                                    fetch
                                        (string instrument)
                                        { Bar = Some interval
                                          After = Some(state.Cursor.ToUnixTimeMilliseconds().ToString())
                                          Before = None
                                          Limit = Some 100 }

                                match result with
                                | Ok candles when candles.Length > 0 ->
                                    let mapped =
                                        candles
                                        |> Array.map (CandlestickSync.toCandlestick instrument interval)
                                        |> Array.toList

                                    let cursor = state.Cursor.AddMinutes(-100.0)

                                    post (
                                        UpdateProgress(
                                            jobId,
                                            fun x ->
                                                { x with
                                                    Progress =
                                                        { x.Progress with
                                                            FetchedCount = x.Progress.FetchedCount + candles.Length
                                                            CurrentTimestamp = cursor
                                                            LastUpdateAt = DateTime.UtcNow } }
                                        )
                                    )

                                    do! CandlestickRepository.save db mapped ct |> Task.map ignore
                                    let fetchedCount = state.FetchedCount + candles.Length
                                    do! persistProgress scopeFactory logger jobId fetchedCount cursor
                                    return! fetchLoop { state with FetchedCount = fetchedCount; Cursor = cursor }
                                | Ok _ -> return state
                                | Error err ->
                                    logger.LogError(
                                        "Sync job {JobId} fetch failed: {Error}",
                                        jobId,
                                        serviceMessage err
                                    )

                                    return { state with LastError = Some(serviceMessage err) }
                        }

                    let! state = fetchLoop state

                    if not ct.IsCancellationRequested then
                        match state.LastError with
                        | Some errMsg ->
                            let setFailed (s: SyncJobState) = { s with Status = Failed errMsg }
                            post (UpdateProgress(jobId, setFailed))
                            do! persistStatus scopeFactory jobId (Failed errMsg)
                        | None ->
                            let setCompleted (s: SyncJobState) = { s with Status = Completed }
                            post (UpdateProgress(jobId, setCompleted))
                            do! persistStatus scopeFactory jobId Completed
                with
                | :? OperationCanceledException ->
                    let setStopped (s: SyncJobState) = { s with Status = Stopped }
                    post (UpdateProgress(jobId, setStopped))
                    do! persistStatus scopeFactory jobId Stopped
                | ex ->
                    logger.LogError(ex, "Sync job {JobId} failed", jobId)
                    let setFailed (s: SyncJobState) = { s with Status = Failed ex.Message }
                    post (UpdateProgress(jobId, setFailed))
                    do! persistStatus scopeFactory jobId (Failed ex.Message)
            }
            :> Task
        )
        |> ignore

    type T =
        { startJob: Instrument -> MarketType -> Interval -> DateTimeOffset -> DateTimeOffset -> int
          stopJob: int -> bool
          pauseJob: int -> bool
          resumeJob: int -> bool
          removeJob: int -> bool
          getJobs: unit -> SyncJobState list }

    let private previouslyActive = [ int SyncJobStatus.Running; int SyncJobStatus.Pending ]

    let private loadAndResume
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        : Map<int, SyncJobState> * Map<int, ManualResetEventSlim>
        =
        let result =
            task {
                match! withDb scopeFactory SyncJobRepository.getActive with
                | Ok activeJobs ->
                    let! pairs =
                        activeJobs
                        |> List.map (fun job ->
                            task {
                                match previouslyActive |> List.exists (fun x -> x = job.Status) with
                                | true ->
                                    do!
                                        withDb
                                            scopeFactory
                                            (fun db ->
                                                SyncJobRepository.updateStatus db job.Id SyncJobStatus.Paused None
                                            )
                                        |> Task.map ignore
                                | _ -> ()

                                logger.LogInformation(
                                    "Loaded sync job {JobId} for {Instrument} as Paused (was {OriginalStatus})",
                                    job.Id,
                                    job.Instrument,
                                    enum<SyncJobStatus> job.Status
                                )

                                return job.Id, { fromDbJob job with Status = Paused }, new ManualResetEventSlim(false)
                            }
                        )
                        |> Task.WhenAll

                    let jobs = pairs |> Array.map (fun (id, state, _) -> id, state) |> Map.ofArray
                    let pauses = pairs |> Array.map (fun (id, _, pause) -> id, pause) |> Map.ofArray
                    return jobs, pauses
                | Error err ->
                    logger.LogError("Failed to load active sync jobs: {Error}", serviceMessage err)
                    return (Map.empty, Map.empty)
            }

        result |> Async.AwaitTask |> Async.RunSynchronously

    let private mutateJobStatus
        id
        (jobs: Map<int, SyncJobState>)
        (newStatus: JobStatus)
        (scopeFactory: IServiceScopeFactory)
        =
        let jobs = Map.add id { jobs[id] with Status = newStatus } jobs
        persistStatus scopeFactory id newStatus |> ignore
        jobs

    let create (scopeFactory: IServiceScopeFactory) (logger: ILogger) : T =
        let initialJobs, initialPauses = loadAndResume scopeFactory logger

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
                        | StartJob(instrument, marketType, interval, fromDate, toDate, reply) ->
                            let now = DateTime.UtcNow
                            let estimatedMinutes = int (toDate - fromDate).TotalMinutes

                            let dbJob: SyncJob =
                                { Id = 0
                                  Instrument = instrument
                                  MarketType = marketType
                                  Interval = interval
                                  FromDate = fromDate
                                  ToDate = toDate
                                  Status = int SyncJobStatus.Pending
                                  ErrorMessage = null
                                  FetchedCount = 0
                                  EstimatedTotal = estimatedMinutes
                                  CurrentCursor = toDate
                                  StartedAt = now
                                  LastUpdateAt = now
                                  CreatedAt = now }

                            let! createResult =
                                withDb scopeFactory (fun db -> SyncJobRepository.create db dbJob) |> Async.AwaitTask

                            match createResult with
                            | Ok saved ->
                                let id = saved.Id

                                let job: SyncJobState =
                                    { Id = id
                                      Instrument = instrument
                                      MarketType = marketType
                                      Interval = interval
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
                                    instrument
                                    interval
                                    fromDate
                                    toDate
                                    0
                                    cts
                                    pauseEvent

                                reply.Reply id
                                return! loop (Map.add id job jobs) (Map.add id cts ctss) (Map.add id pauseEvent pauses)

                            | Error err ->
                                logger.LogError("Failed to create sync job in DB: {Error}", serviceMessage err)
                                reply.Reply -1
                                return! loop jobs ctss pauses

                        | StopJob(id, reply) ->
                            match Map.tryFind id ctss, Map.tryFind id pauses with
                            | Some cts, Some pause ->
                                pause.Set()
                                cts.Cancel()
                                let jobs = mutateJobStatus id jobs Stopped scopeFactory
                                reply.Reply true
                                return! loop jobs ctss pauses
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | PauseJob(id, reply) ->
                            match Map.tryFind id pauses, Map.tryFind id jobs with
                            | Some pause, Some job when job.Status = Running ->
                                pause.Reset()
                                let jobs = mutateJobStatus id jobs Paused scopeFactory
                                reply.Reply true
                                return! loop jobs ctss pauses
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | ResumeJob(id, reply) ->
                            match Map.tryFind id pauses, Map.tryFind id jobs with
                            | Some pause, Some job when job.Status = Paused ->
                                let hasRunningLoop = Map.containsKey id ctss

                                if hasRunningLoop then
                                    pause.Set()
                                    let jobs = mutateJobStatus id jobs Running scopeFactory
                                    reply.Reply true
                                    return! loop jobs ctss pauses
                                else
                                    let cts = new CancellationTokenSource()
                                    let newPause = new ManualResetEventSlim(true)

                                    runSyncLoop
                                        scopeFactory
                                        logger
                                        inbox.Post
                                        id
                                        job.Instrument
                                        job.Interval
                                        job.FromDate
                                        job.Progress.CurrentTimestamp
                                        job.Progress.FetchedCount
                                        cts
                                        newPause

                                    let jobs = mutateJobStatus id jobs Running scopeFactory
                                    reply.Reply true

                                    return! loop jobs (Map.add id cts ctss) (Map.add id newPause pauses)
                            | _ ->
                                reply.Reply false
                                return! loop jobs ctss pauses

                        | RemoveJob(id, reply) ->
                            match Map.tryFind id ctss, Map.tryFind id pauses with
                            | Some cts, Some pause ->
                                pause.Set()
                                cts.Cancel()
                            | _ -> ()

                            do!
                                withDb scopeFactory (fun db -> SyncJobRepository.delete db id)
                                |> Task.map ignore
                                |> Async.AwaitTask

                            reply.Reply true

                            return! loop (Map.remove id jobs) (Map.remove id ctss) (Map.remove id pauses)

                        | UpdateProgress(id, updater) ->
                            let jobs =
                                match Map.tryFind id jobs with
                                | Some job ->
                                    let updated = updater job

                                    match updated.Status with
                                    | Completed
                                    | Stopped
                                    | Failed _ ->
                                        persistProgress
                                            scopeFactory
                                            logger
                                            id
                                            updated.Progress.FetchedCount
                                            updated.Progress.CurrentTimestamp
                                        |> ignore
                                    | _ -> ()

                                    Map.add id updated jobs
                                | None -> jobs

                            return! loop jobs ctss pauses

                        | GetJobs reply ->
                            let! dbResult =
                                withDb scopeFactory (fun db ct -> SyncJobRepository.getAll db ct) |> Async.AwaitTask

                            let allJobs =
                                match dbResult with
                                | Ok dbJobs ->
                                    dbJobs
                                    |> List.map (fun dbJob ->
                                        match Map.tryFind dbJob.Id jobs with
                                        | Some inMemory -> inMemory
                                        | None -> fromDbJob dbJob
                                    )
                                | Error _ -> jobs |> Map.values |> Seq.toList

                            let sorted = allJobs |> List.sortByDescending _.CreatedAt
                            reply.Reply sorted
                            return! loop jobs ctss pauses
                    }

                loop initialJobs Map.empty initialPauses
            )

        { startJob =
            fun instrument marketType interval fromDate toDate ->
                agent.PostAndReply(fun reply -> StartJob(instrument, marketType, interval, fromDate, toDate, reply))
          stopJob = fun id -> agent.PostAndReply(fun reply -> StopJob(id, reply))
          pauseJob = fun id -> agent.PostAndReply(fun reply -> PauseJob(id, reply))
          resumeJob = fun id -> agent.PostAndReply(fun reply -> ResumeJob(id, reply))
          removeJob = fun id -> agent.PostAndReply(fun reply -> RemoveJob(id, reply))
          getJobs = fun () -> agent.PostAndReply(fun reply -> GetJobs reply) }
