namespace Plutus.Identity.CreateUser

type CreateUserRequest = 
    {
        Username: string
        Password: string
    }

module Endpoint =
    open System
    open Plutus.Identity.Domain
    open Plutus.Identity.Ports
    open Plutus.Shared.Errors

    let createUserEndpoint (ports: UserPorts) : CreateUserRequest -> Async<Result<unit, ServiceError>> =
        fun request ->
            async {
                let usernameResult = Username.create request.Username
                let passwordHashResult = PasswordHash.create request.Password

                match usernameResult, passwordHashResult with
                | Ok username, Ok passwordHash ->
                    let! result = ports.CreateUser username passwordHash Async.CancellationToken.None |> Async.AwaitTask
                    return result
                | Error e, _ -> return Error e
                | _, Error e -> return Error e
            }
