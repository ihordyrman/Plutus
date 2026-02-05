open System.IO
open DbUp
open Microsoft.Extensions.Configuration
open Npgsql
open Plutus.Tools.Seeding

let builder =
    ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddUserSecrets("90b1b531-55ba-4a18-a28e-d7ed7e5f201d")
        .AddJsonFile("appsettings.json", true)

let configuration = builder.Build()
let connectionString = configuration.GetSection("Database:ConnectionString").Value

let engine =
    DeployChanges.To.PostgresqlDatabase(connectionString).WithScriptsFromFileSystem("./sql").LogToConsole().Build()

engine.PerformUpgrade() |> ignore

let db = new NpgsqlConnection(connectionString) :> System.Data.IDbConnection
ensureCredentialsPopulated configuration db |> Async.AwaitTask |> Async.RunSynchronously
