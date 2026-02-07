module Plutus.Core.UnitTests.ParametersTests

open Xunit
open Plutus.Core.Pipelines.Core.Parameters

let private mkSchema defs = { Parameters = defs }

let private mkDef key name typ required defaultValue =
    { Key = key
      Name = name
      Description = ""
      Type = typ
      Required = required
      DefaultValue = defaultValue
      Group = None }

let private assertOk result =
    match result with
    | Ok v -> v
    | Error e -> failwithf $"Expected Ok but got Error: %A{e}"

let private assertError result =
    match result with
    | Error e -> e
    | Ok v -> failwithf $"Expected Error but got Ok: %A{v}"

[<Fact>]
let ``Required param present returns Ok with correct typed value`` () =
    let schema = mkSchema [ mkDef "k" "Key" String true None ]
    let result = validate schema (Map.ofList [ "k", "hello" ]) |> assertOk
    Assert.Equal(Some(StringValue "hello"), ValidatedParams.tryGet "k" result)

[<Fact>]
let ``Required param missing returns Error with key`` () =
    let schema = mkSchema [ mkDef "k" "Key" String true None ]
    let errors = validate schema Map.empty |> assertError
    Assert.Single(errors) |> ignore
    Assert.Equal("k", errors[0].Key)

[<Fact>]
let ``Optional param missing with default uses default`` () =
    let schema = mkSchema [ mkDef "k" "Key" String false (Some(StringValue "fallback")) ]
    let result = validate schema Map.empty |> assertOk
    Assert.Equal(Some(StringValue "fallback"), ValidatedParams.tryGet "k" result)

[<Fact>]
let ``Optional param missing without default is skipped`` () =
    let schema = mkSchema [ mkDef "k" "Key" String false None ]
    let result = validate schema Map.empty |> assertOk
    Assert.Equal(None, ValidatedParams.tryGet "k" result)

[<Fact>]
let ``Decimal in range returns Ok`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(Some 0m, Some 100m)) true None ]
    let result = validate schema (Map.ofList [ "d", "50.5" ]) |> assertOk
    Assert.Equal(Some(DecimalValue 50.5m), ValidatedParams.tryGet "d" result)

[<Fact>]
let ``Decimal below min returns Error`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(Some 10m, None)) true None ]
    let errors = validate schema (Map.ofList [ "d", "5" ]) |> assertError
    Assert.Equal("d", errors[0].Key)

[<Fact>]
let ``Decimal above max returns Error`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(None, Some 10m)) true None ]
    let errors = validate schema (Map.ofList [ "d", "15" ]) |> assertError
    Assert.Equal("d", errors[0].Key)

[<Fact>]
let ``Decimal non-numeric returns Error`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(None, None)) true None ]
    let errors = validate schema (Map.ofList [ "d", "abc" ]) |> assertError
    Assert.Equal("d", errors[0].Key)

[<Fact>]
let ``Decimal exactly at min boundary returns Ok`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(Some 5m, None)) true None ]
    let result = validate schema (Map.ofList [ "d", "5" ]) |> assertOk
    Assert.Equal(Some(DecimalValue 5m), ValidatedParams.tryGet "d" result)

[<Fact>]
let ``Decimal exactly at max boundary returns Ok`` () =
    let schema = mkSchema [ mkDef "d" "Dec" (Decimal(None, Some 5m)) true None ]
    let result = validate schema (Map.ofList [ "d", "5" ]) |> assertOk
    Assert.Equal(Some(DecimalValue 5m), ValidatedParams.tryGet "d" result)

[<Fact>]
let ``Int in range returns Ok`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(Some 0, Some 100)) true None ]
    let result = validate schema (Map.ofList [ "i", "42" ]) |> assertOk
    Assert.Equal(Some(IntValue 42), ValidatedParams.tryGet "i" result)

[<Fact>]
let ``Int below min returns Error`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(Some 10, None)) true None ]
    let errors = validate schema (Map.ofList [ "i", "5" ]) |> assertError
    Assert.Equal("i", errors[0].Key)

[<Fact>]
let ``Int above max returns Error`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(None, Some 10)) true None ]
    let errors = validate schema (Map.ofList [ "i", "15" ]) |> assertError
    Assert.Equal("i", errors[0].Key)

