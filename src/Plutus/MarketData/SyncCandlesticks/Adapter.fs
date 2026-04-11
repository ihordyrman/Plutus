namespace Plutus.MarketData.SyncCandlesticks

open System
open System.Data
open Dapper
open FsToolkit.ErrorHandling
open Plutus.MarketData
open Plutus.MarketData.Domain
open Plutus.MarketData.Entities
open Plutus.Shared.Domain
open Plutus.Shared.Errors

module internal Adapter =
    let getLatest (db: IDbConnection) : GetLatestCandlestick =
        fun instrument marketType interval token ->
            task {
                try
                    let! results =
                        db.QueryAsync<CandlestickEntity>(
                            CommandDefinition(
                                """SELECT * FROM candlesticks
                                   WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                                   ORDER BY timestamp DESC LIMIT 1""",
                                {| Instrument = InstrumentId.value instrument.Id
                                   MarketType = int marketType
                                   Interval = Interval.value interval |},
                                cancellationToken = token
                            )
                        )

                    return
                        results
                        |> Seq.tryHead
                        |> Option.map (Helpers.toCandlestick instrument)
                        |> Option.sequenceResult
                        |> Result.mapError (fun e -> Unexpected(Exception e))
                with ex ->
                    return Error(Unexpected ex)
            }

    let getOldest (db: IDbConnection) : GetOldestCandlestick =
        fun instrument marketType interval token ->
            task {
                try
                    let! results =
                        db.QueryAsync<CandlestickEntity>(
                            CommandDefinition(
                                """SELECT * FROM candlesticks
                                   WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                                   ORDER BY timestamp ASC LIMIT 1""",
                                {| Instrument = InstrumentId.value instrument.Id
                                   MarketType = int marketType
                                   Interval = Interval.value interval |},
                                cancellationToken = token
                            )
                        )

                    return
                        results
                        |> Seq.tryHead
                        |> Option.map (Helpers.toCandlestick instrument)
                        |> Option.sequenceResult
                        |> Result.mapError (fun e -> Unexpected(Exception e))
                with ex ->
                    return Error(Unexpected ex)
            }

    let query (db: IDbConnection) : QueryCandlesticks =
        fun q token ->
            task {
                try
                    let parameters = DynamicParameters()
                    parameters.Add("Instrument", InstrumentId.value q.Instrument.Id)
                    parameters.Add("MarketType", int q.MarketType)
                    parameters.Add("Interval", Interval.value q.Interval)
                    q.FromDate |> Option.iter (fun d -> parameters.Add("FromDate", d))
                    q.ToDate |> Option.iter (fun d -> parameters.Add("ToDate", d))

                    let whereClause =
                        [ if q.FromDate.IsSome then
                              "AND timestamp >= @FromDate"
                          if q.ToDate.IsSome then
                              "AND timestamp <= @ToDate" ]
                        |> String.concat " "

                    let limitClause =
                        match q.Limit with
                        | Some l -> $"LIMIT {l}"
                        | None -> "LIMIT 1000"

                    let sql =
                        $"SELECT * FROM candlesticks WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval {whereClause} ORDER BY timestamp DESC {limitClause}"

                    let! results =
                        db.QueryAsync<CandlestickEntity>(CommandDefinition(sql, parameters, cancellationToken = token))

                    return
                        results
                        |> Seq.toList
                        |> List.map (Helpers.toCandlestick q.Instrument)
                        |> List.foldBack (fun result acc ->
                            match result, acc with
                            | Ok c, Ok cs -> Ok(c :: cs)
                            | Error e, _ -> Error(Unexpected(Exception e))
                            | _, Error e -> Error e
                        )
                        <| Ok []
                with ex ->
                    return Error(Unexpected ex)
            }

    let save (db: IDbConnection) : SaveCandlesticks =
        fun candlesticks token ->
            task {
                if candlesticks.IsEmpty then
                    return Ok 0
                else
                    try
                        let parameters =
                            candlesticks
                            |> List.map (fun c ->
                                {| Instrument = InstrumentId.value c.Instrument.Id
                                   MarketType = int c.Instrument.MarketType
                                   Interval = Interval.value c.Interval
                                   Timestamp = c.Timestamp
                                   Open = PositiveDecimal.value c.Open
                                   High = PositiveDecimal.value c.High
                                   Low = PositiveDecimal.value c.Low
                                   Close = PositiveDecimal.value c.Close
                                   Volume = NonNegativeDecimal.value c.Volume
                                   VolumeQuote = NonNegativeDecimal.value c.VolumeQuote
                                   IsCompleted = c.IsCompleted |}
                            )

                        let! result =
                            db.ExecuteAsync(
                                CommandDefinition(
                                    """INSERT INTO candlesticks
                                       (instrument, market_type, interval, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                                       VALUES (@Instrument, @MarketType, @Interval, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                                       ON CONFLICT (instrument, market_type, interval, timestamp)
                                       DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                                     volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted""",
                                    parameters,
                                    cancellationToken = token
                                )
                            )

                        return Ok result
                    with ex ->
                        return Error(Unexpected ex)
            }

    let deleteByInstrument (db: IDbConnection) : DeleteCandlesticksByInstrument =
        fun instrumentId token ->
            task {
                try
                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "DELETE FROM candlesticks WHERE instrument = @Instrument",
                                {| Instrument = InstrumentId.value instrumentId |},
                                cancellationToken = token
                            )
                        )

                    return Ok rowsAffected
                with ex ->
                    return Error(Unexpected ex)
            }
