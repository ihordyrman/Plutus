namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type CreateOrderRequest =
    { PipelineId: int
      MarketType: MarketType
      Instrument: string
      Side: OrderSide
      Quantity: decimal
      Price: decimal }

type SearchFilters =
    { SearchTerm: string option
      Side: OrderSide option
      Status: OrderStatus option
      MarketType: MarketType option
      SortBy: string }

type SearchResult = { Orders: Order list; TotalCount: int }

[<RequireQualifiedAccess>]
module OrderRepository =
    let getById (db: IDbConnection) (orderId: int) (token: CancellationToken) =
        task {
            try
                let! order =
                    db.QueryFirstOrDefaultAsync<Order>(
                        CommandDefinition(
                            "SELECT * FROM orders WHERE id = @Id",
                            {| Id = orderId |},
                            cancellationToken = token
                        )
                    )

                match box order with
                | null -> return Ok None
                | _ -> return Ok(Some order)
            with ex ->
                return Error(Unexpected ex)
        }

    let getByExchangeId (db: IDbConnection) (exchangeOrderId: string) (market: MarketType) (token: CancellationToken) =
        task {
            try
                let! order =
                    db.QueryFirstOrDefaultAsync<Order>(
                        CommandDefinition(
                            "SELECT * FROM orders WHERE exchange_order_id = @ExchangeOrderId AND market_type = @MarketType",
                            {| ExchangeOrderId = exchangeOrderId; MarketType = int market |},
                            cancellationToken = token
                        )
                    )

                match box order with
                | null -> return Ok None
                | _ -> return Ok(Some order)
            with ex ->
                return Error(Unexpected ex)
        }

    let getByPipeline (db: IDbConnection) (pipelineId: int) (status: OrderStatus option) (token: CancellationToken) =
        task {
            try
                let query =
                    match status with
                    | Some s ->
                        "SELECT * FROM orders WHERE pipeline_id = @PipelineId AND status = @Status ORDER BY created_at DESC"
                    | None -> "SELECT * FROM orders WHERE pipeline_id = @PipelineId ORDER BY created_at DESC"

                let parameters: obj =
                    match status with
                    | Some s -> {| PipelineId = pipelineId; Status = int s |}
                    | None -> {| PipelineId = pipelineId |}

                let! orders = db.QueryAsync<Order>(CommandDefinition(query, parameters, cancellationToken = token))
                return Ok(orders |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getActive (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! orders =
                    db.QueryAsync<Order>(
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

                return Ok(orders |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getHistory (db: IDbConnection) (skip: int) (take: int) (token: CancellationToken) =
        task {
            try
                let! orders =
                    db.QueryAsync<Order>(
                        CommandDefinition(
                            "SELECT * FROM orders ORDER BY created_at DESC OFFSET @Skip LIMIT @Take",
                            {| Skip = skip; Take = take |},
                            cancellationToken = token
                        )
                    )

                return Ok(orders |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let getTotalExposure (db: IDbConnection) (market: MarketType option) (token: CancellationToken) =
        task {
            try
                let sql, parameters =
                    match market with
                    | Some m ->
                        """SELECT COALESCE(SUM(quantity * COALESCE(price, 0)), 0)
                           FROM orders
                           WHERE status IN (@Placed, @PartiallyFilled, @Filled)
                           AND market_type = @MarketType""",
                        {| Placed = int OrderStatus.Placed
                           PartiallyFilled = int OrderStatus.PartiallyFilled
                           Filled = int OrderStatus.Filled
                           MarketType = int m |}
                        :> obj
                    | None ->
                        """SELECT COALESCE(SUM(quantity * COALESCE(price, 0)), 0)
                           FROM orders
                           WHERE status IN (@Placed, @PartiallyFilled, @Filled)""",
                        {| Placed = int OrderStatus.Placed
                           PartiallyFilled = int OrderStatus.PartiallyFilled
                           Filled = int OrderStatus.Filled |}
                        :> obj

                let! result =
                    db.QuerySingleAsync<decimal>(CommandDefinition(sql, parameters, cancellationToken = token))

                return Ok result
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (txn: IDbTransaction) (order: CreateOrderRequest) (token: CancellationToken) =
        task {
            try
                let marketTypeInt = int order.MarketType
                let sideInt = int order.Side
                let statusInt = int OrderStatus.Pending

                let! order =
                    db.QuerySingleAsync<Order>(
                        CommandDefinition(
                            """INSERT INTO orders
                               (pipeline_id, market_type, exchange_order_id, instrument, side, status, quantity, price, created_at, updated_at)
                               VALUES (@PipelineId, @MarketType, @ExchangeOrderId, @Instrument, @Side, @Status, @Quantity, @Price, now(), now())
                               RETURNING *""",
                            {| PipelineId = order.PipelineId
                               MarketType = marketTypeInt
                               ExchangeOrderId = ""
                               Instrument = order.Instrument
                               Side = sideInt
                               Status = statusInt
                               Quantity = order.Quantity
                               Price = order.Price |},
                            cancellationToken = token,
                            transaction = txn
                        )
                    )

                return Ok order
            with ex ->
                return Error(Unexpected ex)
        }

    let update (db: IDbConnection) (txn: IDbTransaction) (order: Order) (token: CancellationToken) =
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
                            {| Id = order.Id
                               Status = int order.Status
                               Quantity = order.Quantity
                               Price = order.Price
                               StopPrice = order.StopPrice
                               TakeProfit = order.TakeProfit
                               StopLoss = order.StopLoss
                               ExchangeOrderId = order.ExchangeOrderId
                               PlacedAt = order.PlacedAt
                               ExecutedAt = order.ExecutedAt
                               CancelledAt = order.CancelledAt
                               Fee = order.Fee
                               UpdatedAt = order.UpdatedAt |},
                            cancellationToken = token,
                            transaction = txn
                        )
                    )

                return Ok()
            with ex ->
                return Error(Unexpected ex)
        }

    let search (db: IDbConnection) (filters: SearchFilters) (skip: int) (take: int) (token: CancellationToken) =
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

                let whereClause = if conditions.Count > 0 then "WHERE " + String.Join(" AND ", conditions) else ""

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

                let! orders = db.QueryAsync<Order>(CommandDefinition(dataSql, parameters, cancellationToken = token))

                return Ok { Orders = orders |> Seq.toList; TotalCount = totalCount }
            with ex ->
                return Error(Unexpected ex)
        }

    let count (db: IDbConnection) (token: CancellationToken) =
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
