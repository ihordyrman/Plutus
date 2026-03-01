namespace Plutus.Core.Domain

open System
open Plutus.Core.Shared

[<CLIMutable>]
type Candlestick =
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

type SyncJobStatus =
    | Pending = 0
    | Running = 1
    | Paused = 2
    | Completed = 3
    | Failed = 4
    | Stopped = 5

[<CLIMutable>]
type SyncJob =
    { Id: int
      Instrument: Instrument
      MarketType: MarketType
      Interval: Interval
      FromDate: DateTimeOffset
      ToDate: DateTimeOffset
      Status: int
      ErrorMessage: string
      FetchedCount: int
      EstimatedTotal: int
      CurrentCursor: DateTimeOffset
      StartedAt: DateTime
      LastUpdateAt: DateTime
      CreatedAt: DateTime }
