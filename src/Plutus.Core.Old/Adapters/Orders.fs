namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private OrderEntity =
    { Id: int
      PipelineId: int option
      MarketType: MarketType
      ExchangeOrderId: string
      Instrument: Instrument
      Side: OrderSide
      Status: OrderStatus
      Quantity: decimal
      Price: decimal option
      StopPrice: decimal option
      Fee: decimal option
      PlacedAt: DateTime option
      ExecutedAt: DateTime option
      CancelledAt: DateTime option
      TakeProfit: decimal option
      StopLoss: decimal option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module Orders =
    let private toOrder (e: OrderEntity) : Result<Order, string> =
        match OrderId.create e.Id with
        | Error err -> Error $"Invalid order ID: {err}"
        | Ok orderId ->
            let pipelineId =
                e.PipelineId
                |> Option.bind (fun pid -> PipelineId.create pid |> Result.toOption)

            let exchangeOrderId =
                if String.IsNullOrEmpty e.ExchangeOrderId then None else Some e.ExchangeOrderId

            Ok
                { Id = orderId
                  PipelineId = pipelineId
                  MarketType = e.MarketType
                  ExchangeOrderId = exchangeOrderId
                  Instrument = e.Instrument
                  Side = e.Side
                  Status = e.Status
                  Quantity = e.Quantity
                  Price = e.Price
                  StopPrice = e.StopPrice
                  Fee = e.Fee
                  PlacedAt = e.PlacedAt
                  ExecutedAt = e.ExecutedAt
                  CancelledAt = e.CancelledAt
                  TakeProfit = e.TakeProfit
                  StopLoss = e.StopLoss
                  CreatedAt = e.CreatedAt
                  UpdatedAt = e.UpdatedAt }

    let getActive (db: IDbConnection) : GetActiveOrders =
        fun token ->
            task {
                try
                    let! orders =
                        db.QueryAsync<OrderEntity>(
                            CommandDefinition(
                                """SELECT * FROM orders
                                   WHERE status IN (@Placed, @PartiallyFilled)
                                   AND exchange_order_id IS NOT NULL
                                   AND exchange_order_id <> ''
                                   ORDER BY created_at ASC""",
                                {| Placed = int OrderStatus.Placed; PartiallyFilled = int OrderStatus.PartiallyFilled |},
                                cancellationToken = token
                            )
                        )

                    let results = orders |> Seq.toList |> List.choose (toOrder >> Result.toOption)
                    return Ok results
                with ex ->
                    return Error(Unexpected ex)
            }

    let create (db: IDbConnection) : CreateOrder =
        fun request token ->
            task {
                try
                    let! entity =
                        db.QuerySingleAsync<OrderEntity>(
                            CommandDefinition(
                                """INSERT INTO orders
                                   (pipeline_id, market_type, exchange_order_id, instrument, side, status, quantity, price, created_at, updated_at)
                                   VALUES (@PipelineId, @MarketType, @ExchangeOrderId, @Instrument, @Side, @Status, @Quantity, @Price, now(), now())
                                   RETURNING *""",
                                {| PipelineId = PipelineId.value request.PipelineId
                                   MarketType = int request.MarketType
                                   ExchangeOrderId = ""
                                   Instrument = request.Instrument
                                   Side = int request.Side
                                   Status = int OrderStatus.Pending
                                   Quantity = PositiveDecimal.value request.Quantity
                                   Price = PositiveDecimal.value request.Price |},
                                cancellationToken = token
                            )
                        )

                    match toOrder entity with
                    | Ok order -> return Ok order
                    | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let update (db: IDbConnection) : UpdateOrder =
        fun order token ->
            task {
                try
                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE orders
                                   SET status = @Status, quantity = @Quantity, price = @Price, stop_price = @StopPrice,
                                       take_profit = @TakeProfit, stop_loss = @StopLoss, exchange_order_id = @ExchangeOrderId,
                                       placed_at = @PlacedAt, executed_at = @ExecutedAt, cancelled_at = @CancelledAt,
                                       fee = @Fee, updated_at = @UpdatedAt
                                   WHERE id = @Id""",
                                {| Id = OrderId.value order.Id
                                   Status = int order.Status
                                   Quantity = order.Quantity
                                   Price = order.Price
                                   StopPrice = order.StopPrice
                                   TakeProfit = order.TakeProfit
                                   StopLoss = order.StopLoss
                                   ExchangeOrderId = order.ExchangeOrderId |> Option.defaultValue ""
                                   PlacedAt = order.PlacedAt
                                   ExecutedAt = order.ExecutedAt
                                   CancelledAt = order.CancelledAt
                                   Fee = order.Fee
                                   UpdatedAt = order.UpdatedAt |},
                                cancellationToken = token
                            )
                        )

                    return Ok()
                with ex ->
                    return Error(Unexpected ex)
            }

    let search (db: IDbConnection) : SearchOrders =
        fun filters skip take token ->
            task {
                try
                    let conditions = ResizeArray<string>()
                    let parameters = DynamicParameters()

                    match filters.SearchTerm with
                    | Some term when not (String.IsNullOrEmpty term) ->
                        conditions.Add("instrument ILIKE @SearchTerm")
                        parameters.Add("SearchTerm", $"%%{term}%%")
                    | _ -> ()

                    match filters.Side with
                    | Some side ->
                        conditions.Add("side = @Side")
                        parameters.Add("Side", int side)
                    | None -> ()

                    match filters.Status with
                    | Some status ->
                        conditions.Add("status = @Status")
                        parameters.Add("Status", int status)
                    | None -> ()

                    match filters.MarketType with
                    | Some marketType ->
                        conditions.Add("market_type = @MarketType")
                        parameters.Add("MarketType", int marketType)
                    | None -> ()

                    let whereClause =
                        if conditions.Count > 0 then "WHERE " + String.Join(" AND ", conditions) else ""

                    let orderClause =
                        match filters.SortBy with
                        | "instrument" -> "ORDER BY instrument ASC"
                        | "instrument-desc" -> "ORDER BY instrument DESC"
                        | "status" -> "ORDER BY status ASC"
                        | "status-desc" -> "ORDER BY status DESC"
                        | "side" -> "ORDER BY side ASC"
                        | "side-desc" -> "ORDER BY side DESC"
                        | "quantity" -> "ORDER BY quantity ASC"
                        | "quantity-desc" -> "ORDER BY quantity DESC"
                        | "created-desc" -> "ORDER BY created_at DESC"
                        | "created" -> "ORDER BY created_at ASC"
                        | _ -> "ORDER BY created_at DESC"

                    parameters.Add("Skip", skip)
                    parameters.Add("Take", take)

                    let countSql = $"SELECT COUNT(1) FROM orders {whereClause}"
                    let dataSql = $"SELECT * FROM orders {whereClause} {orderClause} OFFSET @Skip LIMIT @Take"

                    let! totalCount =
                        db.QuerySingleAsync<int>(CommandDefinition(countSql, parameters, cancellationToken = token))

                    let! orders =
                        db.QueryAsync<OrderEntity>(CommandDefinition(dataSql, parameters, cancellationToken = token))

                    let results = orders |> Seq.toList |> List.choose (toOrder >> Result.toOption)
                    return Ok { Orders = results; TotalCount = totalCount }
                with ex ->
                    return Error(Unexpected ex)
            }

    let count (db: IDbConnection) : CountOrders =
        fun token ->
            task {
                try
                    let! result =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(1) FROM orders", cancellationToken = token)
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
            }
