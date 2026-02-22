namespace Plutus.App.Pages.PipelineEdit

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core
open Plutus.Core.Pipelines.Core.Steps
open Plutus.Core.Repositories
open Plutus.Core.Shared

type StepItemViewModel =
    { Id: int
      PipelineId: int
      StepTypeKey: string
      DisplayName: string
      Description: string
      Icon: string
      Category: string
      Order: int
      IsEnabled: bool
      IsFirst: bool
      IsLast: bool
      ParameterSummary: string }

type StepDefinitionViewModel =
    { Key: string
      Name: string
      Description: string
      Category: string
      Icon: string
      IsAlreadyInPipeline: bool }

type ParameterFieldViewModel =
    { Key: string
      DisplayName: string
      Description: string
      Type: Parameters.ParameterType
      IsRequired: bool
      CurrentValue: string option
      DefaultValue: Parameters.ParamValue option }

type StepEditorViewModel =
    { PipelineId: int
      StepId: int
      StepTypeKey: string
      StepName: string
      StepDescription: string
      StepIcon: string
      Fields: ParameterFieldViewModel list
      Errors: string list }

type EditPipelineViewModel =
    { Id: int
      Symbol: string
      BaseCurrency: string
      QuoteCurrency: string
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: int
      Tags: string
      Steps: StepItemViewModel list
      MarketTypes: MarketType list
      BaseCurrencies: string list
      QuoteCurrencies: string list
      StepDefinitions: StepDefinitionViewModel list }

type EditFormData =
    { MarketType: int option
      Symbol: string option
      Tags: string option
      ExecutionInterval: int option
      Enabled: bool }

    static member Empty = { MarketType = None; Symbol = None; Tags = None; ExecutionInterval = None; Enabled = false }

type EditResult =
    | Success
    | ValidationError of message: string
    | NotFoundError
    | ServerError of message: string

