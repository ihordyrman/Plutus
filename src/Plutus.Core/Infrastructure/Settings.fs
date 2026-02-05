namespace Plutus.Core.Infrastructure

[<CLIMutable>]
type DatabaseSettings =
    { ConnectionString: string }

    static member SectionName = "Database"
    static member Default = { ConnectionString = "" }
