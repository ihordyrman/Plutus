namespace Plutus.Core.Domain

open System

type PipelineStatus =
    | Idle = 0
    | Running = 1
    | Paused = 2
    | Error = 3

type PipelineId = private PipelineId of int

module PipelineId =
    let create (id: int) : Result<PipelineId, string> =
        match id with
        | x when x <= 0 -> Error "Pipeline ID must be a positive integer."
        | _ -> Ok(PipelineId id)

    let value (PipelineId id) = id

type PipelineName = private PipelineName of string

module PipelineName =
    let create (name: string) : Result<PipelineName, string> =
        match name with
        | x when String.IsNullOrWhiteSpace x -> Error "Pipeline name cannot be empty."
        | x when x.Length < 3 -> Error "Pipeline name must be at least 3 characters long."
        | x when x.Length > 50 -> Error "Pipeline name cannot exceed 50 characters."
        | _ -> Ok(PipelineName name)

    let value (PipelineName v) = v

type PipelineTag = private PipelineTag of string

module PipelineTag =
    let create (tag: string) : Result<PipelineTag, string> =
        match tag with
        | x when x.Length > 1 && x.Length <= 10 -> Ok(PipelineTag tag)
        | _ -> Error "Pipeline tag must be between 2 and 10 characters."

    let value (PipelineTag tag) = tag

type Pipeline =
    { Id: PipelineId
      Name: PipelineName
      Instrument: Instrument
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      LastExecutedAt: DateTime option
      Status: PipelineStatus
      Tags: PipelineTag list
      CreatedAt: DateTime
      UpdatedAt: DateTime }

type StepId = private StepId of int

module StepId =
    let create (id: int) : Result<StepId, string> =
        if id <= 0 then
            Error "Step ID must be a positive integer."
        else
            Ok(StepId id)

    let value (StepId id) = id

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

type StepOrder = private StepOrder of int

module StepOrder =
    let create (order: int) : Result<StepOrder, string> =
        if order < 0 then
            Error "Step order cannot be negative."
        else
            Ok(StepOrder order)

    let value (StepOrder o) = o

type StepParameters = private StepParameters of Map<string, string>

module StepParameters =
    let create (parameters: Map<string, string>) : Result<StepParameters, string> =
        match parameters with
        | x when x |> Map.exists (fun k _ -> String.IsNullOrWhiteSpace k) ->
            Error "Step parameters cannot contain empty keys."
        | x when x |> Map.exists (fun _ v -> String.IsNullOrWhiteSpace v) ->
            Error "Step parameters cannot contain empty values."
        | _ -> Ok(StepParameters parameters)

    let value (StepParameters param) = param

type StepName = private StepName of string

module StepName =
    let create (name: string) : Result<StepName, string> =
        match name with
        | x when String.IsNullOrWhiteSpace x -> Error "Step name cannot be empty."
        | x when x.Length < 3 -> Error "Step name must be at least 3 characters long."
        | x when x.Length > 50 -> Error "Step name cannot exceed 50 characters."
        | _ -> Ok(StepName name)

    let value (StepName n) = n

type PipelineStep =
    { Id: StepId
      PipelineId: PipelineId
      StepTypeKey: StepTypeKey
      Name: StepName
      Order: StepOrder
      IsEnabled: bool
      Parameters: StepParameters
      CreatedAt: DateTime
      UpdatedAt: DateTime }
