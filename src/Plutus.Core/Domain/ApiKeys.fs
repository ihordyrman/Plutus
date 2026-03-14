namespace Plutus.Core.Domain

open System

type KeyId = private KeyId of int
type KeyName = private KeyName of string
type KeyHash = private KeyHash of string
type KeyPrefix = private KeyPrefix of string

module KeyId =
    let create (id: int) : Result<KeyId, string> =
        if id <= 0 then Error "Key ID must be a positive integer." else Ok(KeyId id)

    let value (KeyId id) = id

module KeyName =
    let create (name: string) : Result<KeyName, string> =
        if String.IsNullOrWhiteSpace name then Error "Key name cannot be empty." else Ok(KeyName name)

    let value (KeyName name) = name

module KeyHash =
    let create (hash: string) : Result<KeyHash, string> =
        if String.IsNullOrWhiteSpace hash then Error "Key hash cannot be empty." else Ok(KeyHash hash)

    let value (KeyHash hash) = hash

module KeyPrefix =
    let create (prefix: string) : Result<KeyPrefix, string> =
        if String.IsNullOrWhiteSpace prefix then
            Error "Key prefix cannot be empty."
        else
            Ok(KeyPrefix prefix)

    let value (KeyPrefix prefix) = prefix

type Key =
    { Id: KeyId
      Name: KeyName
      Hash: KeyHash
      Prefix: KeyPrefix
      IsActive: bool
      LastUsed: DateTime option
      CreatedAt: DateTime }
