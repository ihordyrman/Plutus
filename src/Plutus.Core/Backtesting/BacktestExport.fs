namespace Plutus.Core.Backtesting

open System
open System.IO
open ClosedXML.Excel
open Plutus.Core.Domain

module BacktestExport =

    let private buildTradePairs (trades: BacktestTrade list) =
        trades
        |> List.sortBy _.CandleTime
        |> List.chunkBySize 2
        |> List.filter (fun chunk -> chunk.Length = 2)

    let private addSummarySheet (wb: XLWorkbook) (run: BacktestRun) (pipeline: Pipeline) (metrics: BacktestMetrics) =
        let ws = wb.Worksheets.Add("Summary")
        let dateFmt (d: DateTime) = d.ToString("yyyy-MM-dd")

        [ "Instrument", string pipeline.Instrument
          "Period", $"{dateFmt run.StartDate} to {dateFmt run.EndDate}"
          "Interval", $"{run.IntervalMinutes} min"
          "Initial Capital", $"%.2f{run.InitialCapital}"
          "Final Capital", $"%.2f{metrics.FinalCapital}"
          "Total Return", $"%.2f{metrics.TotalReturn}%%"
          "Win Rate", $"%.2f{metrics.WinRate}%%"
          "Total Trades", string metrics.TotalTrades
          "Winning Trades", string metrics.WinningTrades
          "Losing Trades", string metrics.LosingTrades
          "Profit Factor", $"%.2f{metrics.ProfitFactor}"
          "Average Win", $"%.2f{metrics.AverageWin}"
          "Average Loss", $"%.2f{metrics.AverageLoss}"
          "Largest Win", $"%.2f{metrics.LargestWin}"
          "Largest Loss", $"%.2f{metrics.LargestLoss}"
          "Max Drawdown", $"%.2f{metrics.MaxDrawdownPct}%%"
          "Sharpe Ratio", $"%.4f{metrics.SharpeRatio}" ]
        |> List.iteri (fun i (label, value) ->
            let row = i + 1
            ws.Cell(row, 1).Value <- label
            ws.Cell(row, 2).Value <- value
        )

        ws.Column(1).Width <- 20.0
        ws.Column(2).Width <- 25.0
        ws.Column(1).Style.Font.Bold <- true

    let private addTradesSheet (wb: XLWorkbook) (trades: BacktestTrade list) (initialCapital: decimal) =
        let ws = wb.Worksheets.Add("Trades")

        [| "#"
           "Entry Time"
           "Exit Time"
           "Entry Price"
           "Exit Price"
           "Qty"
           "Fee"
           "PnL"
           "PnL%"
           "Balance" |]
        |> Array.iteri (fun i header -> ws.Cell(1, i + 1).Value <- header)

        ws.Row(1).Style.Font.Bold <- true

        buildTradePairs trades
        |> List.fold
            (fun (acc, row) pair ->
                let buy = pair[0]
                let sell = pair[1]
                let pnl = (sell.Price - buy.Price) * sell.Quantity
                let fee = buy.Fee + sell.Fee
                let pnlPct = if buy.Price > 0m then pnl / (buy.Price * sell.Quantity) * 100m else 0m
                let balance = acc + pnl - fee
                ws.Cell(row, 1).Value <- string (row - 1)
                ws.Cell(row, 2).Value <- buy.CandleTime.ToString("yyyy-MM-dd HH:mm")
                ws.Cell(row, 3).Value <- sell.CandleTime.ToString("yyyy-MM-dd HH:mm")
                ws.Cell(row, 4).Value <- buy.Price
                ws.Cell(row, 5).Value <- sell.Price
                ws.Cell(row, 6).Value <- sell.Quantity
                ws.Cell(row, 7).Value <- fee
                ws.Cell(row, 8).Value <- pnl
                ws.Cell(row, 9).Value <- pnlPct
                ws.Cell(row, 10).Value <- balance

                balance, (row + 1)
            )
            (initialCapital, 2)
        |> ignore

        ws.Columns().AdjustToContents() |> ignore

    let private addEquitySheet (wb: XLWorkbook) (equity: BacktestEquityPoint list) =
        let ws = wb.Worksheets.Add("Equity Curve")
        ws.Cell(1, 1).Value <- "Timestamp"
        ws.Cell(1, 2).Value <- "Equity"
        ws.Cell(1, 3).Value <- "Drawdown%"
        ws.Row(1).Style.Font.Bold <- true

        equity
        |> List.iteri (fun i ep ->
            let row = i + 2
            ws.Cell(row, 1).Value <- ep.CandleTime.ToString("yyyy-MM-dd HH:mm")
            ws.Cell(row, 2).Value <- ep.Equity
            ws.Cell(row, 3).Value <- ep.Drawdown
        )

        ws.Columns().AdjustToContents() |> ignore

    let private addConfigSheet (wb: XLWorkbook) (steps: PipelineStep list) =
        let ws = wb.Worksheets.Add("Configuration")
        ws.Cell(1, 1).Value <- "Order"
        ws.Cell(1, 2).Value <- "Step"
        ws.Cell(1, 3).Value <- "Name"
        ws.Cell(1, 4).Value <- "Enabled"
        ws.Cell(1, 5).Value <- "Parameters"
        ws.Row(1).Style.Font.Bold <- true

        steps
        |> List.iteri (fun i x ->
            let row = i + 2
            ws.Cell(row, 1).Value <- x.Order
            ws.Cell(row, 2).Value <- x.StepTypeKey
            ws.Cell(row, 3).Value <- x.Name
            ws.Cell(row, 4).Value <- (if x.IsEnabled then "Yes" else "No")

            let paramStr = x.Parameters |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}") |> String.concat "; "
            ws.Cell(row, 5).Value <- paramStr
        )

        ws.Columns().AdjustToContents() |> ignore

    let generate
        (run: BacktestRun)
        (pipeline: Pipeline)
        (steps: PipelineStep list)
        (trades: BacktestTrade list)
        (equity: BacktestEquityPoint list)
        (metrics: BacktestMetrics)
        : MemoryStream
        =
        let wb = new XLWorkbook()
        addSummarySheet wb run pipeline metrics
        addTradesSheet wb trades run.InitialCapital
        addEquitySheet wb equity
        addConfigSheet wb steps
        let ms = new MemoryStream()
        wb.SaveAs(ms)
        ms.Position <- 0L
        ms
