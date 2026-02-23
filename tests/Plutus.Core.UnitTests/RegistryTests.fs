module Plutus.Core.UnitTests.RegistryTests

open Xunit
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Pipelines.Core.Registry

let private mkDef key : StepDefinition<string> =
    { Key = key
      Name = key
      Description = ""
      Category = StepCategory.Signal
      Icon = ""
      ParameterSchema = { Parameters = [] }
      RequiredCandleData = fun _ -> []
      Create =
        fun _ _ -> { key = key; execute = fun ctx _ -> System.Threading.Tasks.Task.FromResult(Continue(ctx, "ok")) } }

[<Fact>]
let ``empty registry has no definitions`` () =
    let all = empty<string> |> all
    Assert.Empty(all)

[<Fact>]
let ``register then tryFind returns the definition`` () =
    let def = mkDef "step1"
    let result = empty |> register def |> tryFind "step1"
    Assert.True(result.IsSome)
    Assert.Equal("step1", result.Value.Key)

[<Fact>]
let ``register same key twice overwrites first`` () =
    let def1 = mkDef "step1"
    let def2 = { mkDef "step1" with Name = "updated" }
    let result = empty |> register def1 |> register def2 |> tryFind "step1"
    Assert.Equal("updated", result.Value.Name)

[<Fact>]
let ``tryFind missing key returns None`` () =
    let result = empty<string> |> tryFind "missing"
    Assert.True(result.IsNone)

[<Fact>]
let ``all returns all registered definitions`` () =
    let reg = empty |> register (mkDef "a") |> register (mkDef "b")
    let defs = all reg
    Assert.Equal(2, defs.Length)
    let keys = defs |> List.map _.Key |> List.sort
    Assert.Equal<string list>([ "a"; "b" ], keys)

[<Fact>]
let ``create from list builds correct registry`` () =
    let reg = create [ mkDef "x"; mkDef "y" ]
    Assert.True((tryFind "x" reg).IsSome)
    Assert.True((tryFind "y" reg).IsSome)
    Assert.Equal(2, (all reg).Length)

[<Fact>]
let ``create with duplicate keys in list last wins`` () =
    let d1 = mkDef "k"
    let d2 = { mkDef "k" with Name = "second" }
    let reg = create [ d1; d2 ]
    Assert.Equal("second", (tryFind "k" reg).Value.Name)
