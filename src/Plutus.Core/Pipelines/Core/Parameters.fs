namespace Plutus.Core.Pipelines.Core

open System

module Parameters =

    type ParamValue =
        | StringValue of string
        | DecimalValue of decimal
        | IntValue of int
        | BoolValue of bool
        | ChoiceValue of string

    type ParameterDef =
        { Key: string
          Name: string
          Description: string
          Type: ParameterType
          Required: bool
          DefaultValue: ParamValue option
          Group: string option }

    and ParameterType =
        | String
        | Decimal of min: decimal option * max: decimal option
        | Int of min: int option * max: int option
        | Bool
        | Choice of options: string list

    type ParameterSchema = { Parameters: ParameterDef list }

    type ValidationError = { Key: string; Message: string }

    type ValidatedParams = private { Values: Map<string, ParamValue> }

    module ValidatedParams =
        let tryGet key (param: ValidatedParams) = Map.tryFind key param.Values

        let tryGetString key param =
            tryGet key param
            |> Option.bind (
                function
                | StringValue x -> Some x
                | _ -> None
            )

        let tryGetDecimal key param =
            tryGet key param
            |> Option.bind (
                function
                | DecimalValue x -> Some x
                | _ -> None
            )

        let tryGetInt key param =
            tryGet key param
            |> Option.bind (
                function
                | IntValue x -> Some x
                | _ -> None
            )

        let tryGetBool key param =
            tryGet key param
            |> Option.bind (
                function
                | BoolValue x -> Some x
                | _ -> None
            )

        let getDecimal key defaultValue param = tryGetDecimal key param |> Option.defaultValue defaultValue
        let getInt key defaultValue param = tryGetInt key param |> Option.defaultValue defaultValue
        let getBool key defaultValue param = tryGetBool key param |> Option.defaultValue defaultValue
        let getString key defaultValue param = tryGetString key param |> Option.defaultValue defaultValue

    let private parseValue (def: ParameterDef) (raw: string) : Result<ParamValue, string> =
        match def.Type with
        | String -> Ok(StringValue raw)

        | Decimal(min, max) ->
            match Decimal.TryParse raw with
            | false, _ -> Error "must be a valid decimal"
            | true, x ->
                match min, max with
                | Some m, _ when x < m -> Error $"must be at least {m}"
                | _, Some m when x > m -> Error $"must be at most {m}"
                | _ -> Ok(DecimalValue x)

        | Int(min, max) ->
            match Int32.TryParse raw with
            | false, _ -> Error "must be a valid integer"
            | true, x ->
                match min, max with
                | Some m, _ when x < m -> Error $"must be at least {m}"
                | _, Some m when x > m -> Error $"must be at most {m}"
                | _ -> Ok(IntValue x)

        | Bool ->
            match Boolean.TryParse raw with
            | false, _ -> Error "must be true or false"
            | true, x -> Ok(BoolValue x)

        | Choice options ->
            if List.contains raw options then
                Ok(ChoiceValue raw)
            else
                let options = options |> List.map (sprintf "'%s'")
                Error $"must be one of: {options}"

    let validate (schema: ParameterSchema) (raw: Map<string, string>) : Result<ValidatedParams, ValidationError list> =
        let results =
            schema.Parameters
            |> List.map (fun def ->
                match Map.tryFind def.Key raw, def.DefaultValue with
                | None, None when def.Required ->
                    Error { Key = def.Key; Message = $"Required parameter '{def.Name}' is missing" }
                | None, Some defaultVal -> Ok(Some(def.Key, defaultVal))
                | None, None -> Ok None
                | Some rawValue, _ ->
                    match parseValue def rawValue with
                    | Ok value -> Ok(Some(def.Key, value))
                    | Error msg -> Error { Key = def.Key; Message = $"'{def.Name}' {msg}" }
            )

        let errors =
            results
            |> List.choose (
                function
                | Error e -> Some e
                | _ -> None
            )

        if not (List.isEmpty errors) then
            Error errors
        else
            let values =
                results
                |> List.choose (
                    function
                    | Ok(Some x) -> Some x
                    | _ -> None
                )
                |> Map.ofList

            Ok { Values = values }
