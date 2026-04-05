namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Ports

module BacktestAdapters =
    let getPosition (stateRef: SimState ref) : GetPosition =
        fun _ _ ->
            task {
                match stateRef.Value.Position with
                | None -> return Ok None
                | Some pos ->
                    return
                        Ok(
                            Some
                                { EntryPrice = pos.EntryPrice
                                  Quantity = pos.Quantity
                                  OrderId = 1 }
                        )
            }

    let tradeExecutor (stateRef: SimState ref) : TradeExecutor =
        { ExecuteBuy =
            fun ctx tradeAmount _ ->
                task {
                    let state = stateRef.Value

                    let candleTime =
                        TradingContext.getData<DateTime> "backtest:currentTime" ctx
                        |> Option.defaultValue DateTime.UtcNow

                    if state.Balance < tradeAmount then
                        return Ok(ctx, "Insufficient balance")
                    else
                        let quantity = tradeAmount / ctx.CurrentPrice
                        let tradeCounter = state.TradeCount + 1

                        stateRef.Value <-
                            { state with
                                TradeCount = tradeCounter
                                Balance = state.Balance - tradeAmount
                                Position =
                                    Some
                                        { EntryPrice = ctx.CurrentPrice
                                          Quantity = quantity
                                          EntryTime = candleTime
                                          ExecutionId = ctx.ExecutionId }
                                Trades =
                                    { Id = 0
                                      BacktestRunId = 0
                                      Side = OrderSide.Buy
                                      Price = ctx.CurrentPrice
                                      Quantity = quantity
                                      Fee = 0m
                                      CandleTime = candleTime
                                      Capital = state.Balance }
                                    :: state.Trades }

                        return
                            Ok(
                                { ctx with
                                    Action = Buy
                                    Quantity = Some quantity
                                    ActiveOrderId = Some state.TradeCount },
                                $"BUY {quantity:F8} @ {ctx.CurrentPrice:F4}"
                            )
                }
          ExecuteSell =
            fun ctx _ ->
                task {
                    let state = stateRef.Value

                    let candleTime =
                        TradingContext.getData<DateTime> "backtest:currentTime" ctx
                        |> Option.defaultValue DateTime.UtcNow

                    match state.Position with
                    | None -> return Ok(ctx, "No position to sell")
                    | Some pos ->
                        let proceeds = pos.Quantity * ctx.CurrentPrice

                        stateRef.Value <-
                            { state with
                                Balance = state.Balance + proceeds
                                TradeCount = state.TradeCount + 1
                                Position = None
                                Trades =
                                    { Id = 0
                                      BacktestRunId = 0
                                      Side = OrderSide.Sell
                                      Price = ctx.CurrentPrice
                                      Quantity = pos.Quantity
                                      Fee = 0m
                                      CandleTime = candleTime
                                      Capital = state.Balance }
                                    :: state.Trades }

                        return
                            Ok(
                                { ctx with
                                    Action = Sell
                                    ActiveOrderId = None
                                    BuyPrice = None
                                    Quantity = None },
                                $"SELL {pos.Quantity:F8} @ {ctx.CurrentPrice:F4}"
                            )
                } }
