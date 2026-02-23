namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Ports

module EntryStep =
    let entry (executor: TradeExecutor) : StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            let tradeAmount = params' |> ValidatedParams.getDecimal "tradeAmount" 100m
            let buyThreshold = params' |> ValidatedParams.getDecimal "buyThreshold" 0.5m
            let sellThreshold = params' |> ValidatedParams.getDecimal "sellThreshold" -0.5m

            { key = "entry-step"
              execute =
                fun ctx ct ->
                    task {
                        let totalWeight = ctx.SignalWeights |> Map.values |> Seq.sum

                        let action =
                            if totalWeight > buyThreshold then Buy
                            elif totalWeight < sellThreshold then Sell
                            else ctx.Action

                        match ctx.ActiveOrderId, action with
                        | None, Buy ->
                            match! executor.ExecuteBuy ctx tradeAmount ct with
                            | Ok(ctx', msg) -> return Continue(ctx', $"{msg} (totalWeight={totalWeight:F2})")
                            | Error err -> return Fail $"Error placing buy order: {err}"
                        | Some _, Sell ->
                            match! executor.ExecuteSell ctx ct with
                            | Ok(ctx', msg) -> return Continue(ctx', $"{msg} (totalWeight={totalWeight:F2})")
                            | Error err -> return Fail $"Error placing sell order: {err}"
                        | _ -> return Continue(ctx, $"No action taken. (totalWeight={totalWeight:F2})")
                    } }

        { Key = "entry-step"
          Name = "Entry Step"
          Description = "Places an entry trade based on aggregated signal weights."
          Category = StepCategory.Execution
          Icon = "fa-sign-in-alt"
          ParameterSchema =
            { Parameters =
                [ { Key = "tradeAmount"
                    Name = "Trade Amount (USDT)"
                    Description = "Amount in USDT to trade per order"
                    Type = Decimal(Some 1m, Some 100000m)
                    Required = true
                    DefaultValue = Some(DecimalValue 100m)
                    Group = Some "Order Settings" }
                  { Key = "buyThreshold"
                    Name = "Buy Threshold"
                    Description = "Minimum total signal weight to trigger a buy"
                    Type = Decimal(Some -100m, Some 100m)
                    Required = false
                    DefaultValue = Some(DecimalValue 0.5m)
                    Group = Some "Thresholds" }
                  { Key = "sellThreshold"
                    Name = "Sell Threshold"
                    Description = "Maximum total signal weight to trigger a sell"
                    Type = Decimal(Some -100m, Some 100m)
                    Required = false
                    DefaultValue = Some(DecimalValue -0.5m)
                    Group = Some "Thresholds" } ] }
          RequiredCandleData = fun _ -> []
          Create = create }
