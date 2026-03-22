namespace Plutus.Core.Adapters

open System
open System.Data
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Ports
open Plutus.Core.Shared.Errors

[<CLIMutable>]
type private PipelineEntity =
    { Id: int
      Name: string
      Instrument: Instrument
      MarketType: MarketType
      Enabled: bool
      ExecutionInterval: TimeSpan
      LastExecutedAt: DateTime option
      Status: PipelineStatus
      Tags: string list
      CreatedAt: DateTime
      UpdatedAt: DateTime }

module Pipelines =
    let private toPipeline (e: PipelineEntity) : Result<Pipeline, string> =
        match PipelineId.create e.Id, PipelineName.create e.Name with
        | Ok id, Ok name ->
            Ok
                { Id = id
                  Name = name
                  Instrument = e.Instrument
                  MarketType = e.MarketType
                  Enabled = e.Enabled
                  ExecutionInterval = e.ExecutionInterval
                  LastExecutedAt = e.LastExecutedAt
                  Status = e.Status
                  Tags = e.Tags
                  CreatedAt = e.CreatedAt
                  UpdatedAt = e.UpdatedAt }
        | Error e, _ -> Error $"Invalid pipeline ID: {e}"
        | _, Error e -> Error $"Invalid pipeline name: {e}"

    let getById (db: IDbConnection) : GetPipelineById =
        fun id token ->
            task {
                try
                    let! result =
                        db.QueryFirstOrDefaultAsync<PipelineEntity>(
                            CommandDefinition(
                                "SELECT * FROM pipelines WHERE id = @Id",
                                {| Id = PipelineId.value id |},
                                cancellationToken = token
                            )
                        )

                    match box result with
                    | null -> return Error(NotFound $"Pipeline with id {PipelineId.value id}")
                    | _ ->
                        match toPipeline result with
                        | Ok p -> return Ok p
                        | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let getAll (db: IDbConnection) : GetAllPipelines =
        fun token ->
            task {
                try
                    let! results =
                        db.QueryAsync<PipelineEntity>(
                            CommandDefinition("SELECT * FROM pipelines ORDER BY id", cancellationToken = token)
                        )

                    let pipelines = results |> Seq.toList |> List.choose (toPipeline >> Result.toOption)
                    return Ok pipelines
                with ex ->
                    return Error(Unexpected ex)
            }

    let create (db: IDbConnection) : CreatePipeline =
        fun pipeline token ->
            task {
                try
                    let! id =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                """INSERT INTO pipelines
                                   (name, instrument, market_type, enabled, execution_interval, last_executed_at, status, tags, created_at, updated_at)
                                   VALUES (@Name, @Instrument, @MarketType, @Enabled, @ExecutionInterval, @LastExecutedAt, @Status, @Tags::jsonb, now(), now())
                                   RETURNING id""",
                                {| Name = PipelineName.value pipeline.Name
                                   Instrument = pipeline.Instrument
                                   MarketType = int pipeline.MarketType
                                   Enabled = pipeline.Enabled
                                   ExecutionInterval = pipeline.ExecutionInterval
                                   LastExecutedAt = pipeline.LastExecutedAt
                                   Status = int pipeline.Status
                                   Tags = pipeline.Tags |},
                                cancellationToken = token
                            )
                        )

                    let! entity =
                        db.QuerySingleAsync<PipelineEntity>(
                            CommandDefinition(
                                "SELECT * FROM pipelines WHERE id = @Id",
                                {| Id = id |},
                                cancellationToken = token
                            )
                        )

                    match toPipeline entity with
                    | Ok p -> return Ok p
                    | Error e -> return Error(Unexpected(Exception(e)))
                with ex ->
                    return Error(Unexpected ex)
            }

    let update (db: IDbConnection) : UpdatePipeline =
        fun pipeline token ->
            task {
                try
                    let idVal = PipelineId.value pipeline.Id

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """UPDATE pipelines
                                   SET name = @Name, instrument = @Instrument, market_type = @MarketType,
                                       enabled = @Enabled, execution_interval = @ExecutionInterval,
                                       last_executed_at = @LastExecutedAt, status = @Status, tags = @Tags::jsonb,
                                       updated_at = now()
                                   WHERE id = @Id""",
                                {| Id = idVal
                                   Name = PipelineName.value pipeline.Name
                                   Instrument = pipeline.Instrument
                                   MarketType = int pipeline.MarketType
                                   Enabled = pipeline.Enabled
                                   ExecutionInterval = pipeline.ExecutionInterval
                                   LastExecutedAt = pipeline.LastExecutedAt
                                   Status = int pipeline.Status
                                   Tags = pipeline.Tags |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then
                        let! entity =
                            db.QuerySingleAsync<PipelineEntity>(
                                CommandDefinition(
                                    "SELECT * FROM pipelines WHERE id = @Id",
                                    {| Id = idVal |},
                                    cancellationToken = token
                                )
                            )

                        match toPipeline entity with
                        | Ok p -> return Ok p
                        | Error e -> return Error(Unexpected(Exception(e)))
                    else
                        return Error(NotFound $"Pipeline with id {idVal}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let delete (db: IDbConnection) : DeletePipeline =
        fun id token ->
            task {
                try
                    let idVal = PipelineId.value id

                    let! rowsAffected =
                        db.ExecuteAsync(
                            CommandDefinition(
                                "DELETE FROM pipelines WHERE id = @Id",
                                {| Id = idVal |},
                                cancellationToken = token
                            )
                        )

                    if rowsAffected > 0 then return Ok() else return Error(NotFound $"Pipeline with id {idVal}")
                with ex ->
                    return Error(Unexpected ex)
            }

    let count (db: IDbConnection) : CountPipelines =
        fun token ->
            task {
                try
                    let! result =
                        db.QuerySingleAsync<int>(
                            CommandDefinition("SELECT COUNT(1) FROM pipelines", cancellationToken = token)
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
            }

    let countEnabled (db: IDbConnection) : CountEnabledPipelines =
        fun token ->
            task {
                try
                    let! result =
                        db.QuerySingleAsync<int>(
                            CommandDefinition(
                                "SELECT COUNT(1) FROM pipelines WHERE enabled = true",
                                cancellationToken = token
                            )
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
            }

    let getAllTags (db: IDbConnection) : GetAllPipelineTags =
        fun token ->
            task {
                try
                    let! results =
                        db.QueryAsync<string>(
                            CommandDefinition(
                                """SELECT DISTINCT jsonb_array_elements_text(tags) AS tag
                                   FROM pipelines
                                   WHERE tags IS NOT NULL AND jsonb_array_length(tags) > 0
                                   ORDER BY tag ASC""",
                                cancellationToken = token
                            )
                        )

                    return Ok(results |> Seq.toList)
                with ex ->
                    return Error(Unexpected ex)
            }

    let search (db: IDbConnection) : SearchPipelines =
        fun filters skip take token ->
            task {
                try
                    let conditions = ResizeArray<string>()
                    let parameters = DynamicParameters()

                    match filters.SearchTerm with
                    | Some term when not (String.IsNullOrEmpty term) ->
                        conditions.Add("AND instrument ILIKE @SearchTerm")
                        parameters.Add("SearchTerm", $"%%{term}%%")
                    | _ -> ()

                    match filters.MarketType with
                    | Some marketType when not (String.IsNullOrEmpty marketType) ->
                        match Enum.TryParse<MarketType>(marketType) with
                        | true, mt ->
                            conditions.Add("AND market_type = @MarketType")
                            parameters.Add("MarketType", int mt)
                        | false, _ -> ()
                    | _ -> ()

                    match filters.Status with
                    | Some status ->
                        let isEnabled = status = PipelineStatus.Running
                        conditions.Add("AND enabled = @Enabled")
                        parameters.Add("Enabled", isEnabled)
                    | None -> ()

                    let orderClause =
                        match filters.SortBy with
                        | "instrument-desc" -> "ORDER BY instrument DESC"
                        | "account" -> "ORDER BY market_type ASC"
                        | "account-desc" -> "ORDER BY market_type DESC"
                        | "status" -> "ORDER BY enabled ASC"
                        | "status-desc" -> "ORDER BY enabled DESC"
                        | "updated" -> "ORDER BY updated_at ASC"
                        | "updated-desc" -> "ORDER BY updated_at DESC"
                        | _ -> "ORDER BY instrument ASC"

                    let whereClause = String.Join(" ", conditions)
                    parameters.Add("Skip", skip)
                    parameters.Add("Take", take)

                    let countSql = $"SELECT COUNT(1) FROM pipelines WHERE 1=1 {whereClause}"
                    let dataSql = $"SELECT * FROM pipelines WHERE 1=1 {whereClause} {orderClause} OFFSET @Skip LIMIT @Take"

                    let! totalCount =
                        db.QuerySingleAsync<int>(CommandDefinition(countSql, parameters, cancellationToken = token))

                    let! results =
                        db.QueryAsync<PipelineEntity>(CommandDefinition(dataSql, parameters, cancellationToken = token))

                    let pipelines = results |> Seq.toList |> List.choose (toPipeline >> Result.toOption)

                    let filteredPipelines =
                        match filters.Tag with
                        | Some tag when not (String.IsNullOrEmpty tag) ->
                            pipelines |> List.filter (fun p -> p.Tags |> List.contains tag)
                        | _ -> pipelines

                    return Ok { Pipelines = filteredPipelines; TotalCount = totalCount }
                with ex ->
                    return Error(Unexpected ex)
            }