module Data =
    let private marketTypes = [ MarketType.Okx; MarketType.Binance ]

    let private mapStepToViewModel
        (pipelineId: int)
        (stepDef: StepDefinition<TradingContext> option)
        (step: PipelineStep)
        (isFirst: bool)
        (isLast: bool)
        =
        let paramSummary =
            if step.Parameters.Count > 0 then
                step.Parameters
                |> Seq.truncate 3
                |> Seq.map (fun kvp -> $"{kvp.Key}: {kvp.Value}")
                |> String.concat ", "
            else
                ""

        { Id = step.Id
          PipelineId = pipelineId
          StepTypeKey = step.StepTypeKey
          DisplayName = stepDef |> Option.map _.Name |> Option.defaultValue step.Name
          Description = stepDef |> Option.map _.Description |> Option.defaultValue ""
          Icon = stepDef |> Option.map _.Icon |> Option.defaultValue "fa-puzzle-piece"
          Category = stepDef |> Option.map (fun d -> d.Category.ToString()) |> Option.defaultValue "Unknown"
          Order = step.Order
          IsEnabled = step.IsEnabled
          IsFirst = isFirst
          IsLast = isLast
          ParameterSummary = paramSummary }

    let getEditViewModel
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (ct: CancellationToken)
        : Task<EditPipelineViewModel option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! result = PipelineRepository.getById db pipelineId ct

            match result with
            | Error err ->
                let logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PipelineEdit")

                logger.LogError(
                    "Pipeline with ID {PipelineId} not found: {Error}",
                    pipelineId,
                    Errors.serviceMessage err
                )

                return None
            | Ok pipeline ->
                let allDefs = Registry.all registry
                let! pipelineSteps = PipelineStepRepository.getByPipelineId db pipelineId ct

                let pipelineSteps =
                    match pipelineSteps with
                    | Error _ -> []
                    | Ok steps -> steps

                let existingKeys = pipelineSteps |> List.map _.StepTypeKey |> Set.ofList

                let defs =
                    allDefs
                    |> List.map (fun d ->
                        { Key = d.Key
                          Name = d.Name
                          Description = d.Description
                          Category = d.Category.ToString()
                          Icon = d.Icon
                          IsAlreadyInPipeline = existingKeys.Contains d.Key }
                    )

                let sortedSteps = pipelineSteps |> List.sortBy _.Order
                let stepCount = sortedSteps.Length

                let stepVms =
                    sortedSteps
                    |> List.mapi (fun i step ->
                        let def = Registry.tryFind step.StepTypeKey registry
                        mapStepToViewModel pipelineId def step (i = 0) (i = stepCount - 1)
                    )

                let baseCurrency, quoteCurrency =
                    match pipeline.Symbol.Split('-') with
                    | [| b; q |] -> b, q
                    | _ -> pipeline.Symbol, ""

                let! baseCurrencies = InstrumentRepository.getBaseCurrencies db (int pipeline.MarketType) "SPOT" ct

                let baseCurrencies =
                    match baseCurrencies with
                    | Ok c -> c
                    | Error _ -> []

                let! quoteCurrencies =
                    InstrumentRepository.getQuoteCurrencies db (int pipeline.MarketType) "SPOT" baseCurrency ct

                let quoteCurrencies =
                    match quoteCurrencies with
                    | Ok c -> c
                    | Error _ -> []

                return
                    Option.Some
                        { Id = pipeline.Id
                          Symbol = pipeline.Symbol
                          BaseCurrency = baseCurrency
                          QuoteCurrency = quoteCurrency
                          MarketType = pipeline.MarketType
                          Enabled = pipeline.Enabled
                          ExecutionInterval = int pipeline.ExecutionInterval.TotalMinutes
                          Tags = pipeline.Tags |> String.concat ", "
                          Steps = stepVms
                          MarketTypes = marketTypes
                          BaseCurrencies = baseCurrencies
                          QuoteCurrencies = quoteCurrencies
                          StepDefinitions = defs }
        }

    let getSteps
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (ct: CancellationToken)
        : Task<StepItemViewModel list>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! result = PipelineStepRepository.getByPipelineId db pipelineId ct

            match result with
            | Error _ -> return []
            | Ok steps ->
                let sortedSteps = steps |> List.sortBy _.Order
                let stepCount = sortedSteps.Length

                return
                    sortedSteps
                    |> List.mapi (fun i step ->
                        let def = Registry.tryFind step.StepTypeKey registry
                        mapStepToViewModel pipelineId def step (i = 0) (i = stepCount - 1)
                    )
        }

    let getStepDefinitions
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (ct: CancellationToken)
        : Task<StepDefinitionViewModel list>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! steps = PipelineStepRepository.getByPipelineId db pipelineId ct

            let existingKeys =
                match steps with
                | Ok steps -> steps |> List.map _.StepTypeKey |> Set.ofList
                | Error _ -> Set.empty

            let allDefs = Registry.all registry

            return
                allDefs
                |> List.map (fun d ->
                    { Key = d.Key
                      Name = d.Name
                      Description = d.Description
                      Category = d.Category.ToString()
                      Icon = d.Icon
                      IsAlreadyInPipeline = existingKeys.Contains d.Key }
                )
        }

    let getStepEditor
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepId: int)
        (ct: CancellationToken)
        : Task<StepEditorViewModel option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! result = PipelineStepRepository.getById db stepId ct

            match result with
            | Error _ -> return None
            | Ok step when step.PipelineId <> pipelineId -> return None
            | Ok step ->
                match Registry.tryFind step.StepTypeKey registry with
                | None -> return None
                | Some def ->
                    let fields =
                        def.ParameterSchema.Parameters
                        |> List.map (fun p ->
                            { Key = p.Key
                              DisplayName = p.Name
                              Description = p.Description
                              Type = p.Type
                              IsRequired = p.Required
                              CurrentValue =
                                step.Parameters |> Seq.tryFind (fun kvp -> kvp.Key = p.Key) |> Option.map _.Value
                              DefaultValue = p.DefaultValue }
                        )

                    return
                        Some
                            { PipelineId = pipelineId
                              StepId = stepId
                              StepTypeKey = step.StepTypeKey
                              StepName = def.Name
                              StepDescription = def.Description
                              StepIcon = def.Icon
                              Fields = fields
                              Errors = [] }
        }

    let parseFormData (form: FormData) : EditFormData =
        { MarketType = form.TryGetInt "marketType"
          Symbol = form.TryGetString "symbol"
          Tags = form.TryGetString "tags"
          ExecutionInterval = form.TryGetInt "executionInterval"
          Enabled = form.TryGetString "enabled" |> Option.map (fun _ -> true) |> Option.defaultValue false }

    let updatePipeline
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (formData: EditFormData)
        (ct: CancellationToken)
        : Task<EditResult>
        =
        task {
            match formData.Symbol with
            | None -> return ValidationError "Symbol is required"
            | Some symbol when String.IsNullOrWhiteSpace(symbol) -> return ValidationError "Symbol is required"
            | Some symbol ->
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! result = PipelineRepository.getById db pipelineId ct

                match result with
                | Error _ -> return NotFoundError
                | Ok pipeline ->
                    let marketType =
                        formData.MarketType |> Option.map enum<MarketType> |> Option.defaultValue pipeline.MarketType

                    let tags =
                        formData.Tags
                        |> Option.map (fun t ->
                            t.Split(',')
                            |> Array.map (fun s -> s.Trim())
                            |> Array.filter (not << String.IsNullOrWhiteSpace)
                            |> List.ofArray
                        )
                        |> Option.defaultValue pipeline.Tags

                    let interval =
                        formData.ExecutionInterval
                        |> Option.map (fun m -> TimeSpan.FromMinutes(float m))
                        |> Option.defaultValue pipeline.ExecutionInterval

                    let updated =
                        { pipeline with
                            Symbol = symbol.Trim().ToUpperInvariant()
                            MarketType = marketType
                            Tags = tags
                            ExecutionInterval = interval
                            Enabled = formData.Enabled
                            UpdatedAt = DateTime.UtcNow }

                    let! updateResult = PipelineRepository.update db updated ct

                    match updateResult with
                    | Ok _ -> return Success
                    | Error err -> return ServerError(Errors.serviceMessage err)
        }

    let addStep
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepTypeKey: string)
        (ct: CancellationToken)
        : Task<StepItemViewModel option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()

            match Registry.tryFind stepTypeKey registry with
            | None -> return None
            | Some def ->
                let! maxOrderResult = PipelineStepRepository.getMaxOrder db pipelineId ct

                let maxOrder =
                    match maxOrderResult with
                    | Ok o -> o
                    | Error _ -> -1

                let defaultParams =
                    def.ParameterSchema.Parameters
                    |> List.choose (fun p ->
                        p.DefaultValue
                        |> Option.map (fun dv ->
                            let value =
                                match dv with
                                | Parameters.StringValue s -> s
                                | Parameters.DecimalValue d -> string d
                                | Parameters.IntValue i -> string i
                                | Parameters.BoolValue b -> string b
                                | Parameters.ChoiceValue c -> c
                                | Parameters.ListValue items ->
                                    items |> String.concat (string Parameters.multiChoiceDelimiter)

                            p.Key, value
                        )
                    )
                    |> dict
                    |> System.Collections.Generic.Dictionary

                let newStep: PipelineStep =
                    { Id = 0
                      PipelineId = pipelineId
                      StepTypeKey = stepTypeKey
                      Name = def.Name
                      Order = maxOrder + 1
                      IsEnabled = true
                      Parameters = defaultParams
                      CreatedAt = DateTime.UtcNow
                      UpdatedAt = DateTime.UtcNow }

                let! createResult = PipelineStepRepository.create db newStep ct

                match createResult with
                | Error _ -> return None
                | Ok created -> return Some(mapStepToViewModel pipelineId (Some def) created (maxOrder < 0) true)
        }

    let toggleStep
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepId: int)
        (ct: CancellationToken)
        : Task<StepItemViewModel option>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! result = PipelineStepRepository.getById db stepId ct

            match result with
            | Ok step when step.PipelineId <> pipelineId -> return None
            | Ok step ->
                let! _ = PipelineStepRepository.setEnabled db stepId (not step.IsEnabled) ct
                let! updatedResult = PipelineStepRepository.getById db stepId ct

                match updatedResult with
                | Error _ -> return None
                | Ok updated ->
                    let def = Registry.tryFind updated.StepTypeKey registry
                    let result = mapStepToViewModel pipelineId def updated false false
                    return Some result
            | _ -> return None
        }


    let deleteStep
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepId: int)
        (ct: CancellationToken)
        : Task<bool>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! stepResult = PipelineStepRepository.getById db stepId ct

            match stepResult with
            | Error _ -> return false
            | Ok step when step.PipelineId <> pipelineId -> return false
            | Ok _ ->
                let! deleteResult = PipelineStepRepository.delete db stepId ct
                return Result.isOk deleteResult
        }

    let moveStep
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepId: int)
        (direction: string)
        (ct: CancellationToken)
        : Task<StepItemViewModel list>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let! result = PipelineStepRepository.getByPipelineId db pipelineId ct

            match result with
            | Error _ -> return []
            | Ok steps ->
                let sortedSteps = steps |> List.sortBy _.Order
                let currentIdx = sortedSteps |> List.tryFindIndex (fun s -> s.Id = stepId)

                match currentIdx with
                | None -> return! getSteps scopeFactory pipelineId ct
                | Some idx ->
                    let targetIdx =
                        match direction with
                        | "up" when idx > 0 -> Some(idx - 1)
                        | "down" when idx < sortedSteps.Length - 1 -> Some(idx + 1)
                        | _ -> None

                    match targetIdx with
                    | None -> return! getSteps scopeFactory pipelineId ct
                    | Some tIdx ->
                        let current = sortedSteps.[idx]
                        let target = sortedSteps.[tIdx]

                        let! _ = PipelineStepRepository.swapOrders db target current ct

                        return! getSteps scopeFactory pipelineId ct
        }

    let saveStepParams
        (scopeFactory: IServiceScopeFactory)
        (pipelineId: int)
        (stepId: int)
        (form: FormData)
        (ct: CancellationToken)
        : Task<Result<StepItemViewModel, StepEditorViewModel>>
        =
        task {
            use scope = scopeFactory.CreateScope()
            use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
            let registry = scope.ServiceProvider.GetRequiredService<Registry.T<TradingContext>>()
            let! result = PipelineStepRepository.getById db stepId ct

            match result with
            | Error _ ->
                return
                    Error
                        { PipelineId = pipelineId
                          StepId = stepId
                          StepTypeKey = ""
                          StepName = ""
                          StepDescription = ""
                          StepIcon = ""
                          Fields = []
                          Errors = [ "Step not found" ] }
            | Ok step when step.PipelineId <> pipelineId ->
                return
                    Error
                        { PipelineId = pipelineId
                          StepId = stepId
                          StepTypeKey = ""
                          StepName = ""
                          StepDescription = ""
                          StepIcon = ""
                          Fields = []
                          Errors = [ "Step not found" ] }
            | Ok step ->
                match Registry.tryFind step.StepTypeKey registry with
                | None ->
                    return
                        Error
                            { PipelineId = pipelineId
                              StepId = stepId
                              StepTypeKey = step.StepTypeKey
                              StepName = ""
                              StepDescription = ""
                              StepIcon = ""
                              Fields = []
                              Errors = [ "Unknown step type" ] }
                | Some def ->
                    let newParams = System.Collections.Generic.Dictionary<string, string>()

                    for param in def.ParameterSchema.Parameters do
                        match form.TryGetString param.Key with
                        | Some value ->
                            match param.Type with
                            | Parameters.Bool -> newParams.[param.Key] <- if value = "true" then "true" else "false"
                            | Parameters.MultiChoice _ when String.IsNullOrWhiteSpace(value) -> ()
                            | _ -> newParams.[param.Key] <- value
                        | None ->
                            match param.Type with
                            | Parameters.Bool -> newParams.[param.Key] <- "false"
                            | _ -> ()

                    let rawMap = newParams |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Map.ofSeq

                    match Parameters.validate def.ParameterSchema rawMap with
                    | Error errors ->
                        let fields =
                            def.ParameterSchema.Parameters
                            |> List.map (fun p ->
                                { Key = p.Key
                                  DisplayName = p.Name
                                  Description = p.Description
                                  Type = p.Type
                                  IsRequired = p.Required
                                  CurrentValue =
                                    newParams |> Seq.tryFind (fun kvp -> kvp.Key = p.Key) |> Option.map _.Value
                                  DefaultValue = p.DefaultValue }
                            )

                        return
                            Error
                                { PipelineId = pipelineId
                                  StepId = stepId
                                  StepTypeKey = step.StepTypeKey
                                  StepName = def.Name
                                  StepDescription = def.Description
                                  StepIcon = def.Icon
                                  Fields = fields
                                  Errors = errors |> List.map _.Message }
                    | Ok _ ->
                        let updatedStep = { step with Parameters = newParams; UpdatedAt = DateTime.UtcNow }
                        let! updateResult = PipelineStepRepository.update db updatedStep ct

                        match updateResult with
                        | Error err ->
                            return
                                Error
                                    { PipelineId = pipelineId
                                      StepId = stepId
                                      StepTypeKey = step.StepTypeKey
                                      StepName = def.Name
                                      StepDescription = def.Description
                                      StepIcon = def.Icon
                                      Fields = []
                                      Errors = [ Errors.serviceMessage err ] }
                        | Ok updated -> return Ok(mapStepToViewModel pipelineId (Some def) updated false false)
        }

