namespace Plutus.Core.Pipelines.Trading

module Indicators =

    let sma (period: int) (series: decimal list) : decimal option =
        if series.Length < period || period <= 0 then
            None
        else
            let window = series |> List.skip (series.Length - period)
            Some(List.sum window / decimal period)

    let emaSeries (period: int) (series: decimal list) : decimal list =
        if series.Length < period || period <= 0 then
            []
        else
            let k = 2.0m / (decimal period + 1.0m)
            let seed = series |> List.take period |> List.sum |> (fun s -> s / decimal period)

            let rest = series |> List.skip period

            rest
            |> List.fold
                (fun acc price ->
                    let prev = List.head acc
                    let emaVal = price * k + prev * (1.0m - k)
                    emaVal :: acc
                )
                [ seed ]
            |> List.rev

    let ema (period: int) (series: decimal list) : decimal option =
        match emaSeries period series with
        | [] -> None
        | xs -> Some(List.last xs)

    let stdDev (series: decimal list) : decimal option =
        match series with
        | []
        | [ _ ] -> None
        | _ ->
            let n = decimal series.Length
            let mean = List.sum series / n

            let variance = series |> List.sumBy (fun x -> (x - mean) * (x - mean)) |> (fun s -> s / n)

            Some(decimal (sqrt (double variance)))

    let rollingStdDev (window: int) (series: decimal list) : decimal list =
        if series.Length < window || window <= 1 then
            []
        else
            [ for i in 0 .. series.Length - window do
                  let slice = series |> List.skip i |> List.take window

                  match stdDev slice with
                  | Some sd -> sd
                  | None -> 0m ]

    let returns (series: decimal list) : decimal list =
        match series with
        | []
        | [ _ ] -> []
        | _ ->
            series
            |> List.pairwise
            |> List.map (fun (prev, curr) -> if prev = 0m then 0m else (curr - prev) / prev)

    let vwap (data: (decimal * decimal) list) : decimal option =
        if data.IsEmpty then
            None
        else
            let totalVolume = data |> List.sumBy snd

            if totalVolume = 0m then
                None
            else
                let weightedSum = data |> List.sumBy (fun (tp, vol) -> tp * vol)
                Some(weightedSum / totalVolume)

    let momentum (lookback: int) (series: decimal list) : decimal option =
        if series.Length < lookback + 1 || lookback <= 0 then
            None
        else
            let current = List.last series
            let past = series[series.Length - lookback - 1]

            if past = 0m then None else Some((current - past) / past * 100m)
