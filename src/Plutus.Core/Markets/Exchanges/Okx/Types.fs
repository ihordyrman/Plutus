namespace Plutus.Core.Markets.Exchanges.Okx

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Serialization

type InstrumentType =
    | Spot
    | Margin
    | Swap
    | Futures
    | Option
    | Any

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type OkxResponseCode =
    | Success = 0
    | InvalidTimestamp = 60004
    | InvalidApiKey = 60005
    | TimestampExpired = 60006
    | InvalidSignature = 60007
    | NoSubscriptionChannels = 60008
    | LoginFailed = 60009
    | PleaseLogin = 60011
    | InvalidRequest = 60012
    | InvalidArguments = 60013
    | RequestsTooFrequent = 60014
    | WrongUrlOrChannel = 60018
    | InvalidOperation = 60019
    | MultipleLoginsNotAllowed = 60030
    | InternalLoginError = 63999
    | RateLimitReached = 50011
    | SystemBusy = 50026

[<CLIMutable>]
type OkxAssetsValuationDetail =
    { [<JsonPropertyName("classic")>]
      Classic: string
      [<JsonPropertyName("earn")>]
      Earn: string
      [<JsonPropertyName("funding")>]
      Funding: string
      [<JsonPropertyName("trading")>]
      Trading: string }

[<CLIMutable>]
type OkxAssetsValuation =
    { [<JsonPropertyName("details")>]
      Details: OkxAssetsValuationDetail
      [<JsonPropertyName("totalBal")>]
      TotalBalance: string
      [<JsonPropertyName("ts")>]
      Timestamp: string }

[<CLIMutable>]
type OkxBalanceDetail =
    { [<JsonPropertyName("accAvgPx")>]
      AccAvgPx: string
      [<JsonPropertyName("autoLendAmt")>]
      AutoLendAmt: string
      [<JsonPropertyName("autoLendMtAmt")>]
      AutoLendMtAmt: string
      [<JsonPropertyName("autoLendStatus")>]
      AutoLendStatus: string
      [<JsonPropertyName("availBal")>]
      AvailBal: string
      [<JsonPropertyName("availEq")>]
      AvailEq: string
      [<JsonPropertyName("borrowFroz")>]
      BorrowFroz: string
      [<JsonPropertyName("cashBal")>]
      CashBal: string
      [<JsonPropertyName("ccy")>]
      Ccy: string
      [<JsonPropertyName("clSpotInUseAmt")>]
      ClSpotInUseAmt: string
      [<JsonPropertyName("colBorrAutoConversion")>]
      ColBorrAutoConversion: string
      [<JsonPropertyName("colRes")>]
      ColRes: string
      [<JsonPropertyName("collateralEnabled")>]
      CollateralEnabled: bool
      [<JsonPropertyName("collateralRestrict")>]
      CollateralRestrict: bool
      [<JsonPropertyName("crossLiab")>]
      CrossLiab: string
      [<JsonPropertyName("disEq")>]
      DisEq: string
      [<JsonPropertyName("eq")>]
      Eq: string
      [<JsonPropertyName("eqUsd")>]
      EqUsd: string
      [<JsonPropertyName("fixedBal")>]
      FixedBal: string
      [<JsonPropertyName("frozenBal")>]
      FrozenBal: string
      [<JsonPropertyName("imr")>]
      Imr: string
      [<JsonPropertyName("interest")>]
      Interest: string
      [<JsonPropertyName("isoEq")>]
      IsoEq: string
      [<JsonPropertyName("isoLiab")>]
      IsoLiab: string
      [<JsonPropertyName("isoUpl")>]
      IsoUpl: string
      [<JsonPropertyName("liab")>]
      Liab: string
      [<JsonPropertyName("maxLoan")>]
      MaxLoan: string
      [<JsonPropertyName("maxSpotInUse")>]
      MaxSpotInUse: string
      [<JsonPropertyName("mgnRatio")>]
      MgnRatio: string
      [<JsonPropertyName("mmr")>]
      Mmr: string
      [<JsonPropertyName("notionalLever")>]
      NotionalLever: string
      [<JsonPropertyName("openAvgPx")>]
      OpenAvgPx: string
      [<JsonPropertyName("ordFrozen")>]
      OrdFrozen: string
      [<JsonPropertyName("rewardBal")>]
      RewardBal: string
      [<JsonPropertyName("smtSyncEq")>]
      SmtSyncEq: string
      [<JsonPropertyName("spotBal")>]
      SpotBal: string
      [<JsonPropertyName("spotCopyTradingEq")>]
      SpotCopyTradingEq: string
      [<JsonPropertyName("spotInUseAmt")>]
      SpotInUseAmt: string
      [<JsonPropertyName("spotIsoBal")>]
      SpotIsoBal: string
      [<JsonPropertyName("spotUpl")>]
      SpotUpl: string
      [<JsonPropertyName("spotUplRatio")>]
      SpotUplRatio: string
      [<JsonPropertyName("stgyEq")>]
      StgyEq: string
      [<JsonPropertyName("totalPnl")>]
      TotalPnl: string
      [<JsonPropertyName("totalPnlRatio")>]
      TotalPnlRatio: string
      [<JsonPropertyName("twap")>]
      Twap: string
      [<JsonPropertyName("uTime")>]
      UTime: string
      [<JsonPropertyName("upl")>]
      Upl: string
      [<JsonPropertyName("uplLiab")>]
      UplLiab: string }

