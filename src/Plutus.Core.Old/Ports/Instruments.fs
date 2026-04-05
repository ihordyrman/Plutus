namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type UpsertBatch = Instrument list -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBaseCurrency = MarketType -> InstrumentType -> CancellationToken -> Task<Result<Currency list, ServiceError>>

type GetQuoteCurrency =
    MarketType -> InstrumentType -> Currency -> CancellationToken -> Task<Result<Currency list, ServiceError>>

type InstrumentPorts =
    { UpsertBatch: UpsertBatch; GetBaseCurrency: GetBaseCurrency; GetQuoteCurrency: GetQuoteCurrency }
