namespace Plutus.Core.Identity.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Identity.Domain
open Plutus.Core.Shared.Errors

// User ports

type FindUserByUsername = Username -> CancellationToken -> Task<Result<User option, ServiceError>>
type UserExists = CancellationToken -> Task<Result<bool, ServiceError>>
type CreateUser = Username -> PasswordHash -> CancellationToken -> Task<Result<unit, ServiceError>>

type UserPorts =
    { FindByUsername: FindUserByUsername
      UserExists: UserExists
      CreateUser: CreateUser }

// Api key ports

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
