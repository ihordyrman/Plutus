namespace Warehouse.Core.Repositories

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Dapper
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Warehouse.Core.Domain
open Warehouse.Core.Shared

module CandlestickRepository =
    open Errors

    type T =
        { GetById: int64 -> CancellationToken -> Task<Result<Candlestick, ServiceError>>
          GetLatest:
              string -> MarketType -> string -> CancellationToken -> Task<Result<Candlestick option, ServiceError>>
          Query:
              string
                  -> MarketType
                  -> string
                  -> DateTime option
                  -> DateTime option
                  -> int option
                  -> CancellationToken
                  -> Task<Result<Candlestick list, ServiceError>>
          Save: Candlestick list -> CancellationToken -> Task<Result<int, ServiceError>>
          SaveOne: Candlestick -> CancellationToken -> Task<Result<Candlestick, ServiceError>>
          Delete: int64 -> CancellationToken -> Task<Result<unit, ServiceError>>
          DeleteBySymbol: string -> MarketType -> CancellationToken -> Task<Result<int, ServiceError>>
          DeleteOlderThan: DateTime -> CancellationToken -> Task<Result<int, ServiceError>>
          Count: string -> MarketType -> string -> CancellationToken -> Task<Result<int, ServiceError>> }

    let private getById
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (id: int64)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! canclestics =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            "SELECT * FROM candlesticks WHERE id = @Id LIMIT 1",
                            {| Id = id |},
                            cancellationToken = cancellation
                        )
                    )

                match canclestics |> Seq.tryHead with
                | Some candle ->
                    logger.LogDebug("Retrieved candlestick {Id}", id)
                    return Ok(candle)
                | None ->
                    logger.LogWarning("Candlestick {Id} not found", id)
                    return Error(NotFound $"Candlestick with id {id}")
            with ex ->
                logger.LogError(ex, "Failed to get candlestick {Id}", id)
                return Error(Unexpected ex)
        }

    let private getLatest
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! results =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            """SELECT * FROM candlesticks
                           WHERE symbol = @Symbol AND market_type = @MarketType AND timeframe = @Timeframe
                           ORDER BY timestamp DESC
                           LIMIT 1""",
                            {| Symbol = symbol; MarketType = int marketType; Timeframe = timeframe |},
                            cancellationToken = cancellation
                        )
                    )

                match results |> Seq.tryHead with
                | Some entity ->
                    logger.LogDebug("Retrieved latest candlestick for {Symbol}/{Timeframe}", symbol, timeframe)
                    return Ok(Some(entity))
                | None ->
                    logger.LogDebug("No candlesticks found for {Symbol}/{Timeframe}", symbol, timeframe)
                    return Ok None
            with ex ->
                logger.LogError(ex, "Failed to get latest candlestick for {Symbol}/{Timeframe}", symbol, timeframe)
                return Error(Unexpected ex)
        }

    let private query
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (fromDate: DateTime option)
        (toDate: DateTime option)
        (limit: int option)
        (_: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let baseSql =
                    "SELECT * FROM candlesticks WHERE symbol = @Symbol AND market_type = @MarketType AND timeframe = @Timeframe"

                let conditions = ResizeArray<string>()
                let parameters = DynamicParameters()
                parameters.Add("Symbol", symbol)
                parameters.Add("MarketType", int marketType)
                parameters.Add("Timeframe", timeframe)

                match fromDate with
                | Some from ->
                    conditions.Add("AND timestamp >= @FromDate")
                    parameters.Add("FromDate", from)
                | None -> ()

                match toDate with
                | Some to' ->
                    conditions.Add("AND timestamp <= @ToDate")
                    parameters.Add("ToDate", to')
                | None -> ()

                let limitClause =
                    match limit with
                    | Some l -> $"LIMIT {l}"
                    | None -> "LIMIT 1000"

                let whereClause = String.Join(" ", conditions)
                let finalSql = $"{baseSql} {whereClause} ORDER BY timestamp DESC {limitClause}"

                let! results = db.QueryAsync<Candlestick>(finalSql, parameters)
                let candlesticks = results |> Seq.toList

                logger.LogDebug(
                    "Retrieved {Count} candlesticks for {Symbol}/{Timeframe}",
                    candlesticks.Length,
                    symbol,
                    timeframe
                )

                return Ok candlesticks
            with ex ->
                logger.LogError(ex, "Failed to query candlesticks for {Symbol}/{Timeframe}", symbol, timeframe)
                return Error(Unexpected ex)
        }

    let private save
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (candlesticks: Candlestick list)
        (_: CancellationToken)
        =
        task {
            if candlesticks.IsEmpty then
                return Ok 0
            else
                try
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    let! result =
                        db.ExecuteAsync(
                            """INSERT INTO candlesticks
                               (symbol, market_type, timeframe, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                               VALUES (@Symbol, @MarketType, @Timeframe, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                               ON CONFLICT (symbol, market_type, timeframe, timestamp)
                               DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                             volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted""",
                            candlesticks
                        )

                    logger.LogDebug("Saved {Count} candlesticks", result)
                    return Ok result
                with ex ->
                    logger.LogError(ex, "Failed to save {Count} candlesticks", candlesticks.Length)
                    return Error(Unexpected ex)
        }

    let private saveOne
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (candlestick: Candlestick)
        (_: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleAsync<int>(
                        """INSERT INTO candlesticks
                           (symbol, market_type, timeframe, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                           VALUES (@Symbol, @MarketType, @Timeframe, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                           ON CONFLICT (symbol, market_type, timeframe, timestamp)
                           DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                         volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted
                           RETURNING id""",
                        candlestick
                    )

                return Ok { candlestick with Id = result }
            with ex ->
                logger.LogError(ex, "Failed to save candlestick for {Symbol}", candlestick.Symbol)
                return Error(Unexpected ex)
        }

    let private delete
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (id: int64)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlesticks WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = cancellation
                        )
                    )

                if rowsAffected > 0 then
                    logger.LogInformation("Deleted candlestick {Id}", id)
                    return Ok()
                else
                    logger.LogWarning("Candlestick {Id} not found for deletion", id)
                    return Error(NotFound $"Candlestick with id {id}")
            with ex ->
                logger.LogError(ex, "Failed to delete candlestick {Id}", id)
                return Error(Unexpected ex)
        }

    let private deleteBySymbol
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (symbol: string)
        (marketType: MarketType)
        (_: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync(
                        "DELETE FROM candlesticks WHERE symbol = @Symbol AND market_type = @MarketType",
                        {| Symbol = symbol; MarketType = int marketType |}
                    )

                logger.LogInformation("Deleted {Count} candlesticks for {Symbol}", rowsAffected, symbol)
                return Ok rowsAffected
            with ex ->
                logger.LogError(ex, "Failed to delete candlesticks for {Symbol}", symbol)
                return Error(Unexpected ex)
        }

    let private deleteOlderThan
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (before: DateTime)
        (_: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! rowsAffected =
                    db.ExecuteAsync("DELETE FROM candlesticks WHERE timestamp < @Before", {| Before = before |})

                logger.LogInformation("Deleted {Count} candlesticks older than {Before}", rowsAffected, before)
                return Ok rowsAffected
            with ex ->
                logger.LogError(ex, "Failed to delete candlesticks older than {Before}", before)
                return Error(Unexpected ex)
        }

    let private count
        (scopeFactory: IServiceScopeFactory)
        (logger: ILogger)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (cancellation: CancellationToken)
        =
        task {
            try
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(*) FROM candlesticks WHERE symbol = @Symbol AND market_type = @MarketType AND timeframe = @Timeframe",
                            {| Symbol = symbol; MarketType = int marketType; Timeframe = timeframe |},
                            cancellationToken = cancellation
                        )
                    )

                logger.LogDebug("Count for {Symbol}/{Timeframe}: {Count}", symbol, timeframe, result)
                return Ok result
            with ex ->
                logger.LogError(ex, "Failed to count candlesticks for {Symbol}/{Timeframe}", symbol, timeframe)
                return Error(Unexpected ex)
        }

    let create (scopeFactory: IServiceScopeFactory) : T =
        let loggerFactory = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ILoggerFactory>()
        let logger = loggerFactory.CreateLogger("CandlestickRepository")

        { GetById = getById scopeFactory logger
          GetLatest = getLatest scopeFactory logger
          Query = query scopeFactory logger
          Save = save scopeFactory logger
          SaveOne = saveOne scopeFactory logger
          Delete = delete scopeFactory logger
          DeleteBySymbol = deleteBySymbol scopeFactory logger
          DeleteOlderThan = deleteOlderThan scopeFactory logger
          Count = count scopeFactory logger }
