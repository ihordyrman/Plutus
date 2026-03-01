// For more information see https://aka.ms/fsharp-console-apps

open System.Data
open System.IO
open System.Threading
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Plutus.Core
open Plutus.Core.Domain
open Plutus.Core.Repositories
open Plutus.Core.Shared

type Pipeline = { Id: int; Name: string; Instrument: string }

let builder =
    ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddUserSecrets("90b1b531-55ba-4a18-a28e-d7ed7e5f201d")
        .AddJsonFile("appsettings.json", true)

let serviceCollection = ServiceCollection()
let configuration = builder.Build()
serviceCollection.AddSingleton<IConfiguration>(configuration) |> ignore
CoreServices.registerSlim serviceCollection configuration
let serviceProvider = serviceCollection.BuildServiceProvider()
let db = serviceProvider.GetRequiredService<IDbConnection>()

let gaps =
    CandlestickRepository.findGaps
        db
        { Base = "BTC"; Quote = "USDT" }
        MarketType.Okx
        Interval.OneMinute
        CancellationToken.None

let res = Async.AwaitTask gaps |> Async.RunSynchronously

printfn $"%A{res}"
