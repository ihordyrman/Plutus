namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private ExchangeInstrument =
    { Id: int
      InstrumentId: string
      InstrumentType: string
      BaseCurrency: string
      QuoteCurrency: string
      MarketType: MarketType
      SyncedAt: DateTime
      CreatedAt: DateTime }

module Instruments =
    let private toInstrument (instrument: ExchangeInstrument) : Result<Instrument, string> =
        match
            InstrumentId.create instrument.InstrumentId,
            InstrumentType.create instrument.InstrumentType,
            Pair.create instrument.BaseCurrency instrument.QuoteCurrency
        with
        | Ok id, Ok instrumentType, Ok pair ->
            Ok
                { Id = id
                  Type = instrumentType
                  Pair = pair
                  MarketType = instrument.MarketType }

        | Error e, _, _ -> Error $"Invalid InstrumentId: {e}"
        | _, Error e, _ -> Error $"Invalid InstrumentType: {e}"
        | _, _, Error e -> Error $"Invalid currency pair: {e}"

    let private toEntity (instrument: Instrument) : ExchangeInstrument =
        { Id = 0
          InstrumentId = InstrumentId.value instrument.Id
          InstrumentType = InstrumentType.value instrument.Type
          BaseCurrency = Pair.value instrument.Pair |> fst
          QuoteCurrency = Pair.value instrument.Pair |> snd
          MarketType = instrument.MarketType
          SyncedAt = DateTime.UtcNow
          CreatedAt = DateTime.UtcNow }

    let private foldResult map acc result =
        Seq.map map result
        |> Seq.toList
        |> List.foldBack (fun result acc ->
            match result, acc with
            | Ok item, Ok items -> Ok(item :: items)
            | Error e, _ -> Error(Unexpected(Exception(e)))
            | _, Error e -> Error e
        )
        <| acc

    let upsertBatch (db: IDbConnection) : UpsertBatch =
        fun instruments token ->
            task {
                if instruments.IsEmpty then
                    return Ok()
                else
                    try
                        let! _ =
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
                                    (instruments |> List.map toEntity),
                                    cancellationToken = token
                                )
                            )

                        return Ok()
                    with ex ->
                        return Error(Unexpected ex)
            }

    let getBaseCurrencies (db: IDbConnection) : GetBaseCurrency =
        fun marketType instrumentType token ->
            task {
                try
                    let! results =
                        db.QueryAsync<string>(
                            CommandDefinition(
                                """SELECT DISTINCT base_currency
                               FROM instruments
                               WHERE market_type = @MarketType AND instrument_type = @InstrumentType
                               ORDER BY base_currency""",
                                {| MarketType = marketType
                                   InstrumentType = instrumentType |},
                                cancellationToken = token
                            )
                        )

                    return results |> foldResult Currency.create (Ok [])
                with ex ->
                    return Error(Unexpected ex)
            }

    let getQuoteCurrencies (db: IDbConnection) : GetQuoteCurrency =
        fun marketType instrumentType currency token ->
            task {
                try
                    let instrumentType = InstrumentType.value instrumentType
                    let baseCurrency = Currency.value currency

                    let! results =
                        db.QueryAsync<string>(
                            CommandDefinition(
                                """SELECT DISTINCT quote_currency
                                       FROM instruments
                                       WHERE market_type = @MarketType
                                         AND instrument_type = @InstrumentType
                                         AND base_currency = @BaseCurrency
                                       ORDER BY quote_currency""",
                                {| MarketType = marketType
                                   InstrumentType = instrumentType
                                   BaseCurrency = baseCurrency |},
                                cancellationToken = token
                            )
                        )

                    return results |> foldResult Currency.create (Ok [])
                with ex ->
                    return Error(Unexpected ex)
            }
