namespace Plutus.MarketData.Entities

open System
open Plutus.MarketData.Domain

[<CLIMutable>]
type internal CandlestickEntity =
    { Instrument: string
      MarketType: int
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
type internal GapEntity =
    { GapStart: DateTime
      GapEnd: DateTime }
