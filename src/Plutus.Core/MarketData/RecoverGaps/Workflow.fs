namespace Plutus.Core.MarketData.RecoverGaps

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.MarketData.Domain
open Plutus.Core.Shared.Domain
open Plutus.Core.Shared.Errors

type CandlestickGap = { GapStart: DateTime; GapEnd: DateTime }

type FindCandlestickGaps =
    Instrument -> MarketType -> Interval -> CancellationToken -> Task<Result<CandlestickGap list, ServiceError>>