module View =
    let private closeModalButton =
        _button
            [ _type_ "button"
              _class_ "text-slate-400 hover:text-slate-600 transition-colors"
              Hx.get "/pipelines/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            [ _i [ _class_ "fas fa-times text-xl" ] [] ]

    let private modalBackdrop =
        _div
            [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity"
              Hx.get "/pipelines/modal/close"
              Hx.targetCss "#modal-container"
              Hx.swapInnerHtml ]
            []

    let stepItem (step: StepItemViewModel) =
        let statusClass, statusIcon =
            if step.IsEnabled then
                "bg-green-50 text-green-700", "fa-check"
            else
                "bg-slate-50 text-slate-400", "fa-pause"

        _div
            [ _id_ $"step-{step.Id}"
              _class_ "border border-slate-200 rounded-md p-4 bg-white hover:bg-slate-50 transition-colors" ]
            [ _div
                  [ _class_ "flex items-start justify-between" ]
                  [ _div
                        [ _class_ "flex items-start space-x-3" ]
                        [ _div
                              [ _class_
                                    "flex-shrink-0 w-10 h-10 bg-slate-100 rounded-md flex items-center justify-center" ]
                              [ _i [ _class_ $"fas {step.Icon} text-slate-500" ] [] ]
                          _div
                              []
                              [ _div
                                    [ _class_ "flex items-center space-x-2" ]
                                    [ _span [ _class_ "font-medium text-slate-900" ] [ Text.raw step.DisplayName ]
                                      _span
                                          [ _class_ $"px-2 py-0.5 rounded-full text-xs {statusClass}" ]
                                          [ _i [ _class_ $"fas {statusIcon} mr-1" ] []
                                            Text.raw (if step.IsEnabled then "Enabled" else "Disabled") ] ]
                                _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw step.Description ]
                                if not (String.IsNullOrEmpty step.ParameterSummary) then
                                    _p
                                        [ _class_ "text-xs text-slate-400 mt-1 font-mono" ]
                                        [ Text.raw step.ParameterSummary ] ] ]

                    _div
                        [ _class_ "flex items-center space-x-1" ]
                        [
                          // move up
                          if not step.IsFirst then
                              _button
                                  [ _type_ "button"
                                    _class_ "p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded"
                                    _title_ "Move up"
                                    Hx.post $"/pipelines/{step.PipelineId}/steps/{step.Id}/move?direction=up"
                                    Hx.targetCss "#steps-list"
                                    Hx.swapInnerHtml ]
                                  [ _i [ _class_ "fas fa-chevron-up" ] [] ]

                          // move down
                          if not step.IsLast then
                              _button
                                  [ _type_ "button"
                                    _class_ "p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded"
                                    _title_ "Move down"
                                    Hx.post $"/pipelines/{step.PipelineId}/steps/{step.Id}/move?direction=down"
                                    Hx.targetCss "#steps-list"
                                    Hx.swapInnerHtml ]
                                  [ _i [ _class_ "fas fa-chevron-down" ] [] ]

                          // edit
                          _button
                              [ _type_ "button"
                                _class_ "p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded"
                                _title_ "Edit parameters"
                                Hx.get $"/pipelines/{step.PipelineId}/steps/{step.Id}/editor"
                                Hx.targetCss "#step-editor-container"
                                Hx.swapInnerHtml ]
                              [ _i [ _class_ "fas fa-cog" ] [] ]

                          // toggle
                          _button
                              [ _type_ "button"
                                _class_ (
                                    if step.IsEnabled then
                                        "p-1.5 text-yellow-400 hover:text-yellow-600 hover:bg-yellow-50 rounded"
                                    else
                                        "p-1.5 text-green-400 hover:text-green-600 hover:bg-green-50 rounded"
                                )
                                _title_ (if step.IsEnabled then "Disable" else "Enable")
                                Hx.post $"/pipelines/{step.PipelineId}/steps/{step.Id}/toggle"
                                Hx.targetCss $"#step-{step.Id}"
                                Hx.swapOuterHtml ]
                              [ _i [ _class_ (if step.IsEnabled then "fas fa-pause" else "fas fa-play") ] [] ]

                          // delete
                          _button
                              [ _type_ "button"
                                _class_ "p-1.5 text-red-400 hover:text-red-600 hover:bg-red-50 rounded"
                                _title_ "Delete"
                                Hx.delete $"/pipelines/{step.PipelineId}/steps/{step.Id}"
                                Hx.targetCss $"#step-{step.Id}"
                                Hx.swapOuterHtml
                                Hx.confirm "Are you sure you want to delete this step?" ]
                              [ _i [ _class_ "fas fa-trash" ] [] ] ] ] ]

    let stepsList (steps: StepItemViewModel list) =
        _div
            [ _id_ "steps-list"; _class_ "space-y-3" ]
            [ if steps.IsEmpty then
                  _div
                      [ _class_ "text-center py-8 text-slate-500" ]
                      [ _i [ _class_ "fas fa-layer-group text-3xl mb-2" ] []
                        _p [] [ Text.raw "No steps configured" ]
                        _p [ _class_ "text-sm" ] [ Text.raw "Add steps to define pipeline behavior" ] ]
              else
                  for step in steps do
                      stepItem step ]

    let stepSelector (pipelineId: int) (definitions: StepDefinitionViewModel list) =
        let grouped = definitions |> List.groupBy _.Category

        _div
            [ _class_ "p-4" ]
            [ _div
                  [ _class_ "flex items-center justify-between mb-4 pb-4 border-b" ]
                  [ _h3
                        [ _class_ "text-lg font-semibold text-slate-900" ]
                        [ _i [ _class_ "fas fa-plus-circle mr-2 text-slate-400" ] []; Text.raw "Add Step" ]
                    _button
                        [ _type_ "button"
                          _class_ "text-slate-400 hover:text-slate-600"
                          _onclick_ "document.getElementById('step-editor-container').innerHTML = ''" ]
                        [ _i [ _class_ "fas fa-times" ] [] ] ]

              _div
                  [ _class_ "space-y-4 max-h-[60vh] overflow-y-auto" ]
                  [ for (category, items) in grouped do
                        _div
                            []
                            [ _h4
                                  [ _class_ "text-sm font-semibold text-slate-700 uppercase tracking-wide mb-2" ]
                                  [ Text.raw category ]
                              _div
                                  [ _class_ "space-y-2" ]
                                  [ for def in items do
                                        let isDisabled = def.IsAlreadyInPipeline

                                        let buttonClass =
                                            if isDisabled then
                                                "w-full p-3 border rounded-md text-left bg-slate-50 cursor-not-allowed opacity-60"
                                            else
                                                "w-full p-3 border rounded-md text-left hover:border-slate-300 hover:bg-slate-50 transition-colors cursor-pointer"

                                        _button
                                            [ _type_ "button"
                                              _class_ buttonClass
                                              if not isDisabled then
                                                  Hx.post $"/pipelines/{pipelineId}/steps/add?stepTypeKey={def.Key}"
                                                  Hx.targetCss "#steps-list"
                                                  Hx.swap HxSwap.BeforeEnd
                                              if isDisabled then
                                                  _disabled_ ]
                                            [ _div
                                                  [ _class_ "flex items-start space-x-3" ]
                                                  [ _div
                                                        [ _class_
                                                              "flex-shrink-0 w-8 h-8 bg-slate-100 rounded flex items-center justify-center" ]
                                                        [ _i [ _class_ $"fas {def.Icon} text-slate-500 text-sm" ] [] ]
                                                    _div
                                                        []
                                                        [ _span
                                                              [ _class_ "font-medium text-slate-900" ]
                                                              [ Text.raw def.Name ]
                                                          if isDisabled then
                                                              _span
                                                                  [ _class_ "ml-2 text-xs text-slate-500" ]
                                                                  [ Text.raw "(already added)" ]
                                                          _p
                                                              [ _class_ "text-sm text-slate-500" ]
                                                              [ Text.raw def.Description ] ] ] ] ] ] ] ]

    let private paramValueToString (v: Parameters.ParamValue) =
        match v with
        | Parameters.StringValue s -> s
        | Parameters.DecimalValue d -> string d
        | Parameters.IntValue i -> string i
        | Parameters.BoolValue b -> if b then "true" else "false"
        | Parameters.ChoiceValue s -> s
        | Parameters.ListValue l -> String.concat (string Parameters.multiChoiceDelimiter) l

    let private parameterField (field: ParameterFieldViewModel) =
        let inputId = $"param-{field.Key}"

        let currentVal =
            field.CurrentValue
            |> Option.orElse (field.DefaultValue |> Option.map paramValueToString)
            |> Option.defaultValue ""

        _div
            [ _class_ "space-y-1" ]
            [ _label
                  [ _for_ inputId; _class_ "block text-sm font-medium text-slate-700" ]
                  [ Text.raw field.DisplayName
                    if field.IsRequired then
                        _span [ _class_ "text-red-500 ml-1" ] [ Text.raw "*" ] ]

              match field.Type with
              | Parameters.Bool ->
                  _div
                      [ _class_ "flex items-center" ]
                      [ _input
                            [ _id_ inputId
                              _name_ field.Key
                              _type_ "checkbox"
                              _class_ "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded"
                              _value_ "true"
                              if currentVal = "true" || currentVal = "True" then
                                  _checked_ ] ]

              | Parameters.Choice options ->
                  _select
                      [ _id_ inputId
                        _name_ field.Key
                        _class_
                            "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                      [ for opt in options do
                            if opt = currentVal then
                                _option [ _value_ opt; _selected_ ] [ Text.raw opt ]
                            else
                                _option [ _value_ opt ] [ Text.raw opt ] ]

              | Parameters.MultiChoice options ->
                  let selectedValues =
                      currentVal.Split(Parameters.multiChoiceDelimiter) |> Array.map _.Trim() |> Set.ofArray

                  let containerId = $"mc-{field.Key}"

                  let syncScript =
                      $"var h=document.getElementById('{inputId}');var c=document.getElementById('{containerId}');h.value=Array.from(c.querySelectorAll('input:checked')).map(i=>i.value).join('{Parameters.multiChoiceDelimiter}')"

                  _div
                      [ _class_ "space-y-2" ]
                      [ _input [ _id_ inputId; _name_ field.Key; _type_ "hidden"; _value_ currentVal ]
                        _div
                            [ _id_ containerId
                              _class_ "max-h-48 overflow-y-auto space-y-1 border border-slate-200 rounded-md p-2" ]
                            [ for opt in options do
                                  _label
                                      [ _class_
                                            "flex items-center space-x-2 cursor-pointer py-1 px-1 hover:bg-slate-50 rounded" ]
                                      [ _input
                                            [ _type_ "checkbox"
                                              _class_
                                                  "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded"
                                              _value_ opt
                                              _onchange_ syncScript
                                              if Set.contains opt selectedValues then
                                                  _checked_ ]
                                        _span [ _class_ "text-sm text-slate-700" ] [ Text.raw opt ] ] ] ]

              | Parameters.Int(minVal, maxVal) ->
                  _input
                      [ _id_ inputId
                        _name_ field.Key
                        _type_ "number"
                        _value_ currentVal
                        _class_
                            "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                        match minVal with
                        | Some m -> _min_ (string m)
                        | None -> ()
                        match maxVal with
                        | Some m -> _max_ (string m)
                        | None -> () ]

              | Parameters.Decimal(minVal, maxVal) ->
                  _input
                      [ _id_ inputId
                        _name_ field.Key
                        _type_ "number"
                        _step_ "0.01"
                        _value_ currentVal
                        _class_
                            "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                        match minVal with
                        | Some m -> _min_ (string m)
                        | None -> ()
                        match maxVal with
                        | Some m -> _max_ (string m)
                        | None -> () ]

              | Parameters.String ->
                  _input
                      [ _id_ inputId
                        _name_ field.Key
                        _type_ "text"
                        _value_ currentVal
                        _class_
                            "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]

              if not (String.IsNullOrEmpty field.Description) then
                  _p [ _class_ "text-xs text-slate-500" ] [ Text.raw field.Description ] ]

    let stepEditor (vm: StepEditorViewModel) =
        _div
            [ _class_ "border-l border-slate-100 bg-white p-4" ]
            [ _div
                  [ _class_ "flex items-center justify-between mb-4 pb-4 border-b" ]
                  [ _div
                        [ _class_ "flex items-center space-x-3" ]
                        [ _div
                              [ _class_ "w-10 h-10 bg-slate-100 rounded-md flex items-center justify-center" ]
                              [ _i [ _class_ $"fas {vm.StepIcon} text-slate-500" ] [] ]
                          _div
                              []
                              [ _h3 [ _class_ "font-semibold text-slate-900" ] [ Text.raw vm.StepName ]
                                _p [ _class_ "text-sm text-slate-500" ] [ Text.raw "Edit Parameters" ] ] ]
                    _button
                        [ _type_ "button"
                          _class_ "text-slate-400 hover:text-slate-600"
                          _onclick_ "document.getElementById('step-editor-container').innerHTML = ''" ]
                        [ _i [ _class_ "fas fa-times" ] [] ] ]

              if not vm.Errors.IsEmpty then
                  _div
                      [ _class_ "mb-4 p-3 bg-red-50 border border-red-200 rounded-md" ]
                      [ _ul
                            [ _class_ "text-sm text-red-700" ]
                            [ for err in vm.Errors do
                                  _li [] [ Text.raw err ] ] ]

              _form
                  [ Hx.post $"/pipelines/{vm.PipelineId}/steps/{vm.StepId}/save"
                    Hx.targetCss $"#step-{vm.StepId}"
                    Hx.swapOuterHtml
                    Attr.create
                        "hx-on::after-swap"
                        "if(event.detail.target.id!=='step-editor-container'){document.getElementById('step-editor-container').innerHTML=''}" ]
                  [ _div
                        [ _class_ "space-y-4" ]
                        [ for field in vm.Fields do
                              parameterField field ]

                    _div
                        [ _class_ "mt-6 flex justify-end space-x-3" ]
                        [ _button
                              [ _type_ "button"
                                _class_
                                    "px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 rounded-md transition-colors"
                                _onclick_ "document.getElementById('step-editor-container').innerHTML = ''" ]
                              [ Text.raw "Cancel" ]
                          _button
                              [ _type_ "submit"
                                _class_
                                    "px-3 py-2 text-sm bg-slate-900 hover:bg-slate-800 text-white rounded-md transition-colors" ]
                              [ _i [ _class_ "fas fa-save mr-1" ] []; Text.raw "Save" ] ] ] ]

    let stepEditorEmpty = _div [ _id_ "step-editor-container" ] []

    let private settingsForm (vm: EditPipelineViewModel) =
        _form
            [ Hx.post $"/pipelines/{vm.Id}/edit"; Hx.targetCss "#modal-container"; Hx.swapInnerHtml ]
            [ _div
                  [ _class_ "space-y-4" ]
                  [
                    // market type
                    _div
                        []
                        [ _label
                              [ _for_ "marketType"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                              [ Text.raw "Market Type "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                          _select
                              [ _id_ "marketType"
                                _name_ "marketType"
                                _class_
                                    "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                                Hx.get "/instruments/base-currencies"
                                Hx.trigger "change"
                                Hx.includeCss "[name='marketType']"
                                Hx.targetCss "#baseCurrency"
                                Hx.swap HxSwap.InnerHTML
                                Attr.create
                                    "hx-on::after-settle"
                                    "var q=document.getElementById('quoteCurrency');if(q)htmx.trigger(q,'change');var s=document.getElementById('symbol');if(s)s.value=''" ]
                              [ for mt in vm.MarketTypes do
                                    if mt = vm.MarketType then
                                        _option [ _value_ (string (int mt)); _selected_ ] [ Text.raw (mt.ToString()) ]
                                    else
                                        _option [ _value_ (string (int mt)) ] [ Text.raw (mt.ToString()) ] ] ]

                    // symbol (base + quote dropdowns)
                    _input [ _id_ "symbol"; _name_ "symbol"; _type_ "hidden"; _value_ vm.Symbol ]
                    _div
                        []
                        [ _label
                              [ _for_ "baseCurrency"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                              [ Text.raw "Base Currency "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                          _select
                              [ _id_ "baseCurrency"
                                _name_ "baseCurrency"
                                _class_
                                    "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                                Hx.get "/instruments/quote-currencies"
                                Hx.trigger "change"
                                Hx.includeCss "[name='marketType'],[name='baseCurrency']"
                                Hx.targetCss "#quoteCurrency"
                                Hx.swap HxSwap.InnerHTML
                                Attr.create
                                    "hx-on::after-settle"
                                    "var b=document.getElementById('baseCurrency'),q=document.getElementById('quoteCurrency'),s=document.getElementById('symbol');if(b&&q&&s&&b.value&&q.value)s.value=b.value+'-'+q.value;else if(s)s.value=''" ]
                              [ _option [ _value_ "" ] [ Text.raw "-- Select --" ]
                                for c in vm.BaseCurrencies do
                                    if c = vm.BaseCurrency then
                                        _option [ _value_ c; _selected_ ] [ Text.raw c ]
                                    else
                                        _option [ _value_ c ] [ Text.raw c ] ] ]
                    _div
                        []
                        [ _label
                              [ _for_ "quoteCurrency"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                              [ Text.raw "Quote Currency "; _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                          _select
                              [ _id_ "quoteCurrency"
                                _name_ "quoteCurrency"
                                _class_
                                    "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                                Attr.create
                                    "hx-on:change"
                                    "var b=document.getElementById('baseCurrency'),q=document.getElementById('quoteCurrency'),s=document.getElementById('symbol');if(b&&q&&s&&b.value&&q.value)s.value=b.value+'-'+q.value;else if(s)s.value=''" ]
                              [ _option [ _value_ "" ] [ Text.raw "-- Select --" ]
                                for c in vm.QuoteCurrencies do
                                    if c = vm.QuoteCurrency then
                                        _option [ _value_ c; _selected_ ] [ Text.raw c ]
                                    else
                                        _option [ _value_ c ] [ Text.raw c ] ] ]

                    // tags
                    _div
                        []
                        [ _label
                              [ _for_ "tags"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                              [ Text.raw "Tags" ]
                          _input
                              [ _id_ "tags"
                                _name_ "tags"
                                _type_ "text"
                                _value_ vm.Tags
                                _placeholder_ "e.g., scalping, btc"
                                _class_
                                    "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                          _p [ _class_ "text-sm text-slate-500 mt-1" ] [ Text.raw "Comma-separated tags" ] ]

                    // execution interval
                    _div
                        []
                        [ _label
                              [ _for_ "executionInterval"; _class_ "block text-sm font-medium text-slate-600 mb-1.5" ]
                              [ Text.raw "Execution Interval (minutes) "
                                _span [ _class_ "text-red-500" ] [ Text.raw "*" ] ]
                          _input
                              [ _id_ "executionInterval"
                                _name_ "executionInterval"
                                _type_ "number"
                                _value_ (string vm.ExecutionInterval)
                                _min_ "1"
                                _class_
                                    "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300"
                                _required_ ] ]

                    // enabled
                    _div
                        [ _class_ "flex items-center" ]
                        [ _input
                              [ _id_ "enabled"
                                _name_ "enabled"
                                _type_ "checkbox"
                                _class_ "h-4 w-4 text-slate-900 focus:ring-slate-300 border-slate-200 rounded"
                                if vm.Enabled then
                                    _checked_ ]
                          _label
                              [ _for_ "enabled"; _class_ "ml-2 block text-sm text-slate-700" ]
                              [ Text.raw "Pipeline enabled" ] ]

                    // submit
                    _div
                        [ _class_ "pt-4 border-t" ]
                        [ _button
                              [ _type_ "submit"
                                _class_
                                    "w-full px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors" ]
                              [ _i [ _class_ "fas fa-save mr-2" ] []; Text.raw "Save Settings" ] ] ] ]

    let modal (vm: EditPipelineViewModel) =
        _div
            [ _id_ "pipeline-edit-modal"
              _class_ "fixed inset-0 z-50 overflow-y-auto"
              Attr.create "aria-labelledby" "modal-title"
              _role_ "dialog"
              Attr.create "aria-modal" "true" ]
            [ modalBackdrop

              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg transition-all w-full max-w-7xl" ]
                              [
                                // header
                                _div
                                    [ _class_ "border-b border-slate-100 px-6 py-4" ]
                                    [ _div
                                          [ _class_ "flex items-center justify-between" ]
                                          [ _div
                                                []
                                                [ _h3
                                                      [ _id_ "modal-title"
                                                        _class_ "text-lg font-semibold text-slate-900" ]
                                                      [ _i [ _class_ "fas fa-edit mr-2 text-slate-400" ] []
                                                        Text.raw "Edit Pipeline" ]
                                                  _p
                                                      [ _class_ "text-slate-500 text-sm mt-1" ]
                                                      [ Text.raw $"{vm.Symbol} • ID: {vm.Id}" ] ]
                                            closeModalButton ] ]

                                // content
                                _div
                                    [ _class_ "flex max-h-[70vh]" ]
                                    [
                                      // left column
                                      _div
                                          [ _class_ "w-1/4 p-6 border-r overflow-y-auto" ]
                                          [ _h4
                                                [ _class_
                                                      "text-sm font-semibold text-slate-700 uppercase tracking-wide mb-4" ]
                                                [ _i [ _class_ "fas fa-cog mr-2" ] []; Text.raw "Settings" ]
                                            settingsForm vm ]

                                      // middle column
                                      _div
                                          [ _class_ "w-1/2 p-6 border-r overflow-y-auto" ]
                                          [ _div
                                                [ _class_ "flex items-center justify-between mb-4" ]
                                                [ _h4
                                                      [ _class_
                                                            "text-sm font-semibold text-slate-700 uppercase tracking-wide" ]
                                                      [ _i [ _class_ "fas fa-layer-group mr-2" ] []; Text.raw "Steps" ]
                                                  _button
                                                      [ _type_ "button"
                                                        _class_
                                                            "px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 text-sm font-medium rounded-md transition-colors"
                                                        Hx.get $"/pipelines/{vm.Id}/steps/selector"
                                                        Hx.targetCss "#step-editor-container"
                                                        Hx.swapInnerHtml ]
                                                      [ _i [ _class_ "fas fa-plus mr-1" ] []; Text.raw "Add" ] ]
                                            stepsList vm.Steps ]

                                      // right column
                                      _div [ _id_ "step-editor-container"; _class_ "w-1/4 overflow-y-auto" ] [] ]

                                // footer
                                _div
                                    [ _class_ "px-6 py-4 flex justify-between items-center border-t border-slate-100" ]
                                    [ _div
                                          [ _class_
                                                "bg-slate-50 border border-slate-200 rounded-md p-2 text-xs text-slate-500" ]
                                          [ _i [ _class_ "fas fa-info-circle mr-1" ] []
                                            Text.raw "Steps execute in order from top to bottom" ]
                                      _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/pipelines/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ _i [ _class_ "fas fa-times mr-2" ] []; Text.raw "Close" ] ] ] ] ] ]

    let successResponse (pipelineId: int) =
        _div
            [ _id_ "pipeline-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ modalBackdrop
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-check text-3xl text-green-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Pipeline Updated!" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw "Your changes have been saved successfully." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/pipelines/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml
                                      Attr.create "hx-on::after-request" "htmx.trigger('#pipelines-container', 'load')" ]
                                    [ Text.raw "Close" ] ] ] ] ]

    let errorResponse (message: string) (pipelineId: int) =
        _div
            [ _id_ "pipeline-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ modalBackdrop
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-exclamation-triangle text-3xl text-red-600" ] [] ]
                                _h3 [ _class_ "text-lg font-semibold text-slate-900 mb-2" ] [ Text.raw "Error" ]
                                _p [ _class_ "text-slate-600 mb-4" ] [ Text.raw message ]
                                _div
                                    [ _class_ "flex justify-center space-x-3" ]
                                    [ _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                            Hx.get "/pipelines/modal/close"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Close" ]
                                      _button
                                          [ _type_ "button"
                                            _class_
                                                "px-4 py-2 bg-slate-900 hover:bg-slate-800 text-white font-medium text-sm rounded-md transition-colors"
                                            Hx.get $"/pipelines/{pipelineId}/edit/modal"
                                            Hx.targetCss "#modal-container"
                                            Hx.swapInnerHtml ]
                                          [ Text.raw "Try Again" ] ] ] ] ] ]

    let notFound =
        _div
            [ _id_ "pipeline-edit-modal"; _class_ "fixed inset-0 z-50 overflow-y-auto" ]
            [ modalBackdrop
              _div
                  [ _class_ "fixed inset-0 z-10 overflow-y-auto" ]
                  [ _div
                        [ _class_ "flex min-h-full items-center justify-center p-4" ]
                        [ _div
                              [ _class_
                                    "relative transform overflow-hidden rounded-lg bg-white shadow-lg w-full max-w-md p-6 text-center" ]
                              [ _div
                                    [ _class_
                                          "mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-red-50 mb-4" ]
                                    [ _i [ _class_ "fas fa-exclamation-triangle text-3xl text-red-600" ] [] ]
                                _h3
                                    [ _class_ "text-lg font-semibold text-slate-900 mb-2" ]
                                    [ Text.raw "Pipeline Not Found" ]
                                _p
                                    [ _class_ "text-slate-600 mb-4" ]
                                    [ Text.raw "The requested pipeline could not be found." ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "px-4 py-2 text-slate-600 hover:bg-slate-100 font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/pipelines/modal/close"
                                      Hx.targetCss "#modal-container"
                                      Hx.swapInnerHtml ]
                                    [ Text.raw "Close" ] ] ] ] ]

