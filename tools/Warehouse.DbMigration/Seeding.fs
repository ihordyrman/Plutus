namespace Warehouse.Tools

open System
open System.Data
open System.Threading.Tasks
open Dapper
open Microsoft.Extensions.Configuration

module Seeding =
    let ensureCredentialsPopulated (configuration: IConfiguration) (connection: IDbConnection) : Task<unit> =
        task {
            let! marketCount = connection.QuerySingleAsync<int>("SELECT count(*) FROM markets")

            if marketCount > 0 then
                return ()
            else
                let section = "OkxAuthConfiguration"
                let apiKey = configuration[$"{section}:ApiKey"]
                let passPhrase = configuration[$"{section}:Passphrase"]
                let secretKey = configuration[$"{section}:SecretKey"]

                match (apiKey, passPhrase, secretKey) with
                | null, _, _
                | _, null, _
                | _, _, null -> failwith "Missing OKX configuration"
                | _ -> ()

                let! _ =
                    task {
                        let! existingMarketId =
                            connection.QuerySingleOrDefaultAsync<Nullable<int>>(
                                "SELECT id FROM markets WHERE type = @Type",
                                {| Type = 0 |}
                            )

                        match existingMarketId with
                        | _ when existingMarketId.HasValue -> return existingMarketId.Value
                        | _ ->
                            return!
                                connection.QuerySingleAsync<int>(
                                    "INSERT INTO markets (type, api_key, passphrase, secret_key, is_sandbox, created_at, updated_at)
                                     VALUES (@Type, @ApiKey, @Passphrase, @SecretKey, @IsSandbox, now(), now()) RETURNING id",
                                    {| Type = 0
                                       ApiKey = apiKey
                                       Passphrase = passPhrase
                                       SecretKey = secretKey
                                       IsSandbox = true |}
                                )
                    }

                return ()
        }
