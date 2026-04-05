namespace Plutus.Core.Infrastructure

open System
open System.Data
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection

module Transaction =
    let execute (services: IServiceProvider) (operation: IDbConnection -> IDbTransaction -> Task<Result<'a, 'e>>) =
        task {
            use scope = services.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            if db.State <> ConnectionState.Open then
                db.Open()

            use transaction = db.BeginTransaction()
            let! result = operation db transaction

            match result with
            | Ok value ->
                transaction.Commit()
                return Ok value
            | Error err ->
                transaction.Rollback()
                return Error err
        }
