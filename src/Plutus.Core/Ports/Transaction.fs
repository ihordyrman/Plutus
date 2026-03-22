namespace Plutus.Core.Ports

open System
open System.Data
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Shared.Errors

module TransactionPort =
    let execute (services: IServiceProvider) (operation: IServiceProvider -> Task<Result<'a, ServiceError>>) =
        task {
            use scope = services.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            if db.State <> ConnectionState.Open then
                db.Open()

            use transaction = db.BeginTransaction()

            try
                let! result = operation scope.ServiceProvider

                match result with
                | Ok value ->
                    transaction.Commit()
                    return Ok value
                | Error err ->
                    transaction.Rollback()
                    return Error err
            with ex ->
                transaction.Rollback()
                return Error(Unexpected ex)
        }
