namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private SyncJobEntity =
    { Id: int
      Instrument: Instrument
      MarketType: MarketType
      Interval: Interval
      FromDate: DateTimeOffset
      ToDate: DateTimeOffset
      Status: int
      ErrorMessage: string
      FetchedCount: int
      EstimatedTotal: int
      CurrentCursor: DateTimeOffset
      StartedAt: DateTime
      LastUpdateAt: DateTime
      CreatedAt: DateTime }

module SyncJobs =
    let private toSyncJob (e: SyncJobEntity) : Result<SyncJob, string> =
        match SyncJobId.create e.Id with
        | Error err -> Error $"Invalid sync job ID: {err}"
        | Ok id ->
            let status = enum<SyncJobStatus> e.Status

            Ok
                { Id = id
                  Instrument = e.Instrument
                  MarketType = e.MarketType
                  Interval = e.Interval
                  FromDate = e.FromDate
                  ToDate = e.ToDate
                  Status = status
                  ErrorMessage = if String.IsNullOrEmpty e.ErrorMessage then None else Some e.ErrorMessage
                  FetchedCount = e.FetchedCount
                  EstimatedTotal = e.EstimatedTotal
                  CurrentCursor = e.CurrentCursor
                  StartedAt = e.StartedAt
                  LastUpdateAt = e.LastUpdateAt
                  CreatedAt = e.CreatedAt }

    let create (db: IDbConnection) : CreateSyncJob =
        fun job token ->
            task {
                try
                    let! id =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                """INSERT INTO candlestick_sync_jobs
                                   (instrument, market_type, interval, from_date, to_date, status, fetched_count,
                                    estimated_total, current_cursor, started_at, last_update_at, created_at)
                                   VALUES (@Instrument, @MarketType, @Interval, @FromDate, @ToDate, @Status, @FetchedCount,
                                           @EstimatedTotal, @CurrentCursor, @StartedAt, @LastUpdateAt, @CreatedAt)
                                   RETURNING id""",
                                {| Instrument = job.Instrument
                                   MarketType = int job.MarketType
                                   Interval = job.Interval
                                   FromDate = job.FromDate
                                   ToDate = job.ToDate
                                   Status = int job.Status
                                   FetchedCount = job.FetchedCount
                                   EstimatedTotal = job.EstimatedTotal
                                   CurrentCursor = job.CurrentCursor
                                   StartedAt = job.StartedAt
                                   LastUpdateAt = job.LastUpdateAt
                                   CreatedAt = job.CreatedAt |},
                                cancellationToken = token
                            )
                        )

                    match SyncJobId.create id with
                    | Ok newId -> return Ok { job with Id = newId }
                    | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let updateStatus (db: IDbConnection) : UpdateSyncJobStatus =
        fun id status errorMessage token ->
            task {
                try
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE candlestick_sync_jobs
                                   SET status = @Status, error_message = @ErrorMessage, last_update_at = @Now
                                   WHERE id = @Id""",
                                {| Id = SyncJobId.value id
                                   Status = int status
                                   ErrorMessage = errorMessage |> Option.toObj
                                   Now = DateTime.UtcNow |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let updateProgress (db: IDbConnection) : UpdateSyncJobProgress =
        fun id fetchedCount currentCursor token ->
            task {
                try
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE candlestick_sync_jobs
                                   SET fetched_count = @FetchedCount, current_cursor = @CurrentCursor, last_update_at = @Now
                                   WHERE id = @Id""",
                                {| Id = SyncJobId.value id
                                   FetchedCount = fetchedCount
                                   CurrentCursor = currentCursor
                                   Now = DateTime.UtcNow |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let delete (db: IDbConnection) : DeleteSyncJob =
        fun id token ->
            task {
                try
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "DELETE FROM candlestick_sync_jobs WHERE id = @Id",
                                {| Id = SyncJobId.value id |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let getActive (db: IDbConnection) : GetActiveSyncJobs =
        fun token ->
            task {
                try
                    let! jobs =
                        db.QueryAsync<SyncJobEntity>(
                            CommandDefinition(
                                "SELECT * FROM candlestick_sync_jobs WHERE status IN (0, 1, 2) ORDER BY created_at DESC",
                                cancellationToken = token
                            )
                        )

                    let results = jobs |> Seq.toList |> List.choose (toSyncJob >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let getAll (db: IDbConnection) : GetAllSyncJobs =
        fun token ->
            task {
                try
                    let! jobs =
                        db.QueryAsync<SyncJobEntity>(
                            CommandDefinition(
                                "SELECT * FROM candlestick_sync_jobs ORDER BY created_at DESC",
                                cancellationToken = token
                            )
                        )

                    let results = jobs |> Seq.toList |> List.choose (toSyncJob >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }
