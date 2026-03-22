namespace Plutus.Core.Ports

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type CandlestickQuery =
    { Instrument: Instrument
      MarketType: MarketType
      Interval: Interval
      FromDate: DateTime option
      ToDate: DateTime option
      Limit: int option }

type GetLatestCandlestick = Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<Candlestick option, ServiceError>>
type GetOldestCandlestick = Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<Candlestick option, ServiceError>>
type FindCandlestickGaps = Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<CandlestickGap list, ServiceError>>
type QueryCandlesticks = CandlestickQuery -> CancellationToken -> Task<Result<Candlestick list, ServiceError>>
type SaveCandlesticks = Candlestick list -> CancellationToken -> Task<Result<int, ServiceError>>
type DeleteCandlesticksByInstrument = InstrumentId -> CancellationToken -> Task<Result<int, ServiceError>>
type GetDistinctIntervals = CancellationToken -> Task<Result<Interval list, ServiceError>>
type GetDistinctInstrumentCount = Interval -> CancellationToken -> Task<Result<int, ServiceError>>
type GetWeeklyCoveragePaged = Interval -> int -> int -> CancellationToken -> Task<Result<WeeklyCoverage list, ServiceError>>
type GetWeeklyCoverage = Interval -> CancellationToken -> Task<Result<WeeklyCoverage list, ServiceError>>

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
