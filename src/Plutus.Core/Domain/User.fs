namespace Plutus.Core.Domain

open System

type Username = private Username of string
type PasswordHash = private PasswordHash of string
type UserId = private Id of int

module Username =
    let create (username: string) : Result<Username, string> =
        if String.IsNullOrWhiteSpace(username) then
            Error "Username cannot be empty."
        else
            Ok(Username username)

    let value (Username username) = username

module PasswordHash =
    let create (passwordHash: string) : Result<PasswordHash, string> =
        if String.IsNullOrWhiteSpace(passwordHash) then
            Error "Password hash cannot be empty."
        else
            Ok(PasswordHash passwordHash)

    let value (PasswordHash hash) = hash

module UserId =
    let create (id: int) : Result<UserId, string> =
        if id <= 0 then Error "User ID must be a positive integer." else Ok(Id id)

    let value (Id id) = id

type AuthenticatedUser = { Id: UserId; Username: Username; PasswordHash: PasswordHash }
