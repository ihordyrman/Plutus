namespace Plutus.Tools

open System.Data
open System.Threading.Tasks
open Dapper

module Seeding =
    let ensureMarketsPopulated (connection: IDbConnection) : Task<unit> =
        task {
            let! marketCount = connection.QuerySingleAsync<int>("SELECT count(*) FROM markets")

            if marketCount > 0 then
                return ()
            else
                let! _ =
                    connection.QuerySingleAsync<int>(
                        "INSERT INTO markets (type, created_at, updated_at)
                         VALUES (@Type, now(), now()) RETURNING id",
                        {| Type = 0 |}
                    )

                return ()
        }