[<CLIMutable>]
type OkxAccountBalance =
    { [<JsonPropertyName("adjEq")>]
      AdjEq: string
      [<JsonPropertyName("availEq")>]
      AvailEq: string
      [<JsonPropertyName("borrowFroz")>]
      BorrowFroz: string
      [<JsonPropertyName("imr")>]
      Imr: string
      [<JsonPropertyName("isoEq")>]
      IsoEq: string
      [<JsonPropertyName("mgnRatio")>]
      MgnRatio: string
      [<JsonPropertyName("mmr")>]
      Mmr: string
      [<JsonPropertyName("notionalUsd")>]
      NotionalUsd: string
      [<JsonPropertyName("notionalUsdForBorrow")>]
      NotionalUsdForBorrow: string
      [<JsonPropertyName("notionalUsdForFutures")>]
      NotionalUsdForFutures: string
      [<JsonPropertyName("notionalUsdForOption")>]
      NotionalUsdForOption: string
      [<JsonPropertyName("notionalUsdForSwap")>]
      NotionalUsdForSwap: string
      [<JsonPropertyName("ordFroz")>]
      OrdFroz: string
      [<JsonPropertyName("totalEq")>]
      TotalEq: string
      [<JsonPropertyName("uTime")>]
      UTime: string
      [<JsonPropertyName("upl")>]
      Upl: string
      [<JsonPropertyName("details")>]
      Details: OkxBalanceDetail list }

[<CLIMutable>]
type OkxFundingBalance =
    { [<JsonPropertyName("availBal")>]
      AvailBal: string
      [<JsonPropertyName("bal")>]
      Bal: string
      [<JsonPropertyName("ccy")>]
      Ccy: string
      [<JsonPropertyName("frozenBal")>]
      FrozenBal: string }

