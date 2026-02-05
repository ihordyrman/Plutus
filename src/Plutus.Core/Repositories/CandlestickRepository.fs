namespace Plutus.Core.Repositories

open System
open System.Data
open System.Threading
open Dapper
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

[<RequireQualifiedAccess>]
module CandlestickRepository =

    let getById (db: IDbConnection) (id: int64) (token: CancellationToken) =
        task {
            try
                let! candlesticks =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            "SELECT * FROM candlesticks WHERE id = @Id LIMIT 1",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                match candlesticks |> Seq.tryHead with
                | Some candle -> return Ok candle
                | None -> return Error(NotFound $"Candlestick with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let getLatest
        (db: IDbConnection)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (token: CancellationToken)
        =
        task {
            try
                let! results =
                    db.QueryAsync<Candlestick>(
                        CommandDefinition(
                            """SELECT * FROM candlesticks
                           WHERE symbol = @Symbol AND market_type = @MarketType AND timeframe = @Timeframe
                           ORDER BY timestamp DESC
                           LIMIT 1""",
                            {| Symbol = symbol; MarketType = int marketType; Timeframe = timeframe |},
                            cancellationToken = token
                        )
                    )

                match results |> Seq.tryHead with
                | Some entity -> return Ok(Some entity)
                | None -> return Ok None
            with ex ->
                return Error(Unexpected ex)
        }

    let query
        (db: IDbConnection)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (fromDate: DateTime option)
        (toDate: DateTime option)
        (limit: int option)
        (token: CancellationToken)
        =
        task {
            try
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

                let! results =
                    db.QueryAsync<Candlestick>(CommandDefinition(finalSql, parameters, cancellationToken = token))

                return Ok(results |> Seq.toList)
            with ex ->
                return Error(Unexpected ex)
        }

    let save (db: IDbConnection) (candlesticks: Candlestick list) (token: CancellationToken) =
        task {
            if candlesticks.IsEmpty then
                return Ok 0
            else
                try
                    let! result =
                        db.ExecuteAsync(
                            CommandDefinition(
                                """INSERT INTO candlesticks
                               (symbol, market_type, timeframe, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                               VALUES (@Symbol, @MarketType, @Timeframe, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                               ON CONFLICT (symbol, market_type, timeframe, timestamp)
                               DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                             volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted""",
                                candlesticks,
                                cancellationToken = token
                            )
                        )

                    return Ok result
                with ex ->
                    return Error(Unexpected ex)
        }

    let saveOne (db: IDbConnection) (candlestick: Candlestick) (token: CancellationToken) =
        task {
            try
                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            """INSERT INTO candlesticks
                           (symbol, market_type, timeframe, timestamp, open, high, low, close, volume, volume_quote, is_completed)
                           VALUES (@Symbol, @MarketType, @Timeframe, @Timestamp, @Open, @High, @Low, @Close, @Volume, @VolumeQuote, @IsCompleted)
                           ON CONFLICT (symbol, market_type, timeframe, timestamp)
                           DO UPDATE SET open = @Open, high = @High, low = @Low, close = @Close,
                                         volume = @Volume, volume_quote = @VolumeQuote, is_completed = @IsCompleted
                           RETURNING id""",
                            candlestick,
                            cancellationToken = token
                        )
                    )

                return Ok { candlestick with Id = result }
            with ex ->
                return Error(Unexpected ex)
        }

    let delete (db: IDbConnection) (id: int64) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlesticks WHERE id = @Id",
                            {| Id = id |},
                            cancellationToken = token
                        )
                    )

                if rowsAffected > 0 then return Ok() else return Error(NotFound $"Candlestick with id {id}")
            with ex ->
                return Error(Unexpected ex)
        }

    let deleteBySymbol (db: IDbConnection) (symbol: string) (marketType: MarketType) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlesticks WHERE symbol = @Symbol AND market_type = @MarketType",
                            {| Symbol = symbol; MarketType = int marketType |},
                            cancellationToken = token
                        )
                    )

                return Ok rowsAffected
            with ex ->
                return Error(Unexpected ex)
        }

    let deleteOlderThan (db: IDbConnection) (before: DateTime) (token: CancellationToken) =
        task {
            try
                let! rowsAffected =
                    db.ExecuteAsync(
                        CommandDefinition(
                            "DELETE FROM candlesticks WHERE timestamp < @Before",
                            {| Before = before |},
                            cancellationToken = token
                        )
                    )

                return Ok rowsAffected
            with ex ->
                return Error(Unexpected ex)
        }

    let count
        (db: IDbConnection)
        (symbol: string)
        (marketType: MarketType)
        (timeframe: string)
        (token: CancellationToken)
        =
        task {
            try
                let! result =
                    db.QuerySingleAsync<int>(
                        CommandDefinition(
                            "SELECT COUNT(*) FROM candlesticks WHERE symbol = @Symbol AND market_type = @MarketType AND timeframe = @Timeframe",
                            {| Symbol = symbol; MarketType = int marketType; Timeframe = timeframe |},
                            cancellationToken = token
                        )
                    )

                return Ok result
            with ex ->
                return Error(Unexpected ex)
        }