[<Fact>]
let ``Int non-numeric returns Error`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(None, None)) true None ]
    let errors = validate schema (Map.ofList [ "i", "abc" ]) |> assertError
    Assert.Equal("i", errors[0].Key)

[<Fact>]
let ``Int exactly at min boundary returns Ok`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(Some 5, None)) true None ]
    let result = validate schema (Map.ofList [ "i", "5" ]) |> assertOk
    Assert.Equal(Some(IntValue 5), ValidatedParams.tryGet "i" result)

[<Fact>]
let ``Int exactly at max boundary returns Ok`` () =
    let schema = mkSchema [ mkDef "i" "Int" (Int(None, Some 5)) true None ]
    let result = validate schema (Map.ofList [ "i", "5" ]) |> assertOk
    Assert.Equal(Some(IntValue 5), ValidatedParams.tryGet "i" result)

[<Fact>]
let ``Bool true returns Ok`` () =
    let schema = mkSchema [ mkDef "b" "Bool" Bool true None ]
    let result = validate schema (Map.ofList [ "b", "true" ]) |> assertOk
    Assert.Equal(Some(BoolValue true), ValidatedParams.tryGet "b" result)

[<Fact>]
let ``Bool false returns Ok`` () =
    let schema = mkSchema [ mkDef "b" "Bool" Bool true None ]
    let result = validate schema (Map.ofList [ "b", "false" ]) |> assertOk
    Assert.Equal(Some(BoolValue false), ValidatedParams.tryGet "b" result)

[<Fact>]
let ``Bool invalid string returns Error`` () =
    let schema = mkSchema [ mkDef "b" "Bool" Bool true None ]
    let errors = validate schema (Map.ofList [ "b", "yes" ]) |> assertError
    Assert.Equal("b", errors[0].Key)

[<Fact>]
let ``Choice valid option returns Ok`` () =
    let schema = mkSchema [ mkDef "c" "Choice" (Choice [ "a"; "b"; "c" ]) true None ]
    let result = validate schema (Map.ofList [ "c", "b" ]) |> assertOk
    Assert.Equal(Some(ChoiceValue "b"), ValidatedParams.tryGet "c" result)

[<Fact>]
let ``Choice invalid option returns Error`` () =
    let schema = mkSchema [ mkDef "c" "Choice" (Choice [ "a"; "b" ]) true None ]
    let errors = validate schema (Map.ofList [ "c", "x" ]) |> assertError
    Assert.Equal("c", errors[0].Key)

[<Fact>]
let ``MultiChoice valid subset returns Ok`` () =
    let schema = mkSchema [ mkDef "m" "Multi" (MultiChoice [ "a"; "b"; "c" ]) true None ]
    let result = validate schema (Map.ofList [ "m", "a;c" ]) |> assertOk
    Assert.Equal(Some(ListValue [ "a"; "c" ]), ValidatedParams.tryGet "m" result)

[<Fact>]
let ``MultiChoice none match returns Error`` () =
    let schema = mkSchema [ mkDef "m" "Multi" (MultiChoice [ "a"; "b" ]) true None ]
    let errors = validate schema (Map.ofList [ "m", "x;y" ]) |> assertError
    Assert.Equal("m", errors[0].Key)

[<Fact>]
let ``MultiChoice mixed valid and invalid keeps only valid`` () =
    let schema = mkSchema [ mkDef "m" "Multi" (MultiChoice [ "a"; "b"; "c" ]) true None ]
    let result = validate schema (Map.ofList [ "m", "a;x;b" ]) |> assertOk
    Assert.Equal(Some(ListValue [ "a"; "b" ]), ValidatedParams.tryGet "m" result)

