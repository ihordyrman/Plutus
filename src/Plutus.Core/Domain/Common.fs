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


