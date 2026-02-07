namespace Plutus.Core.Pipelines.Core

open System
open System.Threading
open System.Threading.Tasks

module Steps =
    open Parameters

    type StepResult<'ctx> =
        | Continue of 'ctx * message: string
        | Stop of message: string
        | Fail of error: string

    type Step<'ctx> = { key: string; execute: 'ctx -> CancellationToken -> Task<StepResult<'ctx>> }

    type StepCategory =
        | Validation = 0
        | Risk = 1
        | Signal = 2
        | Execution = 3

    type StepDefinition<'ctx> =
        { Key: string
          Name: string
          Description: string
          Category: StepCategory
          Icon: string
          ParameterSchema: ParameterSchema
          Create: ValidatedParams -> IServiceProvider -> Step<'ctx> }
