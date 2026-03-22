namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type GetOpen = PipelineId -> CancellationToken -> Task<Result<Position, ServiceError>>
type Create = CreatePositionRequest -> CancellationToken -> Task<Result<Position, ServiceError>>

type PositionPorts = { GetOpen: GetOpen; Create: Create }
