namespace Plutus.MarketData.RecoverGaps

open System.Data
open Dapper
open Plutus.MarketData
open Plutus.MarketData.Domain
open Plutus.MarketData.Entities
open Plutus.Shared.Errors

module internal Adapter =
    let findGaps (db: IDbConnection) : FindCandlestickGaps =
        fun instrument marketType interval token ->
            task {
                try
                    let! results =
                        db.QueryAsync<GapEntity>(
                            CommandDefinition(
                                """WITH ordered AS (
                                       SELECT timestamp, LEAD(timestamp) OVER (ORDER BY timestamp) AS next_ts
                                       FROM candlesticks
                                       WHERE instrument = @Instrument AND market_type = @MarketType AND interval = @Interval
                                   )
                                   SELECT timestamp + interval '1 minute' AS gap_start,
                                          next_ts  - interval '1 minute' AS gap_end
                                   FROM ordered
                                   WHERE next_ts - timestamp > interval '2 minutes'""",
                                {| Instrument = InstrumentId.value instrument.Id
                                   MarketType = int marketType
                                   Interval = Interval.value interval |},
                                cancellationToken = token
                            )
                        )

                    return Ok(results |> Seq.toList |> List.map Helpers.toGap)
                with ex ->
                    return Error(Unexpected ex)
            }
