namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Ports

module BacktestAdapters =
    let getPosition (state: BacktestState.T) : GetPosition =
        fun _ _ ->
            task {
                match state.CurrentPosition with
                | None -> return Ok None
                | Some pos -> return Ok(Some { EntryPrice = pos.EntryPrice; Quantity = pos.Quantity; OrderId = 1 })
            }

    let tradeExecutor (state: BacktestState.T) : TradeExecutor =
        { ExecuteBuy =
            fun ctx tradeAmount _ ->
                task {
                    let candleTime =
                        TradingContext.getData<DateTime> "backtest:currentTime" ctx
                        |> Option.defaultValue DateTime.UtcNow

                    if state.Balance < tradeAmount then
                        return Ok(ctx, "Insufficient balance")
                    else
                        let quantity = tradeAmount / ctx.CurrentPrice
                        state.Balance <- state.Balance - tradeAmount
                        state.TradeCounter <- state.TradeCounter + 1

                        state.CurrentPosition <-
                            Some
                                { BacktestState.EntryPrice = ctx.CurrentPrice
                                  Quantity = quantity
                                  EntryTime = candleTime
                                  ExecutionId = ctx.ExecutionId }

                        state.Trades <-
                            { Id = 0
                              BacktestRunId = 0
                              Side = OrderSide.Buy
                              Price = ctx.CurrentPrice
                              Quantity = quantity
                              Fee = 0m
                              CandleTime = candleTime
                              Capital = state.Balance }
                            :: state.Trades

                        return
                            Ok(
                                { ctx with
                                    Action = Buy
                                    Quantity = Some quantity
                                    ActiveOrderId = Some state.TradeCounter },
                                $"BUY {quantity:F8} @ {ctx.CurrentPrice:F4}"
                            )
                }
          ExecuteSell =
            fun ctx _ ->
                task {
                    let candleTime =
                        TradingContext.getData<DateTime> "backtest:currentTime" ctx
                        |> Option.defaultValue DateTime.UtcNow

                    match state.CurrentPosition with
                    | None -> return Ok(ctx, "No position to sell")
                    | Some pos ->
                        let proceeds = pos.Quantity * ctx.CurrentPrice
                        state.Balance <- state.Balance + proceeds
                        state.TradeCounter <- state.TradeCounter + 1
                        state.CurrentPosition <- None

                        state.Trades <-
                            { Id = 0
                              BacktestRunId = 0
                              Side = OrderSide.Sell
                              Price = ctx.CurrentPrice
                              Quantity = pos.Quantity
                              Fee = 0m
                              CandleTime = candleTime
                              Capital = state.Balance }
                            :: state.Trades

                        return
                            Ok(
                                { ctx with Action = Sell; ActiveOrderId = None; BuyPrice = None; Quantity = None },
                                $"SELL {pos.Quantity:F8} @ {ctx.CurrentPrice:F4}"
                            )
                } }
