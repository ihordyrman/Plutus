module Plutus.Core.UnitTests.RunnerTests

open System.Threading
open System.Threading.Tasks
open Xunit
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Runner
open Plutus.Core.Infrastructure

let private mkStep key (fn: string -> CancellationToken -> Task<StepResult<string>>) : Step<string> =
    { key = key; execute = fn }

let private continueStep key suffix : Step<string> =
    mkStep key (fun ctx _ -> Task.FromResult(Continue(ctx + suffix, "ok")))

let private stopStep key msg : Step<string> = mkStep key (fun _ _ -> Task.FromResult(Stop msg))

let private failStep key msg : Step<string> = mkStep key (fun _ _ -> Task.FromResult(Fail msg))

let private noopSerializer (_: string) = "{}"
let private mutable_logs () = ResizeArray<ExecutionLog>()
let private logTo (logs: ResizeArray<ExecutionLog>) = fun log -> logs.Add(log)
let private noopLog = fun (_: ExecutionLog) -> ()

[<Fact>]
let ``All steps Continue returns final Continue with last context`` () =
    task {
        let steps = [ continueStep "a" "+a"; continueStep "b" "+b" ]
        let! result = run 1 "exec" noopSerializer noopLog steps "init" CancellationToken.None

        match result with
        | Continue(ctx, _) -> Assert.Equal("init+a+b", ctx)
        | other -> failwithf $"Expected Continue but got %A{other}"
    }

[<Fact>]
let ``First step Fails skips remaining steps`` () =
    task {
        let steps = [ failStep "a" "boom"; continueStep "b" "+b" ]
        let logs = mutable_logs ()
        let! result = run 1 "exec" noopSerializer (logTo logs) steps "init" CancellationToken.None

        match result with
        | Fail msg -> Assert.Equal("boom", msg)
        | other -> failwithf $"Expected Fail but got %A{other}"

        Assert.Single(logs) |> ignore
        Assert.Equal("a", logs[0].StepTypeKey)
    }

[<Fact>]
let ``Middle step Stops skips remaining steps`` () =
    task {
        let steps = [ continueStep "a" "+a"; stopStep "b" "halted"; continueStep "c" "+c" ]
        let logs = mutable_logs ()
        let! result = run 1 "exec" noopSerializer (logTo logs) steps "init" CancellationToken.None

        match result with
        | Stop msg -> Assert.Equal("halted", msg)
        | other -> failwithf $"Expected Stop but got %A{other}"

        Assert.Equal(2, logs.Count)
    }

[<Fact>]
let ``Single step Continue returns that result`` () =
    task {
        let steps = [ continueStep "a" "+a" ]
        let! result = run 1 "exec" noopSerializer noopLog steps "init" CancellationToken.None

        match result with
        | Continue(ctx, _) -> Assert.Equal("init+a", ctx)
        | other -> failwithf $"Expected Continue but got %A{other}"
    }

[<Fact>]
let ``Empty step list returns Continue with initial context`` () =
    task {
        let! result = run 1 "exec" noopSerializer noopLog [] "init" CancellationToken.None

        match result with
        | Continue(ctx, msg) ->
            Assert.Equal("init", ctx)
            Assert.Equal("Started", msg)
        | other -> failwithf $"Expected Continue but got %A{other}"
    }

[<Fact>]
let ``CancellationToken cancelled before run returns Stop Cancelled`` () =
    task {
        let cts = new CancellationTokenSource()
        cts.Cancel()
        let steps = [ continueStep "a" "+a" ]
        let! result = run 1 "exec" noopSerializer noopLog steps "init" cts.Token

        match result with
        | Stop msg -> Assert.Equal("Cancelled", msg)
        | other -> failwithf $"Expected Stop but got %A{other}"
    }

[<Fact>]
let ``CancellationToken cancelled between steps stops processing`` () =
    task {
        let cts = new CancellationTokenSource()

        let cancelAfterFirst =
            mkStep
                "a"
                (fun ctx _ ->
                    task {
                        cts.Cancel()
                        return Continue(ctx + "+a", "ok")
                    }
                )

        let steps = [ cancelAfterFirst; continueStep "b" "+b" ]
        let logs = mutable_logs ()
        let! result = run 1 "exec" noopSerializer (logTo logs) steps "init" cts.Token

        match result with
        | Stop msg -> Assert.Equal("Cancelled", msg)
        | other -> failwithf $"Expected Stop but got %A{other}"

        Assert.Single(logs) |> ignore
    }

[<Fact>]
let ``Context threading: each step receives output of previous`` () =
    task {
        let steps = [ continueStep "a" "+a"; continueStep "b" "+b"; continueStep "c" "+c" ]
        let! result = run 1 "exec" noopSerializer noopLog steps "start" CancellationToken.None

        match result with
        | Continue(ctx, _) -> Assert.Equal("start+a+b+c", ctx)
        | other -> failwithf $"Expected Continue but got %A{other}"
    }

[<Fact>]
let ``Log function called for each executed step with correct outcome`` () =
    task {
        let steps = [ continueStep "a" "+a"; stopStep "b" "halt" ]
        let logs = mutable_logs ()
        let! _ = run 1 "exec" noopSerializer (logTo logs) steps "init" CancellationToken.None

        Assert.Equal(2, logs.Count)
        Assert.Equal("a", logs[0].StepTypeKey)
        Assert.Equal(StepOutcome.Success, logs[0].Outcome)
        Assert.Equal("b", logs[1].StepTypeKey)
        Assert.Equal(StepOutcome.Stopped, logs[1].Outcome)
    }
