namespace Plutus.Core.Domain

open System

type Candlestick =
    { Instrument: Instrument
      MarketType: MarketType
      Timestamp: DateTime
      Open: PositiveDecimal
      High: PositiveDecimal
      Low: PositiveDecimal
      Close: PositiveDecimal
      Volume: PositiveDecimal
      VolumeQuote: PositiveDecimal
      IsCompleted: bool
      Interval: Interval }

type CandlestickGap = { GapStart: DateTime; GapEnd: DateTime }

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
