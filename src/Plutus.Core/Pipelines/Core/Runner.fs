namespace Plutus.Core.Pipelines.Core

open System.Threading
open System.Threading.Tasks

module Runner =
    open Steps

    let run (steps: Step<'ctx> list) (ctx: 'ctx) (ct: CancellationToken) : Task<StepResult<'ctx>> =
        task {
            let mutable currentCtx = ctx
            let mutable finalResult = Continue(ctx, "Started")

            for step in steps do
                if ct.IsCancellationRequested then
                    finalResult <- Stop "Cancelled"
                else
                    match finalResult with
                    | Continue(c, _) ->
                        let! result = step c ct

                        currentCtx <-
                            match result with
                            | Continue(c', _) -> c'
                            | _ -> currentCtx

                        finalResult <- result
                    | Stop _
                    | Fail _ -> ()

            return finalResult
        }
