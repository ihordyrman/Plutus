namespace Plutus.Core.Infrastructure

[<CLIMutable>]
type DatabaseSettings =
    { ConnectionString: string }

    static member SectionName = "Database"

[<CLIMutable>]
type MarketCredentials =
    { MarketType: string; ApiKey: string; SecretKey: string; Passphrase: string; IsSandbox: bool }

[<CLIMutable>]
type MarketSettings =
    { Credentials: MarketCredentials[] }

    static member SectionName = "MarketSettings"
