namespace Plutus.Core.Infrastructure

[<CLIMutable>]
type DatabaseSettings =
    { ConnectionString: string }

    static member SectionName = "Database"

[<CLIMutable>]
type MarketCredentials = { MarketType: string; ApiKey: string; SecretKey: string; Passphrase: string; IsSandbox: bool }

[<CLIMutable>]
type MarketCredentialsSettings =
    { Credentials: MarketCredentials[] }

    static member SectionName = "MarketCredentials"
