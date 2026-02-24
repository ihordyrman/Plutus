namespace Plutus.Core.Domain

open System
open System.Collections.Generic
open Plutus.Core.Shared

type PipelineStatus =
    | Idle = 0
    | Running = 1
    | Paused = 2
    | Error = 3

[<CLIMutable>]
type Pipeline =
    { Id: int
      Name: string
      Instrument: Instrument
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      LastExecutedAt: Nullable<DateTime>
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
