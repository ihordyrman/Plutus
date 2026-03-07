namespace Plutus.Core.Infrastructure

[<CLIMutable>]
type DatabaseSettings =
    { ConnectionString: string }

    static member SectionName = "Database"

[<CLIMutable>]
type MarketConfiguration =
    { MarketType: string; ApiKey: string; SecretKey: string; Passphrase: string; IsSandbox: bool }

[<CLIMutable>]
type MarketConfigurationSettings =
    { Configurations: MarketConfiguration[] }

    static member SectionName = "MarketConfigurations"