[<CLIMutable>]
type OkxHttpResponse<'T> =
    { [<JsonPropertyName("code")>]
      Code: OkxResponseCode
      [<JsonPropertyName("msg")>]
      Message: string
      [<JsonPropertyName("data")>]
      Data: 'T option }

type OkxCandlestick =
    { Data: string[] }

    member x.Timestamp =
        if x.Data.Length > 0 then
            DateTimeOffset
                .FromUnixTimeMilliseconds(
                    int64 (Double.Parse(x.Data.[0], NumberStyles.Any, NumberFormatInfo.InvariantInfo))
                )
                .UtcDateTime
        else
            DateTime.MinValue

    member x.Open =
        if x.Data.Length > 1 then
            Decimal.Parse(x.Data.[1], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.High =
        if x.Data.Length > 2 then
            Decimal.Parse(x.Data.[2], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.Low =
        if x.Data.Length > 3 then
            Decimal.Parse(x.Data.[3], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.Close =
        if x.Data.Length > 4 then
            Decimal.Parse(x.Data.[4], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.Volume =
        if x.Data.Length > 5 then
            Decimal.Parse(x.Data.[5], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.VolumeCurrency =
        if x.Data.Length > 6 then
            Decimal.Parse(x.Data.[6], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.VolumeQuoteCurrency =
        if x.Data.Length > 7 then
            Decimal.Parse(x.Data.[7], NumberStyles.Any, NumberFormatInfo.InvariantInfo)
        else
            0m

    member x.IsCompleted =
        if x.Data.Length > 8 then
            String.Equals(x.Data.[8], "1", StringComparison.OrdinalIgnoreCase)
        else
            false

type OkxCandlestickConverter() =
    inherit JsonConverter<OkxCandlestick>()

    override _.Read(reader: byref<Utf8JsonReader>, typeToConvert: Type, options: JsonSerializerOptions) =
        let data = JsonSerializer.Deserialize<string[]>(&reader, options)

        if isNull data then
            raise (JsonException("Failed to deserialize candlestick data"))

        { Data = data }

    override _.Write(writer: Utf8JsonWriter, value: OkxCandlestick, options: JsonSerializerOptions) =
        JsonSerializer.Serialize(writer, value.Data, options)

[<JsonConverter(typeof<OkxCandlestickConverter>)>]
type OkxCandlestickWrapper = OkxCandlestick

[<CLIMutable>]
type OkxPlaceOrderRequest =
    { [<JsonPropertyName("instId")>]
      InstrumentId: string
      [<JsonPropertyName("tdMode")>]
      TradeMode: string
      [<JsonPropertyName("side")>]
      Side: string
      [<JsonPropertyName("ordType")>]
      OrderType: string
      [<JsonPropertyName("sz")>]
      Size: string
      [<JsonPropertyName("px"); JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      Price: string option
      [<JsonPropertyName("clOrdId"); JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      ClientOrderId: string option
      [<JsonPropertyName("tag"); JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      Tag: string option
      [<JsonPropertyName("reduceOnly"); JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      ReduceOnly: bool option
      [<JsonPropertyName("tgtCcy"); JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
      TargetCurrency: string option }

[<CLIMutable>]
type OkxPlaceOrderResponse =
    { [<JsonPropertyName("ordId")>]
      OrderId: string
      [<JsonPropertyName("clOrdId")>]
      ClientOrderId: string
      [<JsonPropertyName("tag")>]
      Tag: string
      [<JsonPropertyName("sCode")>]
      StatusCode: string
      [<JsonPropertyName("sMsg")>]
      StatusMessage: string
      [<JsonPropertyName("ts")>]
      Timestamp: string option }

    member x.IsSuccess = x.StatusCode = "0"

[<CLIMutable>]
type OkxOrderDetail =
    { [<JsonPropertyName("ordId")>]
      OrderId: string
      [<JsonPropertyName("clOrdId")>]
      ClientOrderId: string
      [<JsonPropertyName("instId")>]
      InstrumentId: string
      [<JsonPropertyName("state")>]
      State: string
      [<JsonPropertyName("avgPx")>]
      AveragePrice: string
      [<JsonPropertyName("accFillSz")>]
      AccumulatedFillSize: string
      [<JsonPropertyName("fee")>]
      Fee: string
      [<JsonPropertyName("feeCcy")>]
      FeeCurrency: string
      [<JsonPropertyName("fillPx")>]
      LastFillPrice: string
      [<JsonPropertyName("fillSz")>]
      LastFillSize: string
      [<JsonPropertyName("fillTime")>]
      LastFillTime: string
      [<JsonPropertyName("side")>]
      Side: string
      [<JsonPropertyName("sz")>]
      Size: string
      [<JsonPropertyName("px")>]
      Price: string
      [<JsonPropertyName("ordType")>]
      OrderType: string
      [<JsonPropertyName("uTime")>]
      UpdateTime: string
      [<JsonPropertyName("cTime")>]
      CreateTime: string }

type OkxInstrument =
    { [<JsonPropertyName("instId")>]
      InstrumentId: string
      [<JsonPropertyName("instType")>]
      InstrumentType: string
      [<JsonPropertyName("baseCcy")>]
      BaseCurrency: string
      [<JsonPropertyName("quoteCcy")>]
      QuoteCurrency: string }
