namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private DbPosition =
    { Id: int
      PipelineId: int
      InstrumentId: string
      InstrumentType: string
      BaseCurrency: string
      QuoteCurrency: string
      MarketType: int
      EntryPrice: decimal
      Quantity: decimal
      BuyOrderId: string
      SellOrderId: string option
      Status: int
      ExitPrice: decimal option
      ClosedAt: DateTime option
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module Positions =
    let private toPosition (p: DbPosition) : Result<Position, string> =
        let instrumentResult =
            match
                InstrumentId.create p.InstrumentId,
                InstrumentType.create p.InstrumentType,
                Pair.create p.BaseCurrency p.QuoteCurrency,
                MarketType.create p.MarketType
            with
            | Ok instrId, Ok instrType, Ok pair, Ok marketType ->
                Ok { Id = instrId; Type = instrType; Pair = pair; MarketType = marketType }
            | Error e, _, _, _ -> Error $"Invalid InstrumentId: {e}"
            | _, Error e, _, _ -> Error $"Invalid InstrumentType: {e}"
            | _, _, Error e, _ -> Error $"Invalid pair: {e}"
            | _, _, _, Error e -> Error $"Invalid market type: {e}"

        let stateResult =
            match p.Status, p.ExitPrice, p.ClosedAt with
            | 1, Some exitPrice, Some closedAt ->
                PositiveDecimal.create exitPrice |> Result.map (fun ep -> Closed(ep, closedAt))
            | 1, _, _ -> Error "Closed position missing exit_price or closed_at"
            | 2, _, closedAt -> Ok(Cancelled(closedAt |> Option.defaultValue p.UpdatedAt))
            | _ -> Ok(Open p.CreatedAt)

        let buyOrderIdResult =
            match System.Int32.TryParse(p.BuyOrderId) with
            | true, n -> OrderId.create n
            | false, _ -> Error $"Invalid buy_order_id: {p.BuyOrderId}"

        match
            instrumentResult,
            PipelineId.create p.PipelineId,
            PositiveDecimal.create p.EntryPrice,
            PositiveDecimal.create p.Quantity,
            buyOrderIdResult,
            stateResult
        with
        | Ok instrument, Ok pipelineId, Ok entryPrice, Ok quantity, Ok buyOrderId, Ok state ->
            let sellOrderId =
                p.SellOrderId
                |> Option.bind (fun s ->
                    match System.Int32.TryParse(s) with
                    | true, n -> OrderId.create n |> Result.toOption
                    | _ -> None
                )

            Ok
                { PipelineId = pipelineId
                  Instrument = instrument
                  EntryPrice = entryPrice
                  Quantity = quantity
                  BuyOrderId = buyOrderId
                  SellOrderId = sellOrderId
                  PositionState = state }
        | Error e, _, _, _, _, _ -> Error e
        | _, Error e, _, _, _, _ -> Error $"Invalid pipeline ID: {e}"
        | _, _, Error e, _, _, _ -> Error $"Invalid entry price: {e}"
        | _, _, _, Error e, _, _ -> Error $"Invalid quantity: {e}"
        | _, _, _, _, Error e, _ -> Error $"Invalid buy order ID: {e}"
        | _, _, _, _, _, Error e -> Error e

    let getOpen (db: IDbConnection) : GetOpen =
        fun pipelineId token ->
            task {
                try
                    let! result =
                        db.QuerySingleOrDefaultAsync<DbPosition>(
                            CommandDefinition(
                                """SELECT p.id, p.pipeline_id,
                                          p.instrument AS instrument_id,
                                          i.instrument_type, i.base_currency, i.quote_currency, i.market_type,
                                          p.entry_price, p.quantity,
                                          p.buy_order_id, p.sell_order_id,
                                          p.status, p.exit_price, p.closed_at, p.created_at, p.updated_at
                                   FROM positions p
                                   JOIN pipelines pl ON pl.id = p.pipeline_id
                                   JOIN instruments i ON i.instrument_id = p.instrument AND i.market_type = pl.market_type
                                   WHERE p.pipeline_id = @PipelineId AND p.status = @Status
                                   LIMIT 1""",
                                {| PipelineId = PipelineId.value pipelineId; Status = 0 |},
                                cancellationToken = token
                            )
                        )

                    match box result with
                    | null -> return Error(NotFound $"No open position for pipeline {PipelineId.value pipelineId}")
                    | _ ->
                        return
                            match toPosition result with
                            | Ok position -> Ok position
                            | Error e -> Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let create (db: IDbConnection) : Create =
        fun request token ->
            task {
                try
                    let now = DateTime.UtcNow

                    let! _ =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO positions (pipeline_id, instrument, entry_price, quantity, buy_order_id, status, created_at, updated_at)
                                   VALUES (@PipelineId, @Instrument, @EntryPrice, @Quantity, @BuyOrderId, @Status, @Now, @Now)""",
                                {| PipelineId = PipelineId.value request.PipelineId
                                   Instrument = InstrumentId.value request.Instrument.Id
                                   EntryPrice = PositiveDecimal.value request.EntryPrice
                                   Quantity = PositiveDecimal.value request.Quantity
                                   BuyOrderId = OrderId.value request.BuyOrderId
                                   Status = 0
                                   Now = now |},
                                cancellationToken = token
                            )
                        )

                    return
                        Ok
                            { PipelineId = request.PipelineId
                              Instrument = request.Instrument
                              EntryPrice = request.EntryPrice
                              Quantity = request.Quantity
                              BuyOrderId = request.BuyOrderId
                              SellOrderId = None
                              PositionState = Open now }
                with ex ->
                    return Error(Unexpected ex)
            }
