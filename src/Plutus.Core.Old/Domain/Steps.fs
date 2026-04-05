namespace Plutus.Core.Domain

type StepKey = private StepKey of string
type StepTypeKey = private StepTypeKey of string

type StepOutcome =
    | Success
    | Stopped
    | Failed

type StepCategory =
    | Validation = 0
    | Risk = 1
    | Signal = 2
    | Execution = 3

module StepKey =
    let create (value: string) : Result<StepKey, string> =
        match value with
        | null
        | "" -> Error "Step key must be a non-empty string."
        | _ -> Ok(StepKey value)

    let value (StepKey value) = value

module StepOutcome =
    let fromInt =
        function
        | 0 -> Ok Success
        | 1 -> Ok Stopped
        | 2 -> Ok Failed
        | v -> Error $"Invalid step outcome: {v}"

    let toInt =
        function
        | Success -> 0
        | Stopped -> 1
        | Failed -> 2

module StepTypeKey =
    let create (value: string) : Result<StepTypeKey, string> =
        match value with
        | null
        | "" -> Error "Step type key must be a non-empty string."
        | _ -> Ok(StepTypeKey value)
