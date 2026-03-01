namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories
open Plutus.Core.Shared

module VwapSignal =
    let private intervals =
        [ Interval.OneMinute
          Interval.FiveMinutes
          Interval.FifteenMinutes
          Interval.ThirtyMinutes
          Interval.OneHour
          Interval.FourHours
          Interval.OneDay ]
        |> List.map string

    let vwap: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let lookbackPeriod = params' |> ValidatedParams.getInt "lookbackPeriod" 20
            let thresholdPct = params' |> ValidatedParams.getDecimal "thresholdPct" 1.0m
            let interval = params' |> ValidatedParams.getString "interval" "1m" |> Interval.parse
            let signalWeight = params' |> ValidatedParams.getDecimal "signalWeight" 1.0m

            { key = "vwap-signal"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, skip VWAP signal.")
                        | _ ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                            let toDate = TradingContext.getData<DateTime> "backtest:currentTime" ctx

                            match!
                                CandlestickRepository.query
                                    db
                                    ctx.Instrument
                                    ctx.MarketType
                                    interval
                                    None
                                    toDate
                                    (Some lookbackPeriod)
                                    ct
                            with
                            | Error err -> return Fail $"Error fetching candles: {err}"
                            | Ok candles when candles.Length < lookbackPeriod ->
                                return
                                    Continue(
                                        ctx,
                                        $"Insufficient candle data ({candles.Length}/{lookbackPeriod}), skip VWAP signal."
                                    )
                            | Ok candles ->
                                let data =
                                    candles
                                    |> List.rev
                                    |> List.map (fun c ->
                                        let tp = (c.High + c.Low + c.Close) / 3.0m
                                        (tp, c.Volume)
                                    )

                                match Indicators.vwap data with
                                | None -> return Continue(ctx, "VWAP calculation failed (zero volume), skip signal.")
                                | Some vwapValue ->
                                    let threshold = vwapValue * thresholdPct / 100m

                                    let rawDirection =
                                        if ctx.CurrentPrice > vwapValue + threshold then 1.0m
                                        elif ctx.CurrentPrice < vwapValue - threshold then -1.0m
                                        else 0m

                                    let weight = rawDirection * signalWeight

                                    let ctx' = ctx |> TradingContext.withSignalWeight "vwap-signal" weight

                                    let dirStr =
                                        if rawDirection > 0m then "BUY"
                                        elif rawDirection < 0m then "SELL"
                                        else "NEUTRAL"

                                    return
                                        Continue(
                                            ctx',
                                            $"VWAP signal: {dirStr} (price={ctx.CurrentPrice:F4}, vwap={vwapValue:F4}, weight={weight:F2})"
                                        )
                    } }

        { Key = "vwap-signal"
          Name = "VWAP Signal"
          Description = "Generates buy/sell signals based on price deviation from VWAP."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema =
            { Parameters =
                [ { Key = "lookbackPeriod"
                    Name = "Lookback Period"
                    Description = "Number of candles to compute VWAP over"
                    Type = Int(Some 5, Some 500)
                    Required = false
                    DefaultValue = Some(IntValue 20)
                    Group = Some "Indicator" }
                  { Key = "thresholdPct"
                    Name = "Threshold %"
                    Description = "Percentage deviation from VWAP to trigger signal"
                    Type = Decimal(Some 0.1m, Some 10.0m)
                    Required = false
                    DefaultValue = Some(DecimalValue 1.0m)
                    Group = Some "Indicator" }
                  { Key = "interval"
                    Name = "Interval"
                    Description = "Candlestick interval"
                    Type = Choice intervals
                    Required = false
                    DefaultValue = Some(ChoiceValue "1m")
                    Group = Some "General" }
                  { Key = "signalWeight"
                    Name = "Signal Weight"
                    Description = "Weight multiplier for this signal's contribution"
                    Type = Decimal(Some 0.1m, Some 10.0m)
                    Required = false
                    DefaultValue = Some(DecimalValue 1.0m)
                    Group = Some "Weights" } ] }
          RequiredCandleData = fun _ -> []
          Create = create }
