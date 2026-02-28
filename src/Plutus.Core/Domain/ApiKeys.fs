namespace Plutus.Core.Domain

open System

[<CLIMutable>]
type ApiKey =
    { Id: int
      Name: string
      KeyHash: string
      KeyPrefix: string
      IsActive: bool
      LastUsed: DateTime option
      CreatedAt: DateTime }
