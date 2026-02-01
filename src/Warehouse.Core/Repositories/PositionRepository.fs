namespace Warehouse.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Warehouse.Core.Domain
open Warehouse.Core.Shared.Errors

type CreatePositionRequest =
    { PipelineId: int
      Symbol: string
      EntryPrice: decimal
      Quantity: decimal
      BuyOrderId: int
      Status: PositionStatus }

type UpdatePositionRequest =
    { Id: int
      ExitPrice: decimal option
      SellOrderId: int option
      Status: PositionStatus
      ClosedAt: DateTime option }

[<RequireQualifiedAccess>]
module PositionRepository =

    let getOpen (db: IDbConnection) (pipelineId: int) (cancellation: CancellationToken) =
        task {
            try
                let! positions =
                    db.QueryAsync<Position>(
                        CommandDefinition(
                            "SELECT *
                              FROM positions
                              WHERE pipeline_id = @PipelineId AND status = @Status
                              LIMIT 1",
                            {| PipelineId = pipelineId; Status = int PositionStatus.Open |},
                            cancellationToken = cancellation
                        )
                    )

                match positions |> Seq.tryHead with
                | None -> return Ok None
                | Some position -> return Ok(Some(position))
            with ex ->
                return Error(Unexpected ex)
        }

    let create
        (db: IDbConnection)
        (tnx: IDbTransaction)
        (request: CreatePositionRequest)
        (cancellation: CancellationToken)
        =
        task {
            try
                let! result =
                    db.QuerySingleAsync<Position>(
                        CommandDefinition(
                            "INSERT INTO positions (pipeline_id, symbol, entry_price, quantity, buy_order_id, status, created_at, updated_at)
                             VALUES (@PipelineId, @Symbol, @EntryPrice, @Quantity, @BuyOrderId, @Status, NOW(), NOW())
                             RETURNING *",
                            {| PipelineId = request.PipelineId
                               Symbol = request.Symbol
                               EntryPrice = request.EntryPrice
                               Quantity = request.Quantity
                               BuyOrderId = request.BuyOrderId
                               Status = int request.Status |},
                            cancellationToken = cancellation,
                            transaction = tnx
                        )
                    )

                return Ok result
            with ex ->
                return Error(Unexpected ex)
        }

    let update
        (db: IDbConnection)
        (tnx: IDbTransaction)
        (request: UpdatePositionRequest)
        (cancellation: CancellationToken)
        =
        task {
            try
                let closedAt =
                    match request.ClosedAt with
                    | Some dt -> box dt
                    | None -> null

                let! result =
                    db.QuerySingleAsync<Position>(
                        CommandDefinition(
                            "UPDATE positions
                             SET exit_price = @ExitPrice,
                                 sell_order_id = @SellOrderId,
                                 status = @Status,
                                 closed_at = @ClosedAt,
                                 updated_at = NOW()
                             WHERE id = @Id
                             RETURNING *",
                            {| Id = request.Id
                               ExitPrice = request.ExitPrice
                               SellOrderId = request.SellOrderId
                               Status = int request.Status
                               ClosedAt = closedAt |},
                            cancellationToken = cancellation,
                            transaction = tnx
                        )
                    )

                return Ok result
            with ex ->
                return Error(Unexpected ex)
        }
