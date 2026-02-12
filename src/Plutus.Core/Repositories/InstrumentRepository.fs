namespace Plutus.Core.Repositories

open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module InstrumentRepository =

    let upsertBatch (db: IDbConnection) (instruments: ExchangeInstrument list) (token: CancellationToken) =
        task {
            if instruments.IsEmpty then
                return Ok 0
            else
                try
                    let! result =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO instruments
                               (instrument_id, instrument_type, base_currency, quote_currency, market_type, synced_at, created_at)
                               VALUES (@InstrumentId, @InstrumentType, @BaseCurrency, @QuoteCurrency, @MarketType, @SyncedAt, @CreatedAt)
                               ON CONFLICT (market_type, instrument_id)
                               DO UPDATE SET instrument_type = @InstrumentType,
                                             base_currency = @BaseCurrency,
                                             quote_currency = @QuoteCurrency,
                                             synced_at = @SyncedAt""",
                                instruments,
                                cancellationToken = token
                            )
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
        }

    let getBaseCurrencies (db: IDbConnection) (marketType: int) (instrumentType: string) (token: CancellationToken) =
        task {
            try
                let! results =
                    db.QueryAsync<string>(
                        CommandDefinition(
                            """SELECT DISTINCT base_currency
                               FROM instruments
                               WHERE market_type = @MarketType AND instrument_type = @InstrumentType
                               ORDER BY base_currency""",
                            {| MarketType = marketType; InstrumentType = instrumentType |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getQuoteCurrencies
        (db: IDbConnection)
        (marketType: int)
        (instrumentType: string)
        (baseCurrency: string)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<string>(
                        CommandDefinition(
                            """SELECT DISTINCT quote_currency
                               FROM instruments
                               WHERE market_type = @MarketType
                                 AND instrument_type = @InstrumentType
                                 AND base_currency = @BaseCurrency
                               ORDER BY quote_currency""",
                            {| MarketType = marketType; InstrumentType = instrumentType; BaseCurrency = baseCurrency |},
                            cancellationToken = token
                        )
                    )

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let exists (db: IDbConnection) (marketType: int) (instrumentId: string) (token: CancellationToken) =
        task {
            try
                let! count =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(1) FROM instruments WHERE market_type = @MarketType AND instrument_id = @InstrumentId",
                            {| MarketType = marketType; InstrumentId = instrumentId |},
                            cancellationToken = token
                        )
                    )

                return Ok(count > 0)
            with ex ->
                return Error(Unexpected ex)
        }
