namespace Plutus.Core.Pipelines.Trading

open System
open System.Data
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories

module MacdSignal =
    let private timeframes = [ "1m"; "5m"; "15m"; "30m"; "1H"; "4H"; "1Dutc" ]

    let macd: StepDefinition<TradingContext> =
        let create (params': ValidatedParams) (services: IServiceProvider) : Step<TradingContext> =
            let fastPeriod = params' |> ValidatedParams.getInt "fastPeriod" 12
            let slowPeriod = params' |> ValidatedParams.getInt "slowPeriod" 26
            let signalPeriod = params' |> ValidatedParams.getInt "signalPeriod" 9
            let timeframe = params' |> ValidatedParams.getString "timeframe" "1m"
            let signalWeight = params' |> ValidatedParams.getDecimal "signalWeight" 1.0m

            { key = "macd-signal"
              execute =
                fun ctx ct ->
                    task {
                        match ctx.ActiveOrderId, ctx.Action with
                        | Some _, Hold -> return Continue(ctx, "Holding position, skip MACD signal.")
                        | _ ->
                            use scope = services.CreateScope()
                            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                            let candleCount = slowPeriod + signalPeriod
                            let toDate = TradingContext.getData<DateTime> "backtest:currentTime" ctx

                            match!
                                CandlestickRepository.query
                                    db
                                    ctx.Symbol
                                    ctx.MarketType
                                    timeframe
                                    None
                                    toDate
                                    (Some candleCount)
                                    ct
                            with
                            | Error err -> return Fail $"Error fetching candles: {err}"
                            | Ok candles when candles.Length < candleCount ->
                                return
                                    Continue(
                                        ctx,
                                        $"Insufficient candle data ({candles.Length}/{candleCount}), skip MACD signal."
                                    )
                            | Ok candles ->
                                let closes = candles |> List.rev |> List.map _.Close

                                let fastEma = Indicators.emaSeries fastPeriod closes
                                let slowEma = Indicators.emaSeries slowPeriod closes

                                let offset = fastEma.Length - slowEma.Length
                                let fastAligned = fastEma |> List.skip offset

                                let macdLine = List.map2 (fun f s -> f - s) fastAligned slowEma

                                let signalLine = Indicators.emaSeries signalPeriod macdLine

                                if signalLine.Length < 2 then
                                    return Continue(ctx, "Not enough MACD signal line values for crossover detection.")
                                else
                                    let macdOffset = macdLine.Length - signalLine.Length
                                    let macdAligned = macdLine |> List.skip macdOffset

                                    let macdCurr = macdAligned.[macdAligned.Length - 1]
                                    let macdPrev = macdAligned.[macdAligned.Length - 2]
                                    let sigCurr = signalLine.[signalLine.Length - 1]
                                    let sigPrev = signalLine.[signalLine.Length - 2]

                                    let rawDirection =
                                        if macdPrev <= sigPrev && macdCurr > sigCurr then 1.0m
                                        elif macdPrev >= sigPrev && macdCurr < sigCurr then -1.0m
                                        else 0m

                                    let weight = rawDirection * signalWeight

                                    let ctx' = ctx |> TradingContext.withSignalWeight "macd-signal" weight

                                    let dirStr =
                                        if rawDirection > 0m then "BUY"
                                        elif rawDirection < 0m then "SELL"
                                        else "NEUTRAL"

                                    return
                                        Continue(
                                            ctx',
                                            $"MACD signal: {dirStr} (macd={macdCurr:F4}, signal={sigCurr:F4}, weight={weight:F2})"
                                        )
                    } }

        { Key = "macd-signal"
          Name = "MACD Signal"
          Description = "Generates buy/sell signals based on MACD crossover."
          Category = StepCategory.Signal
          Icon = "fa-chart-bar"
          ParameterSchema =
            { Parameters =
                [ { Key = "fastPeriod"
                    Name = "Fast Period"
                    Description = "Period for the fast EMA"
                    Type = Int(Some 2, Some 200)
                    Required = false
                    DefaultValue = Some(IntValue 12)
                    Group = Some "Indicator" }
                  { Key = "slowPeriod"
                    Name = "Slow Period"
                    Description = "Period for the slow EMA"
                    Type = Int(Some 5, Some 500)
                    Required = false
                    DefaultValue = Some(IntValue 26)
                    Group = Some "Indicator" }
                  { Key = "signalPeriod"
                    Name = "Signal Period"
                    Description = "Period for the signal line EMA"
                    Type = Int(Some 2, Some 100)
                    Required = false
                    DefaultValue = Some(IntValue 9)
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
