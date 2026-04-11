namespace Plutus.Core.Infrastructure

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

module CacheStore =

    type T() =
        let store = ConcurrentDictionary<string, obj>()

        member _.Get<'a>(key: string) : 'a option =
            match store.TryGetValue key with
            | true, (:? 'a as typed) -> Some typed
            | _ -> None

        member _.Set(key: string, value: 'a) = store[key] <- box value


type CacheRefresher =
    { Key: string
      Interval: TimeSpan
      Refresh: CacheStore.T -> IServiceScopeFactory -> ILogger -> CancellationToken -> Task<unit> }
