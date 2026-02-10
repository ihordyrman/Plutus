namespace Plutus.Core.Markets.Exchanges.Okx

open System
open System.Globalization
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Abstractions

module OrderSyncer =

    let mapState =
        function
        | "live" -> Some OrderStatus.Placed
        | "partially_filled" -> Some OrderStatus.PartiallyFilled
        | "filled" -> Some OrderStatus.Filled
        | "canceled" -> Some OrderStatus.Cancelled
        | _ -> None

    let parseDecimal (s: string) =
        match Decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    let parseTimestamp (ms: string) =
        match Int64.TryParse(ms) with
        | true, v -> Some(DateTimeOffset.FromUnixTimeMilliseconds(v).UtcDateTime)
        | _ -> None

    let toUpdate (detail: OkxOrderDetail) (status: OrderStatus) : OrderSyncer.OrderUpdate =
        let executedAt =
            match status with
            | OrderStatus.Filled -> parseTimestamp detail.LastFillTime
            | _ -> None

        let cancelledAt =
            match status with
            | OrderStatus.Cancelled -> parseTimestamp detail.UpdateTime
            | _ -> None

        { Status = status
          Fee = parseDecimal detail.Fee |> Option.map abs
          AveragePrice = parseDecimal detail.AveragePrice
          FilledQuantity = parseDecimal detail.AccumulatedFillSize
          ExecutedAt = executedAt
          CancelledAt = cancelledAt }

    let create (http: Http.T) (logger: ILogger) : OrderSyncer.T =
        let getUpdate (order: Order) _ =
            task {
                match! http.getOrder order.Symbol order.ExchangeOrderId with
                | Error err ->
                    logger.LogWarning("Failed to fetch order {OrderId} from OKX: {Error}", order.Id, err)
                    return Ok None
                | Ok details when Array.isEmpty details ->
                    logger.LogWarning("No data returned from OKX for order {OrderId}", order.Id)
                    return Ok None
                | Ok details ->
                    let detail = details[0]

                    match mapState detail.State with
                    | None ->
                        logger.LogWarning("Unknown OKX state '{State}' for order {OrderId}", detail.State, order.Id)
                        return Ok None
                    | Some status -> return Ok(Some(toUpdate detail status))
            }

        OrderSyncer.Okx getUpdate
