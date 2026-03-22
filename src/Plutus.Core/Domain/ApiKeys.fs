namespace Plutus.Core.Domain

open System

type Key =
    { Id: Identifier
      Name: NonEmptyString
      Hash: NonEmptyString
      Prefix: NonEmptyString
      IsActive: bool
      LastUsed: DateTime option
      CreatedAt: DateTime }
