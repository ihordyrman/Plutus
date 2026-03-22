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
    let create (value: string) : Result<PipelineName, string> =
        if String.IsNullOrWhiteSpace value then
            Error "Pipeline name cannot be empty."
        else
            Ok(PipelineName value)

    let value (PipelineName v) = v

type Pipeline =
    { Id: PipelineId
      Name: PipelineName
      Instrument: Instrument
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      LastExecutedAt: DateTime option
      Status: PipelineStatus
      Tags: string list
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

type StepOrder = private StepOrder of int

module StepOrder =
    let create (order: int) : Result<StepOrder, string> =
        if order < 0 then
            Error "Step order cannot be negative."
        else
            Ok(StepOrder order)

    let value (StepOrder o) = o

type PipelineStep =
    { Id: StepId
      PipelineId: PipelineId
      StepTypeKey: StepTypeKey
      Name: NonEmptyString
      Order: StepOrder
      IsEnabled: bool
      Parameters: Map<string, string>
      CreatedAt: DateTime
      UpdatedAt: DateTime }
