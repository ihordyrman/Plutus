namespace Plutus.Core.Ports

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type ExecutionLogFilter =
    { PipelineId: PipelineId
      Outcome: StepOutcome option
      DateFrom: DateTime option
      DateTo: DateTime option }

type GetByExecutionId = ExecutionId -> CancellationToken -> Task<Result<ExecutionLog list, ServiceError>>
type GetFilteredExecutions = ExecutionLogFilter -> int -> int -> CancellationToken -> Task<Result<ExecutionSummary list, ServiceError>>
type CountFilteredExecutions = ExecutionLogFilter -> CancellationToken -> Task<Result<int, ServiceError>>

type ExecutionLogPorts =
    { GetByExecutionId: GetByExecutionId
      GetFilteredExecutions: GetFilteredExecutions
      CountFilteredExecutions: CountFilteredExecutions }
