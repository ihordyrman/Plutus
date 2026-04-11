namespace Plutus.Core.Pipelines.Core

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Infrastructure

module Runner =
    open Steps

    let run
        (pipelineId: int)
        (executionId: string)
        (serializeContext: 'ctx -> string)
        (logStep: ExecutionLog -> unit)
        (steps: Step<'ctx> list)
        (ctx: 'ctx)
        (ct: CancellationToken)
        : Task<StepResult<'ctx>>
        =
        task {
            let mutable currentCtx = ctx
            let mutable finalResult = Continue(ctx, "Started")

            for step in steps do
                if ct.IsCancellationRequested then
                    finalResult <- Stop "Cancelled"
                else
                    match finalResult with
                    | Continue(c, _) ->
                        let startTime = DateTime.UtcNow
                        let! result = step.execute c ct
                        let endTime = DateTime.UtcNow

                        let outcome, message, updatedCtx =
                            match result with
                            | Continue(c', msg) -> StepOutcome.Success, msg, c'
                            | Stop msg -> StepOutcome.Stopped, msg, currentCtx
                            | Fail err -> StepOutcome.Failed, err, currentCtx

                        let contextSnapshot = serializeContext updatedCtx

                        let log =
                            { ExecutionLog.create pipelineId executionId step.key startTime with
                                Outcome = outcome
                                Message = message
                                ContextSnapshot = contextSnapshot
                                EndTime = endTime }

                        logStep log

                        currentCtx <- updatedCtx
                        finalResult <- result
                    | Stop _
                    | Fail _ -> ()

            return finalResult
        }
