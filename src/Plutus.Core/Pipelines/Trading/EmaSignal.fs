namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories

module ExponentialMovingAverageSignal =
    let private timeframes = [ "1m"; "5m"; "15m"; "30m"; "1H"; "4H"; "1Dutc" ]

    let ema: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let fastPeriod = params' |> ValidatedParams.getInt "fastPeriod" 9
            let slowPeriod = params' |> ValidatedParams.getInt "slowPeriod" 21
            let timeframe = params' |> ValidatedParams.getString "timeframe" "1m"
            let signalWeight = params' |> ValidatedParams.getDecimal "signalWeight" 1.0m

            { key = "ema-signal"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, skip EMA signal.")
                        | _ ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                            let candleCount = slowPeriod + 1
                            let toDate = TradingContext.getData<DateTime> "backtest:currentTime" ctx

                            match!
                                CandlestickRepository.query
                                    db
                                    ctx.Instrument
                                    ctx.MarketType
                                    timeframe
                                    None
                                    toDate
                                    (Some candleCount)
                                    ct
                            with
                            | Error err -> return Fail $"Error fetching candles: {err}"
                            | Ok candles when candles.Length < slowPeriod + 1 ->
                                return
                                    Continue(
                                        ctx,
                                        $"Insufficient candle data ({candles.Length}/{candleCount}), skip EMA signal."
                                    )
                            | Ok candles ->
                                let closes = candles |> List.rev |> List.map _.Close

                                let fastEma = Indicators.emaSeries fastPeriod closes
                                let slowEma = Indicators.emaSeries slowPeriod closes

                                let minLen = min fastEma.Length slowEma.Length

                                if minLen < 2 then
                                    return Continue(ctx, "Not enough EMA values for crossover detection.")
                                else
                                    let fastCurr = fastEma[fastEma.Length - 1]
                                    let fastPrev = fastEma[fastEma.Length - 2]
                                    let slowCurr = slowEma[slowEma.Length - 1]
                                    let slowPrev = slowEma[slowEma.Length - 2]

                                    let rawDirection =
                                        if fastPrev <= slowPrev && fastCurr > slowCurr then 1.0m
                                        elif fastPrev >= slowPrev && fastCurr < slowCurr then -1.0m
                                        else 0m

                                    let weight = rawDirection * signalWeight

                                    let ctx' = ctx |> TradingContext.withSignalWeight "ema-signal" weight

                                    let dirStr =
                                        if rawDirection > 0m then "BUY"
                                        elif rawDirection < 0m then "SELL"
                                        else "NEUTRAL"

                                    return
                                        Continue(
                                            ctx',
                                            $"EMA crossover signal: {dirStr} (fast={fastCurr:F4}, slow={slowCurr:F4}, weight={weight:F2})"
                                        )
                    } }

        { Key = "ema-signal"
          Name = "Exponential Moving Average Signal"
          Description = "Generates buy/sell signals based on EMA crossover."
          Category = StepCategory.Signal
          Icon = "fa-chart-line"
          ParameterSchema =
            { Parameters =
                [ { Key = "fastPeriod"
                    Name = "Fast Period"
                    Description = "Period for the fast EMA"
                    Type = Int(Some 2, Some 200)
                    Required = false
                    DefaultValue = Some(IntValue 9)
                    Group = Some "Indicator" }
                  { Key = "slowPeriod"
                    Name = "Slow Period"
                    Description = "Period for the slow EMA"
                    Type = Int(Some 5, Some 500)
                    Required = false
                    DefaultValue = Some(IntValue 21)
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
          RequiredCandleData = fun _ -> []
          Create = create }
