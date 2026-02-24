namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type PipelineSearchFilters =
    { SearchTerm: string option
      Tag: string option
      MarketType: string option
      Status: PipelineStatus option
      SortBy: string }

type PipelineSearchResult = { Pipelines: Pipeline list; TotalCount: int }

[<RequireQualifiedAccess>]
module PipelineRepository =

    let getById (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! pipeline =
                    db.QueryFirstOrDefaultAsync<Pipeline>(
                        CommandDefinition(
                            "SELECT * FROM pipelines WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                match box pipeline with
                | null -> return Error(NotFound $"Pipeline with id {id}")
                | _ -> return Ok pipeline
            with ex ->
                return Error(Unexpected ex)
        }

    let getAll (db: IDbConnection) (token: CancellationToken) =
        task {
            try
                let! pipelines =
                    db.QueryAsync<Pipeline>(
                        CommandDefinition("SELECT * FROM pipelines ORDER BY id", cancellationToken = token)
                    )

                return Ok(pipelines |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let create (db: IDbConnection) (pipeline: Pipeline) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO pipelines
                           (name, instrument, market_type, enabled, execution_interval, last_executed_at, status, tags, created_at, updated_at)
                           VALUES (@Name, @Instrument, @MarketType, @Enabled, @ExecutionInterval, @LastExecutedAt, @Status, @Tags::jsonb, now(), now())
                           RETURNING id""",
                            pipeline,
                            cancellationToken = token
                        )
                    )

                let! pipeline =
                    db.QuerySingleAsync<Pipeline>(
                        CommandDefinition(
                            "SELECT * FROM pipelines WHERE id = @Id",
                            {| Id = result |},
                            cancellationToken = token
                        )
                    )

                return Ok pipeline
            with ex ->
                return Error(Unexpected ex)
        }

    let update (db: IDbConnection) (pipeline: Pipeline) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            """UPDATE pipelines
                           SET name = @Name, instrument = @Instrument, market_type = @MarketType,
                               enabled = @Enabled, execution_interval = @ExecutionInterval,
                               last_executed_at = @LastExecutedAt, status = @Status, tags = @Tags::jsonb,
                               updated_at = now()
                           WHERE id = @Id""",
                            pipeline,
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then
                    let! pipeline =
                        db.QuerySingleAsync<Pipeline>(
                            CommandDefinition(
                                "SELECT * FROM pipelines WHERE id = @Id",
                                {| Id = pipeline.Id |},
                                cancellationToken = token
                            )
                        )

                    return Ok pipeline
                else
                    return Error(NotFound $"Pipeline with id {pipeline.Id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let delete (db: IDbConnection) (id: int) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM pipelines WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then return Ok() else return Error(NotFound $"Pipeline with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let count (db: IDbConnection) (token: CancellationToken) =
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

    let countEnabled (db: IDbConnection) (token: CancellationToken) =
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

    let getAllTags (db: IDbConnection) (token: CancellationToken) =
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

    let search (db: IDbConnection) (filters: PipelineSearchFilters) (skip: int) (take: int) (token: CancellationToken) =
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
                    db.QueryAsync<Pipeline>(CommandDefinition(dataSql, parameters, cancellationToken = token))

                let pipelines = results |> Seq.toList

                // Apply tag filter in memory (tags are stored as JSON)
                let filteredPipelines =
                    match filters.Tag with
                    | Some tag when not (String.IsNullOrEmpty tag) ->
                        pipelines |> List.filter (fun p -> p.Tags |> List.contains tag)
                    | _ -> pipelines

                return Ok { Pipelines = filteredPipelines; TotalCount = totalCount }
            with ex ->
                return Error(Unexpected ex)
        }
