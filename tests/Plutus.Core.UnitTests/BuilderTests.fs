module Plutus.Core.UnitTests.BuilderTests

open System
open System.Threading.Tasks
open Xunit
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Registry
open Plutus.Core.Pipelines.Core.Builder

let private nullServices: IServiceProvider = null

let private mkStepDef
    key
    (schemaDefs: ParameterDef list)
    (createFn: ValidatedParams -> IServiceProvider -> Step<string>)
    : StepDefinition<string>
    =
    { Key = key
      Name = key
      Description = ""
      Category = StepCategory.Signal
      Icon = ""
      ParameterSchema = { Parameters = schemaDefs }
      Create = createFn }

let private simpleCreate key : ValidatedParams -> IServiceProvider -> Step<string> =
    fun _ _ -> { key = key; execute = fun ctx _ -> Task.FromResult(Continue(ctx + "+" + key, "ok")) }

let private mkConfig key order isEnabled (parameters: (string * string) list) : PipelineStepConfig =
    { StepTypeKey = key; Order = order; IsEnabled = isEnabled; Parameters = Map.ofList parameters }

let private assertOk result =
    match result with
    | Ok v -> v
    | Error e -> failwithf $"Expected Ok but got Error: %A{e}"

let private assertError result =
    match result with
    | Error e -> e
    | Ok _ -> failwith "Expected Error but got Ok"

[<Fact>]
let ``Matching config builds steps in order`` () =
    let reg = create [ mkStepDef "a" [] (simpleCreate "a"); mkStepDef "b" [] (simpleCreate "b") ]
    let configs = [ mkConfig "a" 2 true []; mkConfig "b" 1 true [] ]
    let steps = buildSteps reg nullServices configs |> assertOk
    Assert.Equal(2, steps.Length)
    Assert.Equal("b", steps[0].key)
    Assert.Equal("a", steps[1].key)

[<Fact>]
let ``Unknown step key in config is silently skipped`` () =
    let reg = create [ mkStepDef "a" [] (simpleCreate "a") ]
    let configs = [ mkConfig "a" 1 true []; mkConfig "unknown" 2 true [] ]
    let steps = buildSteps reg nullServices configs |> assertOk
    Assert.Single(steps) |> ignore
    Assert.Equal("a", steps[0].key)

[<Fact>]
let ``Disabled step is skipped`` () =
    let reg = create [ mkStepDef "a" [] (simpleCreate "a"); mkStepDef "b" [] (simpleCreate "b") ]
    let configs = [ mkConfig "a" 1 true []; mkConfig "b" 2 false [] ]
    let steps = buildSteps reg nullServices configs |> assertOk
    Assert.Single(steps) |> ignore
    Assert.Equal("a", steps[0].key)

[<Fact>]
let ``Invalid params returns Error with BuildError`` () =
    let paramDef =
        { Key = "p"
          Name = "P"
          Description = ""
          Type = Int(None, None)
          Required = true
          DefaultValue = None
          Group = None }

    let reg = create [ mkStepDef "a" [ paramDef ] (simpleCreate "a") ]
    let configs = [ mkConfig "a" 1 true [ "p", "notanint" ] ]
    let errors = buildSteps reg nullServices configs |> assertError
    Assert.Single(errors) |> ignore
    Assert.Equal("a", errors[0].StepKey)

[<Fact>]
let ``Mixed valid and invalid configs returns Error`` () =
    let paramDef =
        { Key = "p"
          Name = "P"
          Description = ""
          Type = Int(None, None)
          Required = true
          DefaultValue = None
          Group = None }

    let reg =
        create [ mkStepDef "good" [] (simpleCreate "good"); mkStepDef "bad" [ paramDef ] (simpleCreate "bad") ]

    let configs = [ mkConfig "good" 1 true []; mkConfig "bad" 2 true [ "p", "nope" ] ]
    let errors = buildSteps reg nullServices configs |> assertError
    Assert.Single(errors) |> ignore
    Assert.Equal("bad", errors[0].StepKey)

[<Fact>]
let ``Empty config list returns Ok empty`` () =
    let reg = create [ mkStepDef "a" [] (simpleCreate "a") ]
    let steps = buildSteps reg nullServices [] |> assertOk
    Assert.Empty(steps)

[<Fact>]
let ``Out-of-order configs are sorted by Order`` () =
    let reg =
        create
            [ mkStepDef "a" [] (simpleCreate "a")
              mkStepDef "b" [] (simpleCreate "b")
              mkStepDef "c" [] (simpleCreate "c") ]

    let configs = [ mkConfig "c" 3 true []; mkConfig "a" 1 true []; mkConfig "b" 2 true [] ]
    let steps = buildSteps reg nullServices configs |> assertOk
    Assert.Equal(3, steps.Length)
    Assert.Equal("a", steps[0].key)
    Assert.Equal("b", steps[1].key)
    Assert.Equal("c", steps[2].key)

[<Fact>]
let ``Valid params are passed to Create function`` () =
    let mutable captured = None

    let paramDef =
        { Key = "n"
          Name = "N"
          Description = ""
          Type = Int(None, None)
          Required = true
          DefaultValue = None
          Group = None }

    let createFn: ValidatedParams -> IServiceProvider -> Step<string> =
        fun vp _ ->
            captured <- Some(ValidatedParams.getInt "n" 0 vp)
            { key = "s"; execute = fun ctx _ -> Task.FromResult(Continue(ctx, "ok")) }

    let reg = create [ mkStepDef "s" [ paramDef ] createFn ]
    let configs = [ mkConfig "s" 1 true [ "n", "42" ] ]
    buildSteps reg nullServices configs |> assertOk |> ignore
    Assert.Equal(Some 42, captured)
