namespace Plutus.Core.Identity.Entities

open System

[<CLIMutable>]
type internal ApiKey =
    { Id: int
      Name: string
      KeyHash: string
      KeyPrefix: string
      IsActive: bool
      LastUsed: DateTime option
      CreatedAt: DateTime }

[<CLIMutable>]
type internal UserEntity =
    { Id: int
      Username: string
      PasswordHash: string }
