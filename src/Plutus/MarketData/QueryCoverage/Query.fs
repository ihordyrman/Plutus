namespace Plutus.MarketData.QueryCoverage

open System
open System.Data
open Dapper
open FsToolkit.ErrorHandling
open Plutus.MarketData
open Plutus.MarketData.Domain
open Plutus.Shared.Domain
open Plutus.Shared.Errors

[<CLIMutable>]
type private WeeklyCoverageEntity =
    { InstrumentId: string
      InstrumentType: string
      BaseCurrency: string
      QuoteCurrency: string
      MarketType: int
      WeekStart: DateTime
      Count: int }

module internal Adapter =
    let private toWeeklyCoverage (e: WeeklyCoverageEntity) : Result<WeeklyCoverage, string> =
        result {
            let! id = InstrumentId.create e.InstrumentId
            let! instrType = InstrumentType.create e.InstrumentType
            let! pair = Pair.create e.BaseCurrency e.QuoteCurrency
            let! mktType = MarketType.create e.MarketType
            let! count = PositiveInt.create e.Count

            return
                { Instrument =
                    { Id = id
                      Type = instrType
                      Pair = pair
                      MarketType = mktType }
                  WeekStart = e.WeekStart
                  Count = count }
        }

    let private foldCoverageResults results =
        results
        |> Seq.toList
        |> List.map toWeeklyCoverage
        |> List.foldBack (fun result acc ->
            match result, acc with
            | Ok item, Ok items -> Ok(item :: items)
            | Error e, _ -> Error(Unexpected(Exception e))
            | _, Error e -> Error e
        )
        <| Ok []

    let getDistinctIntervals (db: IDbConnection) : GetDistinctIntervals =
        fun token ->
            task {
                try
                    let! results =
                        db.QueryAsync<string>(
                            CommandDefinition(
                                "SELECT DISTINCT interval FROM candlesticks ORDER BY interval",
                                cancellationToken = token
                            )
                        )

                    return results |> Seq.toList |> List.choose (Interval.create >> Result.toOption) |> Ok
                with ex ->
                    return Error(Unexpected ex)
            }

    let getDistinctInstrumentCount (db: IDbConnection) : GetDistinctInstrumentCount =
        fun interval token ->
            task {
                try
                    let! count =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                "SELECT COUNT(DISTINCT instrument) FROM candlesticks WHERE interval = @Interval",
                                {| Interval = Interval.value interval |},
                                cancellationToken = token
                            )
                        )

                    return Ok count
                with ex ->
                    return Error(Unexpected ex)
            }

    let getWeeklyCoveragePaged (db: IDbConnection) : GetWeeklyCoveragePaged =
        fun interval offset limit token ->
            task {
                try
                    let! results =
                        db.QueryAsync<WeeklyCoverageEntity>(
                            CommandDefinition(
                                """WITH paged_instruments AS (
                                       SELECT DISTINCT c.instrument, c.market_type
                                       FROM candlesticks c
                                       WHERE c.interval = @Interval
                                       ORDER BY c.instrument
                                       LIMIT @Limit OFFSET @Offset
                                   )
                                   SELECT i.instrument_id, i.instrument_type, i.base_currency, i.quote_currency, i.market_type,
                                          date_trunc('week', c.timestamp) AS week_start, COUNT(*) AS count
                                   FROM candlesticks c
                                   INNER JOIN paged_instruments p ON c.instrument = p.instrument AND c.market_type = p.market_type
                                   INNER JOIN instruments i ON c.instrument = i.instrument_id AND c.market_type = i.market_type
                                   WHERE c.interval = @Interval
                                   GROUP BY i.instrument_id, i.instrument_type, i.base_currency, i.quote_currency, i.market_type,
                                            date_trunc('week', c.timestamp)
                                   ORDER BY i.instrument_id, week_start""",
                                {| Interval = Interval.value interval
                                   Limit = limit
                                   Offset = offset |},
                                cancellationToken = token
                            )
                        )

                    return results |> foldCoverageResults
                with ex ->
                    return Error(Unexpected ex)
            }

    let getWeeklyCoverage (db: IDbConnection) : GetWeeklyCoverage =
        fun interval token ->
            task {
                try
                    let! results =
                        db.QueryAsync<WeeklyCoverageEntity>(
                            CommandDefinition(
                                """SELECT i.instrument_id, i.instrument_type, i.base_currency, i.quote_currency, i.market_type,
                                          date_trunc('week', c.timestamp) AS week_start, COUNT(*) AS count
                                   FROM candlesticks c
                                   INNER JOIN instruments i ON c.instrument = i.instrument_id AND c.market_type = i.market_type
                                   WHERE c.interval = @Interval
                                   GROUP BY i.instrument_id, i.instrument_type, i.base_currency, i.quote_currency, i.market_type,
                                            date_trunc('week', c.timestamp)
                                   ORDER BY i.instrument_id, week_start""",
                                {| Interval = Interval.value interval |},
                                cancellationToken = token
                            )
                        )

                    return results |> foldCoverageResults
                with ex ->
                    return Error(Unexpected ex)
            }
