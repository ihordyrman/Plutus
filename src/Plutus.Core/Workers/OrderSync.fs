namespace Plutus.Core.Workers

open System
open System.Data
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Repositories

module OrderSync =

    let applyUpdate (order: Order) (update: OrderSyncer.OrderUpdate) =
        { order with
            Status = update.Status
            Fee = update.Fee |> Option.map Nullable |> Option.defaultValue order.Fee
            Price = update.AveragePrice |> Option.map Nullable |> Option.defaultValue order.Price
            Quantity = update.FilledQuantity |> Option.defaultValue order.Quantity
            ExecutedAt = update.ExecutedAt |> Option.map Nullable |> Option.defaultValue order.ExecutedAt
            CancelledAt = update.CancelledAt |> Option.map Nullable |> Option.defaultValue order.CancelledAt
            UpdatedAt = DateTime.UtcNow }

    let hasChanges (original: Order) (updated: Order) =
        original.Status <> updated.Status
        || original.Fee <> updated.Fee
        || original.Price <> updated.Price
        || original.Quantity <> updated.Quantity
        || original.ExecutedAt <> updated.ExecutedAt
        || original.CancelledAt <> updated.CancelledAt

    let syncOrder
        (syncers: OrderSyncer.T list)
        (services: IServiceProvider)
        (logger: ILogger)
        (token: CancellationToken)
        (order: Order)
        =
        task {
            match OrderSyncer.tryFind order.MarketType syncers with
            | None ->
                logger.LogWarning("No syncer for market {MarketType}, order {OrderId}", order.MarketType, order.Id)
                return false
            | Some syncer ->
                match! OrderSyncer.getUpdate order token syncer with
                | Error err ->
                    logger.LogWarning("Failed to fetch order {OrderId}: {Error}", order.Id, err)
                    return false
                | Ok None -> return false
                | Ok(Some update) ->
                    let updated = applyUpdate order update

                    if hasChanges order updated then
                        match!
                            Transaction.execute services (fun db txn -> OrderRepository.update db txn updated token)
                        with
                        | Ok() ->
                            logger.LogInformation(
                                "Order {OrderId} updated: {OldStatus} -> {NewStatus}",
                                order.Id,
                                order.Status,
                                updated.Status
                            )

                            return true
                        | Error err ->
                            logger.LogError("Failed to update order {OrderId}: {Error}", order.Id, err)
                            return false
                    else
                        return false
        }

    let syncAll (scopeFactory: IServiceScopeFactory) (logger: ILogger) (token: CancellationToken) =
        task {
            use scope = scopeFactory.CreateScope()
            let services = scope.ServiceProvider
            use db = services.GetRequiredService<IDbConnection>()
            let syncers = services.GetRequiredService<OrderSyncer.T list>()
            let syncOrder = syncOrder syncers services logger token

            match! OrderRepository.getActive db token with
            | Error err -> logger.LogError("Failed to load active orders: {Error}", err)
            | Ok [] -> ()
            | Ok orders ->
                let mutable updatedCount = 0

                for order in orders do
                    match! syncOrder order with
                    | true -> updatedCount <- updatedCount + 1
                    | false -> ()

                if updatedCount > 0 then
                    logger.LogInformation("Order sync: {Updated}/{Total} orders updated", updatedCount, orders.Length)
        }

type OrderSyncWorker(scopeFactory: IServiceScopeFactory, logger: ILogger<OrderSyncWorker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct) =
        task {
            use timer = new PeriodicTimer(TimeSpan.FromSeconds 10.0)

            while not ct.IsCancellationRequested do
                try
                    let! _ = timer.WaitForNextTickAsync(ct)
                    do! OrderSync.syncAll scopeFactory logger ct
                with ex ->
                    logger.LogCritical(ex, "Error in OrderSyncWorker loop")
        }
