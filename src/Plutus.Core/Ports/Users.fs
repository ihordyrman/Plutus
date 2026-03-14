namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type FindUserByUsername = Username -> CancellationToken -> Task<Result<AuthenticatedUser option, ServiceError>>
type UserExists = CancellationToken -> Task<Result<bool, ServiceError>>
type CreateUser = Username -> PasswordHash -> CancellationToken -> Task<Result<unit, ServiceError>>

type UserPorts = { FindByUsername: FindUserByUsername; UserExists: UserExists; CreateUser: CreateUser }
