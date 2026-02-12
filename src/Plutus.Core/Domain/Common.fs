namespace Plutus.Core.Domain

open System

[<CLIMutable>]
type Candlestick =
    { Id: int
      Symbol: string
      MarketType: int
      Timestamp: DateTime
      Open: decimal
      High: decimal
      Low: decimal
      Close: decimal
      Volume: decimal
      VolumeQuote: decimal
      IsCompleted: bool
      Timeframe: string }

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
      Symbol: string
      MarketType: int
      Timeframe: string
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
