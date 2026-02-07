namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories

module EwmacSignal =
    let private timeframes = [ "1m"; "5m"; "15m"; "30m"; "1H"; "4H"; "1Dutc" ]

    let ewmac: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let fastSpan = params' |> ValidatedParams.getInt "fastSpan" 8
            let slowSpan = params' |> ValidatedParams.getInt "slowSpan" 32
            let timeframe = params' |> ValidatedParams.getString "timeframe" "1m"
            let signalWeight = params' |> ValidatedParams.getDecimal "signalWeight" 1.0m

            { key = "ewmac-signal"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, skip EWMAC signal.")
                        | _ ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                            let candleCount = slowSpan * 2

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
                            | Error err -> return Fail $"Error fetching candles: {err}"
                            | Ok candles when candles.Length < slowSpan ->
                                return
                                    Continue(
                                        ctx,
                                        $"Insufficient candle data ({candles.Length}/{candleCount}), skip EWMAC signal."
                                    )
                            | Ok candles ->
                                let closes = candles |> List.rev |> List.map _.Close
                                let fastEma = Indicators.emaSeries fastSpan closes
                                let slowEma = Indicators.emaSeries slowSpan closes

                                let offset = fastEma.Length - slowEma.Length
                                let fastAligned = fastEma |> List.skip offset

                                let rawSignal = List.map2 (fun f s -> f - s) fastAligned slowEma

                                let rets = Indicators.returns closes
                                let rollingStd = Indicators.rollingStdDev slowSpan rets

                                if rawSignal.IsEmpty || rollingStd.IsEmpty then
                                    return Continue(ctx, "Not enough data for EWMAC normalization.")
                                else
                                    let lastRawSignal = List.last rawSignal
                                    let lastStd = List.last rollingStd
                                    let lastPrice = List.last closes

                                    let normalizer =
                                        if lastStd > 0m && lastPrice > 0m then lastStd * lastPrice else 1.0m

                                    let normalized = lastRawSignal / normalizer

                                    let rawDirection =
                                        if normalized > 0m then 1.0m
                                        elif normalized < 0m then -1.0m
                                        else 0m

                                    let weight = rawDirection * signalWeight

                                    let ctx' = ctx |> TradingContext.withSignalWeight "ewmac-signal" weight

                                    let dirStr =
                                        if rawDirection > 0m then "BUY"
                                        elif rawDirection < 0m then "SELL"
                                        else "NEUTRAL"

                                    return
                                        Continue(
                                            ctx',
                                            $"EWMAC signal: {dirStr} (normalized={normalized:F4}, weight={weight:F2})"
                                        )
                    } }

        { Key = "ewmac-signal"
          Name = "EWMAC Signal"
          Description =
            "Generates buy/sell signals based on Carver-style EWMAC (exponentially weighted moving average crossover)."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema =
            { Parameters =
                [ { Key = "fastSpan"
                    Name = "Fast Span"
                    Description = "Span for the fast EWMA"
                    Type = Int(Some 2, Some 200)
                    Required = false
                    DefaultValue = Some(IntValue 8)
                    Group = Some "Indicator" }
                  { Key = "slowSpan"
                    Name = "Slow Span"
                    Description = "Span for the slow EWMA"
                    Type = Int(Some 5, Some 500)
                    Required = false
                    DefaultValue = Some(IntValue 32)
                    Group = Some "Indicator" }
                  { Key = "timeframe"
                    Name = "Timeframe"
                    Description = "Candlestick timeframe"
                    Type = Choice timeframes
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
          Create = create }
