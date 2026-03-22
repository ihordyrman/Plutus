namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type GetStepById = StepId -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
type GetStepsByPipelineId = PipelineId -> CancellationToken -> Task<Result<PipelineStep list, ServiceError>>
type CreateStep = PipelineStep -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
type UpdateStep = PipelineStep -> CancellationToken -> Task<Result<PipelineStep, ServiceError>>
type DeleteStep = StepId -> CancellationToken -> Task<Result<unit, ServiceError>>
type DeleteStepsByPipelineId = PipelineId -> CancellationToken -> Task<Result<int, ServiceError>>
type SetStepEnabled = StepId -> bool -> CancellationToken -> Task<Result<unit, ServiceError>>
type SwapStepOrders = PipelineStep -> PipelineStep -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetMaxStepOrder = PipelineId -> CancellationToken -> Task<Result<int, ServiceError>>

type PipelineStepPorts =
    { GetById: GetStepById
      GetByPipelineId: GetStepsByPipelineId
      Create: CreateStep
      Update: UpdateStep
      Delete: DeleteStep
      DeleteByPipelineId: DeleteStepsByPipelineId
      SetEnabled: SetStepEnabled
      SwapOrders: SwapStepOrders
      GetMaxOrder: GetMaxStepOrder }
