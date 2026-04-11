namespace Plutus.MarketData

open FsToolkit.ErrorHandling
open Plutus.MarketData.Domain
open Plutus.MarketData.Entities
open Plutus.Shared.Domain

module internal Helpers =
    let toCandlestick (instrument: Instrument) (e: CandlestickEntity) : Result<Candlestick, string> =
        result {
            let! open' = PositiveDecimal.create e.Open
            let! high = PositiveDecimal.create e.High
            let! low = PositiveDecimal.create e.Low
            let! close = PositiveDecimal.create e.Close
            let! volume = NonNegativeDecimal.create e.Volume
            let! volumeQuote = NonNegativeDecimal.create e.VolumeQuote

            return
                { Instrument = instrument
                  Timestamp = e.Timestamp
                  Open = open'
                  High = high
                  Low = low
                  Close = close
                  Volume = volume
                  VolumeQuote = volumeQuote
                  IsCompleted = e.IsCompleted
                  Interval = e.Interval }
        }

    let toGap (e: GapEntity) : CandlestickGap = { GapStart = e.GapStart; GapEnd = e.GapEnd }
