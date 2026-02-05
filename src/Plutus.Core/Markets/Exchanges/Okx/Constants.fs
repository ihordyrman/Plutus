namespace Plutus.Core.Markets.Exchanges.Okx

module SocketEndpoints =

    [<Literal>]
    let PUBLIC_WS_URL = "wss://ws.okx.com:8443/ws/v5/public"

    [<Literal>]
    let PRIVATE_WS_URL = "wss://ws.okx.com:8443/ws/v5/private"

    [<Literal>]
    let DEMO_PUBLIC_WS_URL = "wss://wspap.okx.com:8443/ws/v5/public"

    [<Literal>]
    let DEMO_PRIVATE_WS_URL = "wss://wspap.okx.com:8443/ws/v5/private"

module CandlestickTimeframes =
    [<Literal>]
    let OneMinute = "1m"

    [<Literal>]
    let ThreeMinutes = "3m"

    [<Literal>]
    let FiveMinutes = "5m"

    [<Literal>]
    let FifteenMinutes = "15m"

    [<Literal>]
    let ThirtyMinutes = "30m"

    [<Literal>]
    let OneHour = "1H"

    [<Literal>]
    let TwoHours = "2H"

    [<Literal>]
    let FourHours = "4H"

    [<Literal>]
    let SixHoursUtc = "6Hutc"

    [<Literal>]
    let TwelveHoursUtc = "12Hutc"

    [<Literal>]
    let OneDayUtc = "1Dutc"

    [<Literal>]
    let TwoDaysUtc = "2Dutc"

    [<Literal>]
    let ThreeDaysUtc = "3Dutc"

    [<Literal>]
    let OneWeekUtc = "1Wutc"

    [<Literal>]
    let OneMonthUtc = "1Mutc"

    [<Literal>]
    let ThreeMonthsUtc = "3Mutc"

module InstrumentType =
    [<Literal>]
    let Spot = "SPOT"

    [<Literal>]
    let Margin = "MARGIN"

    [<Literal>]
    let Swap = "SWAP"

    [<Literal>]
    let Futures = "FUTURES"

    [<Literal>]
    let Option = "OPTION"

    [<Literal>]
    let Any = "ANY"
