namespace Plutus.MarketData.SyncCandlesticks

open System
open System.Data
open Dapper
open FsToolkit.ErrorHandling
open Plutus.MarketData
open Plutus.MarketData.Domain
open Plutus.Shared.Domain
open Plutus.Shared.Errors

[<CLIMutable>]
type private CandlestickEntity =
    { Id: int
      Instrument: Instrument
      MarketType: MarketType
      Timestamp: DateTime
      Open: decimal
      High: decimal
      Low: decimal
      Close: decimal
      Volume: decimal
      VolumeQuote: decimal
      IsCompleted: bool
      Interval: Interval }

[<CLIMutable>]
type private GapEntity =
    { GapStart: DateTime
      GapEnd: DateTime }

module Candlesticks =
    let private toCandlestick (e: CandlestickEntity) : Candlestick =
        result { 

            let! instrument = Instrument.create e.Instrument

            return
                { Instrument = e.Instrument
                    MarketType = e.MarketType
                    Timestamp = e.Timestamp
                    Open = e.Open
                    High = e.High
                    Low = e.Low
                    Close = e.Close
                    Volume = e.Volume
                    VolumeQuote = e.VolumeQuote
                    IsCompleted = e.IsCompleted
                    Interval = e.Interval }
        }

        
    let private toGap (e: GapEntity) : CandlestickGap =
        { GapStart = e.GapStart; GapEnd = e.GapEnd }

    let private toWeeklyCoverage (e: WeeklyCoverageEntity) : WeeklyCoverage =
        { Instrument = e.Instrument; WeekStart = e.WeekStart; Count = e.Count }

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
                                {| Instrument = instrument; MarketType = int marketType; Interval = interval |},
                                cancellationToken = token
                            )
                        )

                    return Ok(results |> Seq.tryHead |> Option.map toCandlestick)
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
                                {| Instrument = instrument; MarketType = int marketType; Interval = interval |},
                                cancellationToken = token
                            )
                        )

                    return Ok(results |> Seq.tryHead |> Option.map toCandlestick)
                with ex ->
                    return Error(Unexpected ex)
            }

    let findGaps (db: IDbConnection) : FindCandlestickGaps =
        fun instrument marketType interval token ->
            task {
                try
                    let! results =
                        db.QueryAsync<GapEntity>(
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

                    return Ok(results |> Seq.toList |> List.map toGap)
                with ex ->
                    return Error(Unexpected ex)
            }

    let query (db: IDbConnection) : QueryCandlesticks =
        fun q token ->
            task {
                try
                    let baseSql =
                        "SELECT * FROM candlesticks WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval"

                    let conditions = ResizeArray<string>()
                    let parameters = DynamicParameters()
                    parameters.Add("Instrument", q.Instrument)
                    parameters.Add("MarketType", int q.MarketType)
                    parameters.Add("Interval", q.Interval)

                    match q.FromDate with
                    | Some from ->
                        conditions.Add("AND timestamp >= @FromDate")
                        parameters.Add("FromDate", from)
                    | None -> ()

                    match q.ToDate with
                    | Some toDate ->
                        conditions.Add("AND timestamp <= @ToDate")
                        parameters.Add("ToDate", toDate)
                    | None -> ()

                    let limitClause =
                        match q.Limit with
                        | Some l -> $"LIMIT {l}"
                        | None -> "LIMIT 1000"

                    let whereClause = String.Join(" ", conditions)
                    let finalSql = $"{baseSql} {whereClause} ORDER BY timestamp DESC {limitClause}"

                    let! results =
                        db.QueryAsync<CandlestickEntity>(CommandDefinition(finalSql, parameters, cancellationToken = token))

                    return Ok(results |> Seq.toList |> List.map toCandlestick)
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
                                {| Instrument = c.Instrument
                                   MarketType = int c.MarketType
                                   Interval = c.Interval
                                   Timestamp = c.Timestamp
                                   Open = c.Open
                                   High = c.High
                                   Low = c.Low
                                   Close = c.Close
                                   Volume = c.Volume
                                   VolumeQuote = c.VolumeQuote
                                   IsCompleted = c.IsCompleted |})

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

