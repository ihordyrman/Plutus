namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type GetByHash = KeyHash -> CancellationToken -> Task<Result<Key option, ServiceError>>
type GetAll = CancellationToken -> Task<Result<Key list, ServiceError>>
type Create = KeyName -> KeyHash -> KeyPrefix -> CancellationToken -> Task<Result<Key, ServiceError>>
type Deactivate = KeyId -> CancellationToken -> Task<Result<unit, ServiceError>>
type UpdateLastUsed = KeyId -> CancellationToken -> Task<Result<unit, ServiceError>>

type KeyPorts =
    { GetByHash: GetByHash
      GetAll: GetAll
      Create: Create
      Deactivate: Deactivate
      UpdateLastUsed: UpdateLastUsed }
