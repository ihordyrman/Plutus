namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module SyncJobRepository =

    let create (db: IDbConnection) (job: SyncJob) (token: CancellationToken) =
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
                            job,
                            cancellationToken = token
                        )
                    )

                return Ok { job with Id = id }
            with ex ->
                return Error(Unexpected ex)
        }

    let updateStatus
        (db: IDbConnection)
        (id: int)
        (status: SyncJobStatus)
        (errorMessage: string option)
        (token: CancellationToken)
        =
        task {
            try
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE candlestick_sync_jobs
                               SET status = @Status, error_message = @ErrorMessage, last_update_at = @Now
                               WHERE id = @Id""",
                            {| Id = id
                               Status = int status
                               ErrorMessage = (errorMessage |> Option.toObj)
                               Now = DateTime.UtcNow |},
                            cancellationToken = token
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let updateProgress
        (db: IDbConnection)
        (id: int)
        (fetchedCount: int)
        (currentCursor: DateTimeOffset)
        (token: CancellationToken)
        =
        task {
            try
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE candlestick_sync_jobs
                               SET fetched_count = @FetchedCount, current_cursor = @CurrentCursor, last_update_at = @Now
                               WHERE id = @Id""",
                            {| Id = id
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

    let delete (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! _ =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlestick_sync_jobs WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let getActive (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! jobs =
                    db.QueryAsync<SyncJob>(
                        CommandDefinition(
                            "SELECT * FROM candlestick_sync_jobs WHERE status IN (0, 1, 2) ORDER BY created_at DESC",
                            cancellationToken = token
                        )
                    )

                return Ok(jobs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getAll (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! jobs =
                    db.QueryAsync<SyncJob>(
                        CommandDefinition(
                            "SELECT * FROM candlestick_sync_jobs ORDER BY created_at DESC",
                            cancellationToken = token
                        )
                    )

                return Ok(jobs |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }
