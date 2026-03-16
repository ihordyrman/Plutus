namespace Plutus.Core.Domain

open System
open System.Collections.Generic

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

[<CLIMutable>]
type Pipeline =
    { Id: int
      Name: string
      Instrument: Instrument
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      LastExecutedAt: DateTime option
      Status: PipelineStatus
      Tags: string list
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type PipelineStep =
    { Id: int
      PipelineId: int
      StepTypeKey: string
      Name: string
      Order: int
      IsEnabled: bool
      Parameters: Dictionary<string, string>
      CreatedAt: DateTime
      UpdatedAt: DateTime }
