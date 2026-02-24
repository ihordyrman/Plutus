namespace Plutus.Core.Repositories

open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared
open Plutus.Core.Shared.Errors

type CreatePositionRequest =
    { PipelineId: int
      Instrument: Instrument
      EntryPrice: decimal
      Quantity: decimal
      BuyOrderId: int
      Status: PositionStatus }

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
                            "INSERT INTO positions (pipeline_id, instrument, entry_price, quantity, buy_order_id, status, created_at, updated_at)
                             VALUES (@PipelineId, @Instrument, @EntryPrice, @Quantity, @BuyOrderId, @Status, NOW(), NOW())
                             RETURNING *",
                            {| PipelineId = request.PipelineId
                               Instrument = request.Instrument
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
