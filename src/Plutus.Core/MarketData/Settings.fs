namespace Plutus.Core.MarketData

[<CLIMutable>]
type internal MarketCredentials =
    { MarketType: string
      ApiKey: string
      SecretKey: string
      Passphrase: string
      IsSandbox: bool }

[<CLIMutable>]
type internal MarketSettings =
    { Credentials: MarketCredentials[] }

    static member SectionName = "MarketSettings"
