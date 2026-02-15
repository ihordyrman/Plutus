namespace Plutus.Dashboard.Api

open System
open System.Collections.Generic
open Plutus.Core.Domain
open Plutus.Core.Pipelines.Core.Parameters
open Plutus.Core.Pipelines.Core.Steps

module ApiDtos =

    // Request DTOs

    [<CLIMutable>]
    type CreatePipelineRequest =
        { Symbol: string; MarketType: int; ExecutionIntervalMinutes: int; Enabled: bool; Tags: string list }

    [<CLIMutable>]
    type UpdatePipelineRequest =
        { Symbol: string; MarketType: int; ExecutionIntervalMinutes: int; Enabled: bool; Tags: string list }

    [<CLIMutable>]
    type AddStepRequest = { StepTypeKey: string; IsEnabled: bool; Parameters: Dictionary<string, string> }

    [<CLIMutable>]
    type BulkStepsRequest = { Steps: AddStepRequest list }

    [<CLIMutable>]
    type RunBacktestRequest =
        { PipelineId: int
          StartDate: DateTime
          EndDate: DateTime
          IntervalMinutes: int
          InitialCapital: decimal }

    // Response DTOs

    type ParameterDefDto =
        { Key: string
          Name: string
          Description: string
          Type: string
          Required: bool
          DefaultValue: obj
          Group: string option
          Min: obj
          Max: obj
          Options: string list option }

    type StepDefinitionDto =
        { Key: string
          Name: string
          Description: string
          Category: string
          Icon: string
          Parameters: ParameterDefDto list }

    type PipelineDto =
        { Id: int
          Name: string
          Symbol: string
          MarketType: int
          Enabled: bool
          ExecutionIntervalMinutes: int
          Status: int
          Tags: string list
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    type StepDto =
        { Id: int
          PipelineId: int
          StepTypeKey: string
          Name: string
          Order: int
          IsEnabled: bool
          Parameters: Dictionary<string, string>
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    type PipelineDetailDto =
        { Id: int
          Name: string
          Symbol: string
          MarketType: int
          Enabled: bool
          ExecutionIntervalMinutes: int
          Status: int
          Tags: string list
          Steps: StepDto list
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    type TradeDto =
        { EntryTime: DateTime
          ExitTime: DateTime
          EntryPrice: decimal
          ExitPrice: decimal
          Quantity: decimal
          Pnl: decimal }

    type BacktestRunDto =
        { Id: int
          PipelineId: int
          Status: int
          StartDate: DateTime
          EndDate: DateTime
          IntervalMinutes: int
          InitialCapital: decimal
          FinalCapital: Nullable<decimal>
          TotalTrades: int
          WinRate: Nullable<decimal>
          MaxDrawdown: Nullable<decimal>
          SharpeRatio: Nullable<decimal>
          ErrorMessage: string
          CreatedAt: DateTime
          CompletedAt: Nullable<DateTime> }

    // Mapping functions

    let private paramValueToObj (v: ParamValue) : obj =
        match v with
        | StringValue s -> box s
        | DecimalValue d -> box d
        | IntValue i -> box i
        | BoolValue b -> box b
        | ChoiceValue s -> box s
        | ListValue lst -> box lst

    let private categoryToString (c: StepCategory) =
        match c with
        | StepCategory.Validation -> "validation"
        | StepCategory.Risk -> "risk"
        | StepCategory.Signal -> "signal"
        | StepCategory.Execution -> "execution"
        | _ -> "unknown"

    let toParameterDefDto (p: ParameterDef) : ParameterDefDto =
        let typeName, min, max, options =
            match p.Type with
            | String -> "string", null, null, None
            | Decimal(mn, mx) ->
                "decimal", (mn |> Option.map box |> Option.toObj), (mx |> Option.map box |> Option.toObj), None
            | Int(mn, mx) -> "int", (mn |> Option.map box |> Option.toObj), (mx |> Option.map box |> Option.toObj), None
            | Bool -> "bool", null, null, None
            | Choice opts -> "choice", null, null, Some opts
            | MultiChoice opts -> "multi_choice", null, null, Some opts

        { Key = p.Key
          Name = p.Name
          Description = p.Description
          Type = typeName
          Required = p.Required
          DefaultValue = p.DefaultValue |> Option.map paramValueToObj |> Option.toObj
          Group = p.Group
          Min = min
          Max = max
          Options = options }

    let toStepDefinitionDto (d: StepDefinition<_>) : StepDefinitionDto =
        { Key = d.Key
          Name = d.Name
          Description = d.Description
          Category = categoryToString d.Category
          Icon = d.Icon
          Parameters = d.ParameterSchema.Parameters |> List.map toParameterDefDto }

    let toStepDto (s: PipelineStep) : StepDto =
        { Id = s.Id
          PipelineId = s.PipelineId
          StepTypeKey = s.StepTypeKey
          Name = s.Name
          Order = s.Order
          IsEnabled = s.IsEnabled
          Parameters = s.Parameters
          CreatedAt = s.CreatedAt
          UpdatedAt = s.UpdatedAt }

    let toPipelineDto (p: Pipeline) : PipelineDto =
        { Id = p.Id
          Name = p.Name
          Symbol = p.Symbol
          MarketType = int p.MarketType
          Enabled = p.Enabled
          ExecutionIntervalMinutes = int p.ExecutionInterval.TotalMinutes
          Status = int p.Status
          Tags = p.Tags
          CreatedAt = p.CreatedAt
          UpdatedAt = p.UpdatedAt }

    let toPipelineDetailDto (p: Pipeline) (steps: PipelineStep list) : PipelineDetailDto =
        { Id = p.Id
          Name = p.Name
          Symbol = p.Symbol
          MarketType = int p.MarketType
          Enabled = p.Enabled
          ExecutionIntervalMinutes = int p.ExecutionInterval.TotalMinutes
          Status = int p.Status
          Tags = p.Tags
          Steps = steps |> List.map toStepDto
          CreatedAt = p.CreatedAt
          UpdatedAt = p.UpdatedAt }

    let toBacktestRunDto (r: BacktestRun) : BacktestRunDto =
        { Id = r.Id
          PipelineId = r.PipelineId
          Status = int r.Status
          StartDate = r.StartDate
          EndDate = r.EndDate
          IntervalMinutes = r.IntervalMinutes
          InitialCapital = r.InitialCapital
          FinalCapital = r.FinalCapital
          TotalTrades = r.TotalTrades
          WinRate = r.WinRate
          MaxDrawdown = r.MaxDrawdown
          SharpeRatio = r.SharpeRatio
          ErrorMessage = r.ErrorMessage
          CreatedAt = r.CreatedAt
          CompletedAt = r.CompletedAt }

    let toTradeDto (buy: BacktestTrade) (sell: BacktestTrade) : TradeDto =
        { EntryTime = buy.CandleTime
          ExitTime = sell.CandleTime
          EntryPrice = buy.Price
          ExitPrice = sell.Price
          Quantity = sell.Quantity
          Pnl = (sell.Price - buy.Price) * sell.Quantity }
