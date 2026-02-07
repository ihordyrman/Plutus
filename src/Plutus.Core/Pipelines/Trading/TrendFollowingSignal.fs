namespace Plutus.Core.Pipelines.Trading

open System
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps

module TrendFollowingSignal =
    let instruments =
        [ Instrument.BTC
          Instrument.ETH
          Instrument.SOL
          Instrument.OKB
          Instrument.DOGE
          Instrument.XRP
          Instrument.BCH
          Instrument.LTC ]
        |> List.map (fun x -> { Left = x; Right = Instrument.USDT }.ToString())

    let trendFollowing: StepDefinition<TradingContext> =
        let create (_: ValidatedParams) (_: IServiceProvider) : Step<TradingContext> =
            { key = "trend-following-signal"
              execute =
                fun ctx ct ->
                    task {
                        // Placeholder logic for trend following signal
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, no new signal generated.")
                        | _ ->
                            if Random().NextDouble() > 0.5 then
                                return Continue({ ctx with Action = Buy }, "Trend following signal generated: BUY")
                            else
                                return Continue({ ctx with Action = Sell }, "No signal generated")
                    } }

        { Key = "trend-following-signal"
          Name = "Trend Following Signal"
          Description = "Generates buy signals when prices are rising or sell signals when prices are falling."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema =
            { Parameters =
                [ { Key = "instruments"
                    Name = "Trading Pairs"
                    Description = "Select which cryptocurrency pairs to monitor for trend signals."
                    Type = MultiChoice instruments
                    Required = true
                    DefaultValue = Some(ListValue [ instruments |> List.head ])
                    Group = Some "General" } ] }
          Create = create }
