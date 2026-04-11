namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type GetById = MarketId -> CancellationToken -> Task<Result<Market, ServiceError>>
type GetAll = CancellationToken -> Task<Result<Market list, ServiceError>>
type Count = CancellationToken -> Task<Result<int, ServiceError>>
type EnsureExists = MarketType -> CancellationToken -> Task<Result<unit, ServiceError>>

type MarketPorts = { GetById: GetById; GetAll: GetAll; Count: Count; EnsureExists: EnsureExists }
