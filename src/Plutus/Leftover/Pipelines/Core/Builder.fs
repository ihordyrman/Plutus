namespace Plutus.Core.Pipelines.Core

open System

module Builder =
    open Steps

    type PipelineStepConfig = { StepTypeKey: string; Order: int; IsEnabled: bool; Parameters: Map<string, string> }

    type BuildError = { StepKey: string; Errors: Parameters.ValidationError list }

    let buildSteps
        (registry: Registry.T<'ctx>)
        (services: IServiceProvider)
        (stepConfigs: PipelineStepConfig list)
        : Result<Step<'ctx> list, BuildError list>
        =

        let result =
            stepConfigs
            |> List.filter _.IsEnabled
            |> List.sortBy _.Order
            |> List.choose (fun config ->
                registry
                |> Registry.tryFind config.StepTypeKey
                |> Option.map (fun x ->
                    match Parameters.validate x.ParameterSchema config.Parameters with
                    | Ok validParams -> Ok(x.Create validParams services)
                    | Error errs -> Error { StepKey = config.StepTypeKey; Errors = errs }
                )
            )

        let errors =
            result
            |> List.choose (
                function
                | Error e -> Some e
                | _ -> None
            )

        match errors with
        | [] ->
            Ok(
                result
                |> List.choose (
                    function
                    | Ok step -> Some step
                    | _ -> None
                )
            )
        | _ -> Error errors
