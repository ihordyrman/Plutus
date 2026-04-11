namespace Plutus.MarketData.RecoverGaps

open System
open System.Threading
open System.Threading.Tasks
open Plutus.MarketData.Domain
open Plutus.Shared.Domain
open Plutus.Shared.Errors

type CandlestickGap = { GapStart: DateTime; GapEnd: DateTime }

type FindCandlestickGaps =
    Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<CandlestickGap list, ServiceError>>
