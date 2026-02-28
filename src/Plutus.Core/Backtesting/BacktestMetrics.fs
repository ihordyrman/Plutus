namespace Plutus.Core.Backtesting

open System
open Plutus.Core.Domain

module BacktestMetrics =

    type Metrics =
        { TotalReturn: decimal
          FinalCapital: decimal
          TotalTrades: int
          WinningTrades: int
          LosingTrades: int
          WinRate: decimal
          AverageWin: decimal
          AverageLoss: decimal
          ProfitFactor: decimal
          LargestWin: decimal
          LargestLoss: decimal
          MaxDrawdownPct: decimal
          SharpeRatio: decimal
          AverageHoldingPeriod: TimeSpan
          EquityCurve: (DateTime * decimal) list }

    let calculate
        (initialCapital: decimal)
        (trades: BacktestTrade list)
        (equityPoints: BacktestEquityPoint list)
        : Metrics
        =
        let pairs =
            trades
            |> List.sortBy _.CandleTime
            |> List.chunkBySize 2
            |> List.filter (fun chunk -> chunk.Length = 2)

        let pnls =
            pairs
            |> List.map (fun pair ->
                let buy = pair[0]
                let sell = pair[1]
                (sell.Price - buy.Price) * sell.Quantity, sell.Price > buy.Price, sell.CandleTime - buy.CandleTime
            )

        let wins = pnls |> List.filter (fun (_, w, _) -> w)
        let losses = pnls |> List.filter (fun (_, w, _) -> not w)
        let winPnls = wins |> List.map (fun (p, _, _) -> p)
        let lossPnls = losses |> List.map (fun (p, _, _) -> p)

        let equityCurve = equityPoints |> List.map (fun ep -> ep.CandleTime, ep.Equity)
        let equityValues = equityPoints |> List.map _.Equity

        let finalCapital = if equityValues.IsEmpty then initialCapital else equityValues |> List.last

        let maxDrawdownPct =
            if equityValues.IsEmpty then
                0m
            else
                let mutable peak = equityValues.Head
                let mutable maxDd = 0m

                for equity in equityValues do
                    if equity > peak then
                        peak <- equity

                    let dd = if peak > 0m then (peak - equity) / peak * 100m else 0m

                    if dd > maxDd then
                        maxDd <- dd

                maxDd

        let sharpeRatio =
            if equityValues.Length < 2 then
                0m
            else
                let returns =
                    equityValues
                    |> List.pairwise
                    |> List.map (fun (prev, curr) -> if prev > 0m then (curr - prev) / prev else 0m)

                let mean = returns |> List.average
                let variance = returns |> List.map (fun r -> (r - mean) * (r - mean)) |> List.average
                let stdDev = decimal (sqrt (float variance))
                if stdDev = 0m then 0m else (mean / stdDev) * (decimal (sqrt 365.0))

        let grossProfit = winPnls |> List.sum |> max 0m
        let grossLoss = lossPnls |> List.sum |> abs

        let avgHolding =
            if pnls.IsEmpty then
                TimeSpan.Zero
            else
                let totalTicks = pnls |> List.sumBy (fun (_, _, dur) -> float dur.Ticks)
                TimeSpan.FromTicks(int64 (totalTicks / float pnls.Length))

        { TotalReturn = if initialCapital > 0m then (finalCapital - initialCapital) / initialCapital * 100m else 0m
          FinalCapital = finalCapital
          TotalTrades = pairs.Length
          WinningTrades = wins.Length
          LosingTrades = losses.Length
          WinRate = if pairs.IsEmpty then 0m else decimal wins.Length / decimal pairs.Length * 100m
          AverageWin = if winPnls.IsEmpty then 0m else winPnls |> List.average
          AverageLoss = if lossPnls.IsEmpty then 0m else lossPnls |> List.average
          ProfitFactor = if grossLoss = 0m then 0m else grossProfit / grossLoss
          LargestWin = if winPnls.IsEmpty then 0m else winPnls |> List.max
          LargestLoss = if lossPnls.IsEmpty then 0m else lossPnls |> List.min
          MaxDrawdownPct = maxDrawdownPct
          SharpeRatio = sharpeRatio
          AverageHoldingPeriod = avgHolding
          EquityCurve = equityCurve }
