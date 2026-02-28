namespace Plutus.Core.Markets.Exchanges.Okx

open System.Globalization
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Abstractions
open Plutus.Core.Shared

module OrderExecutor =
    open Errors

    let toOkxSide (side: OrderSide) =
        match side with
        | OrderSide.Buy -> "buy"
        | OrderSide.Sell -> "sell"
        | _ -> failwith "unknown side"

    let mapToRequest (order: Order) =
        let isLimitOrder = order.Price.HasValue && order.Price.Value > 0m

        { InstrumentId = string order.Instrument
          TradeMode = "cash"
          Side = toOkxSide order.Side
          OrderType = if isLimitOrder then "limit" else "market"
          Size = order.Quantity.ToString(CultureInfo.InvariantCulture)
          Price = if isLimitOrder then Some(order.Price.Value.ToString(CultureInfo.InvariantCulture)) else None
          ClientOrderId = Some(order.Id.ToString(CultureInfo.InvariantCulture))
          Tag = Some(string order.Id)
          ReduceOnly = None
          TargetCurrency = None }

    let create (http: Http.T) (logger: ILogger) : OrderExecutor.T =
        let executeOrder (order: Order) _ =
            task {
                let request = mapToRequest order

                logger.LogInformation(
                    "Placing {OrderType} {Side} order for {Instrument}: Qty={Quantity}, Price={Price}",
                    request.OrderType,
                    request.Side,
                    request.InstrumentId,
                    request.Size,
                    (match request.Price with
                     | Some p -> p
                     | None -> "None")
                )

                let! result = http.placeOrder request

                match result with
                | Ok [| response |] when response.IsSuccess ->
                    logger.LogInformation("Order placed: {OrderId}", response.OrderId)
                    return Ok response.OrderId
                | Ok [| response |] ->
                    logger.LogError("OKX rejected: {Message}", response.StatusMessage)
                    return Error(ApiError(response.StatusMessage, Some(int response.StatusCode)))
                | Error err -> return Error err
                | _ ->
                    logger.LogError("Unexpected response from OKX when placing order")
                    return Error(ApiError("Unexpected response from OKX", None))
            }

        OrderExecutor.Okx executeOrder
