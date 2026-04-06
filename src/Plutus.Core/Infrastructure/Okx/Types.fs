namespace Plutus.Core.Infrastructure.Okx

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Serialization

type internal InstrumentType =
    | Spot
    | Margin
    | Swap
    | Futures
    | Option
    | Any

[<JsonConverter(typeof<JsonStringEnumConverter>)>]
type internal OkxResponseCode =
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
type internal OkxAssetsValuationDetail =
    { [<JsonPropertyName("classic")>]
      Classic: string
      [<JsonPropertyName("earn")>]
      Earn: string
      [<JsonPropertyName("funding")>]
      Funding: string
      [<JsonPropertyName("trading")>]
      Trading: string }

[<CLIMutable>]
type internal OkxAssetsValuation =
    { [<JsonPropertyName("details")>]
      Details: OkxAssetsValuationDetail
      [<JsonPropertyName("totalBal")>]
      TotalBalance: string
      [<JsonPropertyName("ts")>]
      Timestamp: string }

[<CLIMutable>]
type internal OkxBalanceDetail =
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
type internal OkxAccountBalance =
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
type internal OkxFundingBalance =
    { [<JsonPropertyName("availBal")>]
      AvailBal: string
      [<JsonPropertyName("bal")>]
      Bal: string
      [<JsonPropertyName("ccy")>]
      Ccy: string
      [<JsonPropertyName("frozenBal")>]
      FrozenBal: string }

[<CLIMutable>]
type internal OkxHttpResponse<'T> =
    { [<JsonPropertyName("code")>]
      Code: OkxResponseCode
      [<JsonPropertyName("msg")>]
      Message: string
      [<JsonPropertyName("data")>]
      Data: 'T option }

type internal OkxCandlestick =
    { Timestamp: DateTime
      Open: decimal
      High: decimal
      Low: decimal
      Close: decimal
      Volume: decimal
      VolumeCurrency: decimal
      VolumeQuoteCurrency: decimal
      IsCompleted: bool }

type internal OkxCandlestickConverter() =
    inherit JsonConverter<OkxCandlestick>()

    override _.Read(reader, _, options) =
        let data = JsonSerializer.Deserialize<string[]>(&reader, options)

        if isNull data then
            raise (JsonException "Failed to deserialize candlestick data")

        let dec (value: string) =
            Decimal.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture)

        let double (value: string) =
            Double.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture)

        { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(int64 (double data[0])).UtcDateTime
          Open = dec data[1]
          High = dec data[2]
          Low = dec data[3]
          Close = dec data[4]
          Volume = dec data[5]
          VolumeCurrency = dec data[6]
          VolumeQuoteCurrency = dec data[7]
          IsCompleted = data[8] = "1" }

    override _.Write(_, _, _) =
        raise (NotSupportedException "Serialization not supported")

[<CLIMutable>]
type internal OkxPlaceOrderRequest =
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
type internal OkxPlaceOrderResponse =
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
type internal OkxOrderDetail =
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

type internal OkxInstrument =
    { [<JsonPropertyName("instId")>]
      InstrumentId: string
      [<JsonPropertyName("instType")>]
      InstrumentType: string
      [<JsonPropertyName("baseCcy")>]
      BaseCurrency: string
      [<JsonPropertyName("quoteCcy")>]
      QuoteCurrency: string }
