namespace Plutus.MarketData

open System
open System.Threading
open System.Threading.Tasks
open Plutus.MarketData.Domain
open Plutus.Shared.Domain
open Plutus.Shared.Errors

// Instruments

type UpsertBatch = Instrument list -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBaseCurrency = MarketType -> InstrumentType -> CancellationToken -> Task<Result<Currency list, ServiceError>>

type GetQuoteCurrency =
    MarketType -> InstrumentType -> Currency -> CancellationToken -> Task<Result<Currency list, ServiceError>>

type InstrumentPorts =
    { UpsertBatch: UpsertBatch
      GetBaseCurrency: GetBaseCurrency
      GetQuoteCurrency: GetQuoteCurrency }

// Candlesticks

type CandlestickQuery =
    { Instrument: Instrument
      MarketType: MarketType
      Interval: Interval
      FromDate: DateTime option
      ToDate: DateTime option
      Limit: int option }

type GetLatestCandlestick =
    Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<Candlestick option, ServiceError>>

type GetOldestCandlestick =
    Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<Candlestick option, ServiceError>>

type QueryCandlesticks = CandlestickQuery -> CancellationToken -> Task<Result<Candlestick list, ServiceError>>
type SaveCandlesticks = Candlestick list -> CancellationToken -> Task<Result<int, ServiceError>>
type DeleteCandlesticksByInstrument = InstrumentId -> CancellationToken -> Task<Result<int, ServiceError>>
type GetDistinctIntervals = CancellationToken -> Task<Result<Interval list, ServiceError>>
type GetDistinctInstrumentCount = Interval -> CancellationToken -> Task<Result<int, ServiceError>>

type GetWeeklyCoveragePaged =
    Interval -> int -> int -> CancellationToken -> Task<Result<WeeklyCoverage list, ServiceError>>

type GetWeeklyCoverage = Interval -> CancellationToken -> Task<Result<WeeklyCoverage list, ServiceError>>

type FindCandlestickGaps =
    Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<CandlestickGap list, ServiceError>>

type CandlestickPorts =
    { GetLatest: GetLatestCandlestick
      GetOldest: GetOldestCandlestick
      FindGaps: FindCandlestickGaps
      Query: QueryCandlesticks
      Save: SaveCandlesticks
      DeleteByInstrument: DeleteCandlesticksByInstrument
      GetDistinctIntervals: GetDistinctIntervals
      GetDistinctInstrumentCount: GetDistinctInstrumentCount
      GetWeeklyCoveragePaged: GetWeeklyCoveragePaged
      GetWeeklyCoverage: GetWeeklyCoverage }
