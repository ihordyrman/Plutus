namespace Warehouse.Core.Markets.Stores

open System
open System.Collections.Concurrent
open System.Collections.Frozen
open System.Globalization
open Warehouse.Core.Domain
open Warehouse.Core.Markets.Abstractions

module LiveDataStore =

    [<Struct>]
    type private DataKey = { Symbol: string; MarketType: MarketType }

    type private DataSnapshot =
        { Asks: ConcurrentDictionary<decimal, struct (decimal * int)>
          Bids: ConcurrentDictionary<decimal, struct (decimal * int)> }

        static member Create() =
            { Asks = ConcurrentDictionary<decimal, struct (decimal * int)>()
              Bids = ConcurrentDictionary<decimal, struct (decimal * int)>() }

        static member ToMarketData(snapshot: DataSnapshot) : MarketData =
            { Asks = snapshot.Asks.ToFrozenDictionary(); Bids = snapshot.Bids.ToFrozenDictionary() }

    type T =
        { Get: string -> MarketType -> MarketData option
          Update: MarketDataEvent -> unit
          Clean: MarketType -> unit }

    let create () : T =
        let cache = ConcurrentDictionary<DataKey, DataSnapshot>()

        let tryParseLevel (data: string[]) =
            if data.Length < 4 then
                ValueNone
            else
                match
                    Decimal.TryParse(data[0], CultureInfo.InvariantCulture),
                    Decimal.TryParse(data[1], CultureInfo.InvariantCulture),
                    Int32.TryParse(data[3], CultureInfo.InvariantCulture)
                with
                | (true, price), (true, size), (true, orderCount) -> ValueSome struct (price, size, orderCount)
                | _ -> ValueNone

        let updateSide (side: ConcurrentDictionary<decimal, struct (decimal * int)>) (levels: string[][]) =
            for level in levels do
                match tryParseLevel level with
                | ValueSome struct (price, size, orderCount) ->
                    if size = 0m then side.TryRemove(price) |> ignore else side[price] <- struct (size, orderCount)
                | ValueNone -> ()

        { Get =
            fun symbol marketType ->
                let key = { Symbol = symbol; MarketType = marketType }

                match cache.TryGetValue(key) with
                | true, snapshot -> Some(DataSnapshot.ToMarketData snapshot)
                | false, _ -> None

          Update =
            fun marketDataEvent ->
                let key = { Symbol = marketDataEvent.Symbol; MarketType = marketDataEvent.Source }
                let snapshot = cache.GetOrAdd(key, fun _ -> DataSnapshot.Create())
                updateSide snapshot.Asks marketDataEvent.Asks
                updateSide snapshot.Bids marketDataEvent.Bids

          Clean =
            fun marketType ->
                cache.Keys
                |> Seq.filter (fun x -> x.MarketType = marketType)
                |> Seq.iter (fun x -> cache.TryRemove x |> ignore) }
