namespace Plutus.Core.Infrastructure

open System

type StepOutcome =
    | Success
    | Stopped
    | Failed

[<CLIMutable>]
type ExecutionLog =
    { Id: int
      PipelineId: int
      ExecutionId: string
      StepTypeKey: string
      Outcome: StepOutcome
      Message: string
      ContextSnapshot: string
      StartTime: DateTime
      EndTime: DateTime }

module ExecutionLog =
    let create pipelineId executionId stepTypeKey startTime : ExecutionLog =
        { Id = 0
          PipelineId = pipelineId
          ExecutionId = executionId
          StepTypeKey = stepTypeKey
          Outcome = Success
          Message = ""
          ContextSnapshot = "{}"
          StartTime = startTime
          EndTime = startTime }
