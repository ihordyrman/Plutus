namespace Plutus.Core.MarketData.Domain

open System
open Plutus.Core.Shared.Domain

type Interval =
    | OneMinute
    | FiveMinutes
    | FifteenMinutes
    | ThirtyMinutes
    | OneHour
    | FourHours
    | OneDay

type InstrumentType =
    | Spot
    | Future
    | Option
    | Swap
    | Perpetual

type InstrumentId = private InstrumentId of string
type Currency = private Currency of string

module Interval =
    let create (interval: string) : Result<Interval, string> =
        match interval with
        | "1m" -> Ok OneMinute
        | "5m" -> Ok FiveMinutes
        | "15m" -> Ok FifteenMinutes
        | "30m" -> Ok ThirtyMinutes
        | "1h" -> Ok OneHour
        | "4h" -> Ok FourHours
        | "1d" -> Ok OneDay
        | _ -> Error $"Invalid interval: {interval}"

    let value (interval: Interval) =
        match interval with
        | OneMinute -> "1m"
        | FiveMinutes -> "5m"
        | FifteenMinutes -> "15m"
        | ThirtyMinutes -> "30m"
        | OneHour -> "1h"
        | FourHours -> "4h"
        | OneDay -> "1d"

module InstrumentType =
    let create (instrumentType: string) : Result<InstrumentType, string> =
        match instrumentType with
        | "SPOT" -> Ok Spot
        | "FUTURE" -> Ok Future
        | "OPTION" -> Ok Option
        | "SWAP" -> Ok Swap
        | "PERPETUAL" -> Ok Perpetual
        | _ -> Error $"Invalid instrument type: {instrumentType}"

    let value (instrumentType: InstrumentType) =
        match instrumentType with
        | Spot -> "SPOT"
        | Future -> "FUTURE"
        | Option -> "OPTION"
        | Swap -> "SWAP"
        | Perpetual -> "PERPETUAL"

module InstrumentId =
    let create (id: string) : Result<InstrumentId, string> =
        match id with
        | x when String.IsNullOrWhiteSpace x -> Error "Instrument ID cannot be empty."
        | _ -> Ok(InstrumentId id)

    let value (InstrumentId id) = id

module Currency =
    let create (currency: string) : Result<Currency, string> =
        match currency with
        | x when String.IsNullOrWhiteSpace x -> Error "Currency cannot be empty."
        | _ -> Ok(Currency currency)

    let value (Currency currency) = currency

module MarketType =
    let create (marketType: int) : Result<MarketType, string> =
        match marketType with
        | 0 -> Ok MarketType.Okx
        | 1 -> Ok MarketType.Binance
        | 2 -> Ok MarketType.IBKR
        | _ -> Error $"Invalid market type: {marketType}"

    let value (marketType: MarketType) = int marketType


type Pair = { Base: Currency; Quote: Currency }

module Pair =
    let create (baseCcy: string) (quoteCcy: string) : Result<Pair, string> =
        match Currency.create baseCcy, Currency.create quoteCcy with
        | Ok baseCurrency, Ok quoteCurrency ->
            Ok
                { Base = baseCurrency
                  Quote = quoteCurrency }
        | Error e, _ -> Error $"Invalid base currency: {e}"
        | _, Error e -> Error $"Invalid quote currency: {e}"

    let value (pair: Pair) =
        Currency.value pair.Base, Currency.value pair.Quote

    let toString (pair: Pair) =
        $"{Currency.value pair.Base}-{Currency.value pair.Quote}"

type Instrument =
    { Id: InstrumentId
      Type: InstrumentType
      Pair: Pair
      MarketType: MarketType }


// Candlesticks
type Candlestick =
    { Instrument: Instrument
      Timestamp: DateTime
      Open: PositiveDecimal
      High: PositiveDecimal
      Low: PositiveDecimal
      Close: PositiveDecimal
      Volume: NonNegativeDecimal
      VolumeQuote: NonNegativeDecimal
      IsCompleted: bool
      Interval: Interval }

type WeeklyCoverage =
    { Instrument: Instrument
      WeekStart: DateTime
      Count: PositiveInt }

type SyncJobStatus =
    | Pending = 0
    | Running = 1
    | Paused = 2
    | Completed = 3
    | Failed = 4
    | Stopped = 5

type SyncJobId = private SyncJobId of int

module SyncJobId =
    let create (id: int) : Result<SyncJobId, string> =
        if id <= 0 then
            Error "SyncJob ID must be a positive integer."
        else
            Ok(SyncJobId id)

    let value (SyncJobId id) = id

type SyncJob =
    { Id: SyncJobId
      Instrument: Instrument
      MarketType: MarketType
      Interval: Interval
      FromDate: DateTimeOffset
      ToDate: DateTimeOffset
      Status: SyncJobStatus
      ErrorMessage: string option
      FetchedCount: PositiveInt
      EstimatedTotal: PositiveInt
      CurrentCursor: DateTimeOffset
      StartedAt: DateTime
      LastUpdateAt: DateTime
      CreatedAt: DateTime }