module Handler =
    let modal (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! vm = Data.getEditViewModel scopeFactory pipelineId ctx.RequestAborted

                    match vm with
                    | Some v -> return! Response.ofHtml (View.modal v) ctx
                    | None -> return! Response.ofHtml View.notFound ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineEdit")
                    logger.LogError(ex, "Error getting pipeline edit view for {PipelineId}", pipelineId)
                    return! Response.ofHtml View.notFound ctx
            }

    let update (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                try
                    let! form = Request.getForm ctx
                    let formData = Data.parseFormData form
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! result = Data.updatePipeline scopeFactory pipelineId formData ctx.RequestAborted

                    match result with
                    | Success -> return! Response.ofHtml (View.successResponse pipelineId) ctx
                    | ValidationError msg -> return! Response.ofHtml (View.errorResponse msg pipelineId) ctx
                    | NotFoundError -> return! Response.ofHtml View.notFound ctx
                    | ServerError msg -> return! Response.ofHtml (View.errorResponse msg pipelineId) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("PipelineEdit")
                    logger.LogError(ex, "Error updating pipeline {PipelineId}", pipelineId)
                    return! Response.ofHtml (View.errorResponse "An unexpected error occurred" pipelineId) ctx
            }

    let stepsList (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! steps = Data.getSteps scopeFactory pipelineId ctx.RequestAborted
                return! Response.ofHtml (View.stepsList steps) ctx
            }

    let stepSelector (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! defs = Data.getStepDefinitions scopeFactory pipelineId ctx.RequestAborted
                return! Response.ofHtml (View.stepSelector pipelineId defs) ctx
            }

    let stepEditor (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! vm = Data.getStepEditor scopeFactory pipelineId stepId ctx.RequestAborted

                match vm with
                | Some v -> return! Response.ofHtml (View.stepEditor v) ctx
                | None -> return! Response.ofHtml View.stepEditorEmpty ctx
            }

    let addStep (pipelineId: int) : HttpHandler =
        fun ctx ->
            task {
                let stepTypeKey =
                    ctx.Request.Query
                    |> Seq.tryFind (fun kvp -> kvp.Key = "stepTypeKey")
                    |> Option.bind (fun kvp -> kvp.Value |> Seq.tryHead)
                    |> Option.defaultValue ""

                if String.IsNullOrEmpty stepTypeKey then
                    return! Response.ofEmpty ctx
                else
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! step = Data.addStep scopeFactory pipelineId stepTypeKey ctx.RequestAborted

                    match step with
                    | Some s -> return! Response.ofHtml (View.stepItem s) ctx
                    | None -> return! Response.ofEmpty ctx
            }

    let toggleStep (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! step = Data.toggleStep scopeFactory pipelineId stepId ctx.RequestAborted

                match step with
                | Some s -> return! Response.ofHtml (View.stepItem s) ctx
                | None -> return! Response.ofEmpty ctx
            }

    let deleteStep (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! _ = Data.deleteStep scopeFactory pipelineId stepId ctx.RequestAborted
                return! Response.ofEmpty ctx
            }

    let moveStep (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let direction =
                    ctx.Request.Query
                    |> Seq.tryFind (fun kvp -> kvp.Key = "direction")
                    |> Option.bind (fun kvp -> kvp.Value |> Seq.tryHead)
                    |> Option.defaultValue ""

                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! steps = Data.moveStep scopeFactory pipelineId stepId direction ctx.RequestAborted
                return! Response.ofHtml (View.stepsList steps) ctx
            }

    let saveStep (pipelineId: int) (stepId: int) : HttpHandler =
        fun ctx ->
            task {
                let! form = Request.getForm ctx
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                let! result = Data.saveStepParams scopeFactory pipelineId stepId form ctx.RequestAborted

                match result with
                | Ok step -> return! Response.ofHtml (View.stepItem step) ctx
                | Error vm ->
                    ctx.Response.Headers["HX-Retarget"] <- "#step-editor-container"
                    ctx.Response.Headers["HX-Reswap"] <- "innerHTML"
                    return! Response.ofHtml (View.stepEditor vm) ctx
            }
