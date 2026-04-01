namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

module ApiKey =
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
        | Error e -> Error(Unexpected(Exception($"Failed to map API key: {e}")))

    let getByHash (db: IDbConnection) : GetByHash =
        fun hash token ->
            task {
                try
                    let hash = KeyHash.value hash

                    let! keys =
                        db.QueryAsync<ApiKey>(
                            CommandDefinition(
                                "SELECT * FROM api_keys WHERE key_hash = @Hash AND is_active = true",
                                {| Hash = hash |},
                                cancellationToken = token
                            )
                        )

                    match keys |> Seq.tryHead with
                    | None -> return Ok None
                    | Some key -> return (mapApiKey key)
                with ex ->
                    return Error(Unexpected ex)
            }


    let create (db: IDbConnection) : Create =
        fun name hash prefix token ->
            task {
                try
                    let name = KeyName.value name
                    let hash = KeyHash.value hash
                    let prefix = KeyPrefix.value prefix

                    let! key =
                        db.QuerySingleAsync<ApiKey>(
                            CommandDefinition(
                                """INSERT INTO api_keys (name, key_hash, key_prefix, created_at)
                                     VALUES (@Name, @KeyHash, @KeyPrefix, now())
                                     RETURNING *""",
                                {| Name = name
                                   KeyHash = hash
                                   KeyPrefix = prefix |},
                                cancellationToken = token
                            )
                        )

                    match toKey key with
                    | Ok k -> return Ok k
                    | Error e -> return Error(Unexpected(Exception($"Failed to map API key: {e}")))
                with ex ->
                    return Error(Unexpected ex)
            }

    let deactivate (db: IDbConnection) : Deactivate =
        fun id token ->
            task {
                try
                    let id = KeyId.value id

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE api_keys SET is_active = false WHERE id = @Id",
                                {| Id = id |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then
                        return Ok()
                    else
                        return Error(NotFound $"API key with id {id} not found")
                with ex ->
                    return Error(Unexpected ex)
            }

    let updateLastUsed (db: IDbConnection) : UpdateLastUsed =
        fun id token ->
            task {
                try
                    let now = DateTime.UtcNow
                    let keyId = KeyId.value id

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "UPDATE api_keys SET last_used = @Now WHERE id = @Id",
                                {| Now = now; Id = keyId |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }
