namespace Plutus.MarketData

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

[<CLIMutable>]
type DatabaseSettings =
    { ConnectionString: string }

    static member SectionName = "Database"


