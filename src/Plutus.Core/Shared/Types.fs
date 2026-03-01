namespace Plutus.Core.Shared

type Interval =
    | OneMinute
    | FiveMinutes
    | FifteenMinutes
    | ThirtyMinutes
    | OneHour
    | FourHours
    | OneDay

    override this.ToString() =
        match this with
        | OneMinute -> "1m"
        | FiveMinutes -> "5m"
        | FifteenMinutes -> "15m"
        | ThirtyMinutes -> "30m"
        | OneHour -> "1h"
        | FourHours -> "4h"
        | OneDay -> "1d"

type Instrument =
    { Base: string
      Quote: string }

    override this.ToString() = $"%s{this.Base}-%s{this.Quote}"

module Instrument =
    let parse (s: string) =
        match s.Split '-' with
        | [| baseCcy; quoteCcy |] -> { Base = baseCcy; Quote = quoteCcy }
        | _ -> failwith $"Invalid instrument format: {s}"

module Interval =
    let parse (s: string) =
        match s with
        | "1m" -> Interval.OneMinute
        | "5m" -> Interval.FiveMinutes
        | "15m" -> Interval.FifteenMinutes
        | "30m" -> Interval.ThirtyMinutes
        | "1h" -> Interval.OneHour
        | "4h" -> Interval.FourHours
        | "1d" -> Interval.OneDay
        | _ -> failwith $"Invalid interval format: {s}"