[<Fact>]
let ``MultiChoice with whitespace around values`` () =
    let schema = mkSchema [ mkDef "m" "Multi" (MultiChoice [ "a"; "b" ]) true None ]
    let result = validate schema (Map.ofList [ "m", " a ; b " ]) |> assertOk
    Assert.Equal(Some(ListValue [ "a"; "b" ]), ValidatedParams.tryGet "m" result)

[<Fact>]
let ``Multiple params with one invalid returns single error`` () =
    let schema = mkSchema [ mkDef "a" "A" String true None; mkDef "b" "B" (Int(None, None)) true None ]

    let errors = validate schema (Map.ofList [ "a", "ok"; "b", "nope" ]) |> assertError
    Assert.Single(errors) |> ignore
    Assert.Equal("b", errors[0].Key)

[<Fact>]
let ``Multiple params all invalid returns all keys in error list`` () =
    let schema =
        mkSchema [ mkDef "a" "A" (Int(None, None)) true None; mkDef "b" "B" (Int(None, None)) true None ]

    let errors = validate schema (Map.ofList [ "a", "x"; "b", "y" ]) |> assertError
    Assert.Equal(2, errors.Length)
    let keys = errors |> List.map _.Key |> List.sort
    Assert.Equal<string list>([ "a"; "b" ], keys)

[<Fact>]
let ``Empty schema and empty input returns Ok`` () =
    let result = validate (mkSchema []) Map.empty |> assertOk
    Assert.Equal(None, ValidatedParams.tryGet "anything" result)

[<Fact>]
let ``getInt returns value when present`` () =
    let schema = mkSchema [ mkDef "i" "I" (Int(None, None)) true None ]
    let vp = validate schema (Map.ofList [ "i", "7" ]) |> assertOk
    Assert.Equal(7, ValidatedParams.getInt "i" 0 vp)

[<Fact>]
let ``getInt returns default when missing`` () =
    let vp = validate (mkSchema []) Map.empty |> assertOk
    Assert.Equal(99, ValidatedParams.getInt "i" 99 vp)

[<Fact>]
let ``getDecimal returns value when present`` () =
    let schema = mkSchema [ mkDef "d" "D" (Decimal(None, None)) true None ]
    let vp = validate schema (Map.ofList [ "d", "3.14" ]) |> assertOk
    Assert.Equal(3.14m, ValidatedParams.getDecimal "d" 0m vp)

[<Fact>]
let ``getDecimal returns default when missing`` () =
    let vp = validate (mkSchema []) Map.empty |> assertOk
    Assert.Equal(1.5m, ValidatedParams.getDecimal "d" 1.5m vp)

[<Fact>]
let ``getString returns value when present`` () =
    let schema = mkSchema [ mkDef "s" "S" String true None ]
    let vp = validate schema (Map.ofList [ "s", "hi" ]) |> assertOk
    Assert.Equal("hi", ValidatedParams.getString "s" "" vp)

[<Fact>]
let ``getString returns default when missing`` () =
    let vp = validate (mkSchema []) Map.empty |> assertOk
    Assert.Equal("def", ValidatedParams.getString "s" "def" vp)

[<Fact>]
let ``getBool returns value when present`` () =
    let schema = mkSchema [ mkDef "b" "B" Bool true None ]
    let vp = validate schema (Map.ofList [ "b", "true" ]) |> assertOk
    Assert.True(ValidatedParams.getBool "b" false vp)

[<Fact>]
let ``getBool returns default when missing`` () =
    let vp = validate (mkSchema []) Map.empty |> assertOk
    Assert.False(ValidatedParams.getBool "b" false vp)

[<Fact>]
let ``getList returns value when present`` () =
    let schema = mkSchema [ mkDef "m" "M" (MultiChoice [ "a"; "b" ]) true None ]
    let vp = validate schema (Map.ofList [ "m", "a;b" ]) |> assertOk
    Assert.Equal<string list>([ "a"; "b" ], ValidatedParams.getList "m" [] vp)

[<Fact>]
let ``getList returns default when missing`` () =
    let vp = validate (mkSchema []) Map.empty |> assertOk
    Assert.Equal<string list>([ "x" ], ValidatedParams.getList "m" [ "x" ] vp)
