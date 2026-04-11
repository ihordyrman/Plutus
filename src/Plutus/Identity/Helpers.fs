namespace Plutus.Identity

open System
open Plutus.Identity.Entities
open Plutus.Identity.Domain
open Plutus.Shared.Errors

module internal Helpers =
    let toKey (apiKey: ApiKey) : Result<Key, string> =
        match
            KeyId.create apiKey.Id,
            KeyName.create apiKey.Name,
            KeyHash.create apiKey.KeyHash,
            KeyPrefix.create apiKey.KeyPrefix
        with
        | Ok id, Ok name, Ok hash, Ok prefix ->
            Ok
                { Id = id
                  Name = name
                  Hash = hash
                  Prefix = prefix
                  IsActive = apiKey.IsActive
                  LastUsed = apiKey.LastUsed
                  CreatedAt = apiKey.CreatedAt }
        | Error e, _, _, _ -> Error $"Invalid KeyId: {e}"
        | _, Error e, _, _ -> Error $"Invalid KeyName: {e}"
        | _, _, Error e, _ -> Error $"Invalid KeyHash: {e}"
        | _, _, _, Error e -> Error $"Invalid KeyPrefix: {e}"

    let mapApiKey (apiKey: ApiKey) : Result<Key option, ServiceError> =
        match toKey apiKey with
        | Ok key -> Ok(Some key)
        | Error e -> Error(Unexpected(Exception $"Failed to map API key: {e}"))

    let toUser (user: UserEntity) : Result<User, string> =
        match UserId.create user.Id, Username.create user.Username, PasswordHash.create user.PasswordHash with
        | Ok id, Ok username, Ok passwordHash ->
            Ok
                { Id = id
                  Username = username
                  PasswordHash = passwordHash }
        | Error e, _, _ -> Error $"Invalid ID for user {user.Id}: {e}"
        | _, Error e, _ -> Error $"Invalid username for user ID {user.Id}: {e}"
        | _, _, Error e -> Error $"Invalid password hash for user ID {user.Id}: {e}"
