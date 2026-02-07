namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories

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

    let private timeframes = [ "1m"; "5m"; "15m"; "30m"; "1H"; "4H"; "1Dutc" ]

    let trendFollowing: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let selectedInstruments = params' |> ValidatedParams.getList "instruments" [ instruments |> List.head ]
            let lookbackPeriod = params' |> ValidatedParams.getInt "lookbackPeriod" 20
            let momentumThreshold = params' |> ValidatedParams.getDecimal "momentumThreshold" 2.0m
            let timeframe = params' |> ValidatedParams.getString "timeframe" "1m"
            let breadthThresholdPct = params' |> ValidatedParams.getDecimal "breadthThresholdPct" 50.0m
            let signalWeight = params' |> ValidatedParams.getDecimal "signalWeight" 1.0m

            { key = "trend-following-signal"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, skip trend following signal.")
                        | _ ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                            let candleCount = lookbackPeriod + 1

                            match!
                                CandlestickRepository.query
                                    db
                                    ctx.Symbol
                                    (ctx.MarketType)
                                    timeframe
                                    None
                                    None
                                    (Some candleCount)
                                    ct
                            with
                            | Error err -> return Fail $"Error fetching candles for {ctx.Symbol}: {err}"
                            | Ok ownCandles when ownCandles.Length < candleCount ->
                                return
                                    Continue(
                                        ctx,
                                        $"Insufficient candle data for {ctx.Symbol} ({ownCandles.Length}/{candleCount}), skip trend signal."
                                    )
                            | Ok ownCandles ->
                                let ownCloses = ownCandles |> List.rev |> List.map _.Close

                                let ownMomentum = Indicators.momentum lookbackPeriod ownCloses

                                match ownMomentum with
                                | None -> return Continue(ctx, "Could not compute own momentum, skip trend signal.")
                                | Some ownMom ->
                                    let mutable bullish = 0
                                    let mutable bearish = 0
                                    let mutable total = 0

                                    for instrument in selectedInstruments do
                                        if instrument <> ctx.Symbol then
                                            match!
                                                CandlestickRepository.query
                                                    db
                                                    instrument
                                                    (ctx.MarketType)
                                                    timeframe
                                                    None
                                                    None
                                                    (Some candleCount)
                                                    ct
                                            with
                                            | Ok instCandles when instCandles.Length >= candleCount ->
                                                let closes = instCandles |> List.rev |> List.map _.Close

                                                match Indicators.momentum lookbackPeriod closes with
                                                | Some mom ->
                                                    total <- total + 1

                                                    if mom > momentumThreshold then
                                                        bullish <- bullish + 1
                                                    elif mom < -momentumThreshold then
                                                        bearish <- bearish + 1
                                                | None -> ()
                                            | _ -> ()

                                    let breadthRequired = breadthThresholdPct / 100m

                                    let rawDirection =
                                        if total = 0 then
                                            if ownMom > momentumThreshold then 1.0m
                                            elif ownMom < -momentumThreshold then -1.0m
                                            else 0m
                                        else
                                            let bullishPct = decimal bullish / decimal total
                                            let bearishPct = decimal bearish / decimal total

                                            if ownMom > momentumThreshold && bullishPct >= breadthRequired then 1.0m
                                            elif ownMom < -momentumThreshold && bearishPct >= breadthRequired then -1.0m
                                            else 0m

                                    let weight = rawDirection * signalWeight

                                    let ctx' = ctx |> TradingContext.withSignalWeight "trend-following-signal" weight

                                    let dirStr =
                                        if rawDirection > 0m then "BUY"
                                        elif rawDirection < 0m then "SELL"
                                        else "NEUTRAL"

                                    return
                                        Continue(
                                            ctx',
                                            $"Trend following signal: {dirStr} (ownMom={ownMom:F2}%%, breadth: {bullish}B/{bearish}S of {total}, weight={weight:F2})"
                                        )
                    } }

        { Key = "trend-following-signal"
          Name = "Trend Following Signal"
          Description = "Generates buy/sell signals based on momentum with market breadth confirmation."
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
                    Group = Some "General" }
                  { Key = "lookbackPeriod"
                    Name = "Lookback Period"
                    Description = "Number of candles for momentum calculation"
                    Type = Int(Some 5, Some 200)
                    Required = false
                    DefaultValue = Some(IntValue 20)
                    Group = Some "Indicator" }
                  { Key = "momentumThreshold"
                    Name = "Momentum Threshold %"
                    Description = "Minimum momentum percentage to trigger signal"
                    Type = Decimal(Some 0.1m, Some 50.0m)
                    Required = false
                    DefaultValue = Some(DecimalValue 2.0m)
                    Group = Some "Indicator" }
                  { Key = "timeframe"
                    Name = "Timeframe"
                    Description = "Candlestick timeframe"
                    Type = Choice timeframes
                    Required = false
                    DefaultValue = Some(ChoiceValue "1m")
                    Group = Some "General" }
                  { Key = "breadthThresholdPct"
                    Name = "Breadth Threshold %"
                    Description = "Percentage of instruments that must confirm the trend"
                    Type = Decimal(Some 10.0m, Some 100.0m)
                    Required = false
                    DefaultValue = Some(DecimalValue 50.0m)
                    Group = Some "Indicator" }
                  { Key = "signalWeight"
                    Name = "Signal Weight"
                    Description = "Weight multiplier for this signal's contribution"
                    Type = Decimal(Some 0.1m, Some 10.0m)
                    Required = false
                    DefaultValue = Some(DecimalValue 1.0m)
                    Group = Some "Weights" } ] }
          Create = create }
