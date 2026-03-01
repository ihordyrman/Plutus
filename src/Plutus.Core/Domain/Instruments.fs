namespace Plutus.Core.Domain

open System

[<CLIMutable>]
type ExchangeInstrument =
    { Id: int
      InstrumentId: string
      InstrumentType: string
      BaseCurrency: string
      QuoteCurrency: string
      MarketType: MarketType
      SyncedAt: DateTime
      CreatedAt: DateTime }
