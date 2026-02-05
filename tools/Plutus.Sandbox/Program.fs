// For more information see https://aka.ms/fsharp-console-apps

open System
open System.IO
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Plutus.Core
open Plutus.Core.Markets.Exchanges.Okx

type Pipeline = { Id: int; Name: string; Symbol: string }

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
let http = serviceProvider.GetRequiredService<Http.T>()

// 1440 last entries for 1 timeframe
// if 1 min frame -> 1 day data
let after = DateTimeOffset.UtcNow.AddDays(-1.0).ToUnixTimeMilliseconds().ToString()

let candlestics =
    http.getCandlesticks "BTC-USDT" { Bar = Some "1H"; After = Some after; Before = None; Limit = Some 300 }
    |> Async.AwaitTask
    |> Async.RunSynchronously

printfn $"%A{candlestics}"
