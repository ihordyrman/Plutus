namespace Plutus.Core.Domain

open System

type ExecutionId = private ExecutionId of string

module ExecutionId =
    let create (id: string) : Result<ExecutionId, string> =
        match id with
        | null
        | "" -> Error "Execution ID must be a non-empty string."
        | _ -> Ok(ExecutionId id)

    let value (ExecutionId id) = id

type ExecutionLog =
    { PipelineId: PipelineId
      ExecutionId: ExecutionId
      StepTypeKey: StepTypeKey
      Outcome: StepOutcome
      Message: NonEmptyString
      ContextSnapshot: string option
      StartTime: DateTime
      EndTime: DateTime }

type ExecutionSummary =
    { ExecutionId: ExecutionId
      StartTime: DateTime
      EndTime: DateTime
      StepCount: PositiveInt
      WorstOutcome: StepOutcome }
