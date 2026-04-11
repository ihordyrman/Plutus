namespace Plutus.Identity.Domain

open System

// User

type Username = private Username of string
type PasswordHash = private PasswordHash of string
type UserId = private Id of int

module Username =
    let create (username: string) : Result<Username, string> =
        match username with
        | x when String.IsNullOrWhiteSpace x -> Error "Username cannot be empty."
        | x when x.Length < 3 -> Error "Username must be at least 3 characters long."
        | x when x.Length > 50 -> Error "Username cannot be longer than 50 characters."
        | _ -> Ok(Username username)

    let value (Username username) = username

module PasswordHash =
    let create (passwordHash: string) : Result<PasswordHash, string> =
        match passwordHash with
        | x when String.IsNullOrWhiteSpace x -> Error "Password hash cannot be empty."
        | x when x.Length < 100 -> Error "Password hash must be at least 100 characters long."
        | _ -> Ok(PasswordHash passwordHash)

    let value (PasswordHash hash) = hash

module UserId =
    let create (id: int) : Result<UserId, string> =
        match id with
        | x when x <= 0 -> Error "User ID must be a positive integer."
        | _ -> Ok(Id id)

    let value (Id id) = id

type User =
    { Id: UserId
      Username: Username
      PasswordHash: PasswordHash }

// Api key

type KeyId = private KeyId of int
type KeyName = private KeyName of string
type KeyHash = private KeyHash of string
type KeyPrefix = private KeyPrefix of string

module KeyId =
    let create (id: int) : Result<KeyId, string> =
        match id with
        | x when x <= 0 -> Error "Key ID must be a positive integer."
        | _ -> Ok(KeyId id)

    let value (KeyId id) = id

module KeyName =
    let create (name: string) : Result<KeyName, string> =
        match name with
        | x when String.IsNullOrWhiteSpace x -> Error "Key name cannot be empty."
        | _ -> Ok(KeyName name)

    let value (KeyName name) = name

module KeyHash =
    let create (hash: string) : Result<KeyHash, string> =
        match hash with
        | _ when String.IsNullOrWhiteSpace hash -> Error "Key hash cannot be empty."
        | _ -> Ok(KeyHash hash)

    let value (KeyHash hash) = hash

module KeyPrefix =
    let create (prefix: string) : Result<KeyPrefix, string> =
        match prefix with
        | x when String.IsNullOrWhiteSpace prefix -> Error "Key prefix cannot be empty."
        | _ -> Ok(KeyPrefix prefix)

    let value (KeyPrefix prefix) = prefix

type Key =
    { Id: KeyId
      Name: KeyName
      Hash: KeyHash
      Prefix: KeyPrefix
      IsActive: bool
      LastUsed: DateTime option
      CreatedAt: DateTime }
