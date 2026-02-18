namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Trading

module BacktestSteps =

    let checkPosition (state: BacktestState.T) : StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            { key = "check-position"
              execute =
                fun ctx _ ->
                    task {
                        match state.CurrentPosition with
                        | None -> return Continue({ ctx with Action = NoAction }, "No open position")
                        | Some pos ->
                            let ctx' =
                                { ctx with
                                    BuyPrice = Some pos.EntryPrice
                                    Quantity = Some pos.Quantity
                                    ActiveOrderId = Some 1
                                    Action = Hold }

                            return Continue(ctx', $"Position found - Entry: {pos.EntryPrice:F8}")
                    } }

        { Key = "check-position"
          Name = "Check Position (Backtest)"
          Description = "Checks backtest in-memory position state."
          Category = StepCategory.Validation
          Icon = "fa-search-dollar"
          ParameterSchema = { Parameters = [] }
          Create = create }

    let positionGate (state: BacktestState.T) : StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            { key = "position-gate-step"
              execute =
                fun ctx _ ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | None, NoAction ->
                            match state.CurrentPosition with
                            | Some _ ->
                                return
                                    Continue(
                                        { ctx with ActiveOrderId = Some 1 },
                                        "Open position exists, setting action to Hold"
                                    )
                            | None -> return Continue(ctx, "No active orders or positions, ready to place entry order.")
                        | _ -> return Continue(ctx, "Already have an active order or action in progress")
                    } }

        { Key = "position-gate-step"
          Name = "Position Gate (Backtest)"
          Description = "Backtest position gate using in-memory state."
          Category = StepCategory.Validation
          Icon = "fa-sign-in-alt"
          ParameterSchema = { Parameters = [] }
          Create = create }

    let entry (state: BacktestState.T) : StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            let tradeAmount = params' |> ValidatedParams.getDecimal "tradeAmount" 100m
            let buyThreshold = params' |> ValidatedParams.getDecimal "buyThreshold" 0.5m
            let sellThreshold = params' |> ValidatedParams.getDecimal "sellThreshold" -0.5m

            { key = "entry-step"
              execute =
                fun ctx _ ->
                    task {
                        let totalWeight = ctx.SignalWeights |> Map.values |> Seq.sum

                        let action =
                            if totalWeight > buyThreshold then Buy
                            elif totalWeight < sellThreshold then Sell
                            else ctx.Action

                        let candleTime =
                            TradingContext.getData<DateTime> "backtest:currentTime" ctx
                            |> Option.defaultValue DateTime.UtcNow

                        match ctx.ActiveOrderId, action with
                        | None, Buy when state.Balance >= tradeAmount ->
                            let quantity = tradeAmount / ctx.CurrentPrice
                            state.Balance <- state.Balance - tradeAmount
                            state.TradeCounter <- state.TradeCounter + 1

                            state.CurrentPosition <-
                                Some
                                    { BacktestState.EntryPrice = ctx.CurrentPrice
                                      Quantity = quantity
                                      EntryTime = candleTime
                                      ExecutionId = ctx.ExecutionId }

                            let trade =
                                { Id = 0
                                  BacktestRunId = 0
                                  Side = OrderSide.Buy
                                  Price = ctx.CurrentPrice
                                  Quantity = quantity
                                  Fee = 0m
                                  CandleTime = candleTime
                                  Capital = state.Balance }

                            state.Trades <- trade :: state.Trades

                            return
                                Continue(
                                    { ctx with
                                        Action = Buy
                                        Quantity = Some quantity
                                        ActiveOrderId = Some state.TradeCounter },
                                    $"BUY {quantity:F8} @ {ctx.CurrentPrice:F4} (totalWeight={totalWeight:F2})"
                                )

                        | Some _, Sell ->
                            match state.CurrentPosition with
                            | Some pos ->
                                let proceeds = pos.Quantity * ctx.CurrentPrice
                                state.Balance <- state.Balance + proceeds
                                state.TradeCounter <- state.TradeCounter + 1
                                state.CurrentPosition <- None

                                let trade =
                                    { Id = 0
                                      BacktestRunId = 0
                                      Side = OrderSide.Sell
                                      Price = ctx.CurrentPrice
                                      Quantity = pos.Quantity
                                      Fee = 0m
                                      CandleTime = candleTime
                                      Capital = state.Balance }

                                state.Trades <- trade :: state.Trades

                                return
                                    Continue(
                                        { ctx with
                                            Action = Sell
                                            ActiveOrderId = None
                                            BuyPrice = None
                                            Quantity = None },
                                        $"SELL {pos.Quantity:F8} @ {ctx.CurrentPrice:F4} (totalWeight={totalWeight:F2})"
                                    )
                            | None -> return Continue(ctx, $"No position to sell. (totalWeight={totalWeight:F2})")

                        | None, Buy ->
                            return Continue(ctx, $"Insufficient balance for buy. (totalWeight={totalWeight:F2})")
                        | _ -> return Continue(ctx, $"No action taken. (totalWeight={totalWeight:F2})")
                    } }

        { Key = "entry-step"
          Name = "Entry Step (Backtest)"
          Description = "Backtest entry step with in-memory order execution."
          Category = StepCategory.Execution
          Icon = "fa-sign-in-alt"
          ParameterSchema = EntryStep.entry.ParameterSchema
          Create = create }
