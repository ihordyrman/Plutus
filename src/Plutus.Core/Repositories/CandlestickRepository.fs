namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type Gap = { GapStart: DateTime; GapEnd: DateTime }

[<CLIMutable>]
type WeeklyCoverage = { Instrument: Instrument; WeekStart: DateTime; Count: int }

[<RequireQualifiedAccess>]
module CandlestickRepository =

    let getLatest
        (db: IDbConnection)
        (instrument: Instrument)
        (marketType: MarketType)
        (interval: Interval)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            """SELECT * FROM candlesticks
                           WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                           ORDER BY timestamp DESC
                           LIMIT 1""",
                            {| Instrument = instrument; MarketType = int marketType; Interval = interval |},
                            cancellationToken = token
                        )
                    )

                match results |> Seq.tryHead with
                | Some entity -> return Ok(Some entity)
                | None -> return Ok None
            with ex ->
                return Error(Unexpected ex)
        }

    let getOldest
        (db: IDbConnection)
        (instrument: Instrument)
        (marketType: MarketType)
        (interval: Interval)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            """SELECT * FROM candlesticks
                           WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                           ORDER BY timestamp ASC
                           LIMIT 1""",
                            {| Instrument = instrument; MarketType = int marketType; Interval = interval |},
                            cancellationToken = token
                        )
                    )

                match results |> Seq.tryHead with
                | Some entity -> return Ok(Some entity)
                | None -> return Ok None
            with ex ->
                return Error(Unexpected ex)
        }

    let findGaps
        (db: IDbConnection)
        (instrument: Instrument)
        (marketType: MarketType)
        (interval: Interval)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<Gap>(
                        CommandDefinition(
                            """WITH ordered AS (
                                SELECT timestamp, LEAD(timestamp) OVER (ORDER BY timestamp) as next_ts
                                FROM candlesticks
                                WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                            )
                            SELECT timestamp + interval '1 minute' as gap_start,
                                   next_ts - interval '1 minute' as gap_end
                            FROM ordered
                            WHERE next_ts - timestamp > interval '2 minutes'""",
                            {| Instrument = instrument; MarketType = int marketType; Interval = interval |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let query
        (db: IDbConnection)
        (instrument: Instrument)
        (marketType: MarketType)
        (interval: Interval)
        (fromDate: DateTime option)
        (toDate: DateTime option)
        (limit: int option)
        (token: CancellationToken)
        =
        task {
            try
                let baseSql =
                    "SELECT * FROM candlesticks WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval"

                let conditions = ResizeArray<string>()
                let parameters = DynamicParameters()
                parameters.Add("Instrument", instrument)
                parameters.Add("MarketType", int marketType)
                parameters.Add("Interval", interval)

                match fromDate with
                | Some from ->
                    conditions.Add("AND timestamp >= @FromDate")
                    parameters.Add("FromDate", from)
                | None -> ()

                match toDate with
                | Some to' ->
                    conditions.Add("AND timestamp <= @ToDate")
                    parameters.Add("ToDate", to')
                | None -> ()

                let limitClause =
                    match limit with
                    | Some l -> $"LIMIT {l}"
                    | None -> "LIMIT 1000"

                let whereClause = String.Join(" ", conditions)
                let finalSql = $"{baseSql} {whereClause} ORDER BY timestamp DESC {limitClause}"

                let! results =
                    db.QueryAsync<Candlestick>(CommandDefinition(finalSql, parameters, cancellationToken = token))

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let save (db: IDbConnection) (candlesticks: Candlestick list) (token: CancellationToken) =
        task {
            if candlesticks.IsEmpty then
                return Ok 0
            else
                try
                    let! result =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO candlesticks
                               (instrument, market_type, interval, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                               VALUES (@Instrument, @MarketType, @Interval, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                               ON CONFLICT (instrument, market_type, interval, timestamp)
                               DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                             volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted""",
                                candlesticks,
                                cancellationToken = token
                            )
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
        }

    let deleteAllByInstrument (db: IDbConnection) (instrument: string) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlesticks WHERE instrument = @Instrument",
                            {| Instrument = instrument |},
                            cancellationToken = token
                        )
                    )

                return Ok rowsAffected
            with ex ->
                return Error(Unexpected ex)
        }

    let getDistinctIntervals (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<string>(
                        CommandDefinition(
                            "SELECT DISTINCT interval FROM candlesticks ORDER BY interval",
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getDistinctInstrumentCount (db: IDbConnection) (interval: Interval) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(DISTINCT instrument) FROM candlesticks WHERE interval = @Interval",
                            {| Interval = interval |},
                            cancellationToken = token
                        )
                    )

                return Ok result
            with ex ->
                return Error(Unexpected ex)
        }

    let getWeeklyCoveragePaged
        (db: IDbConnection)
        (interval: Interval)
        (offset: int)
        (limit: int)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<WeeklyCoverage>(
                        CommandDefinition(
                            """WITH instruments AS (
                                   SELECT DISTINCT instrument FROM candlesticks
                                   WHERE interval = @Interval
                                   ORDER BY instrument
                                   LIMIT @Limit OFFSET @Offset
                               )
                               SELECT c.instrument, date_trunc('week', c.timestamp) as week_start, COUNT(*) as count
                               FROM candlesticks c
                               INNER JOIN instruments s ON c.instrument = s.instrument
                               WHERE c.interval = @Interval
                               GROUP BY c.instrument, date_trunc('week', c.timestamp)
                               ORDER BY c.instrument, week_start""",
                            {| Interval = interval; Limit = limit; Offset = offset |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getWeeklyCoverage (db: IDbConnection) (interval: Interval) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<WeeklyCoverage>(
                        CommandDefinition(
                            """SELECT instrument, date_trunc('week', timestamp) as week_start, COUNT(*) as count
                               FROM candlesticks
                               WHERE interval = @Interval
                               GROUP BY instrument, date_trunc('week', timestamp)
                               ORDER BY instrument, week_start""",
                            {| Interval = interval |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }
