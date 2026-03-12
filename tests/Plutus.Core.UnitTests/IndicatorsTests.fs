module Plutus.Core.UnitTests.IndicatorsTests

open Xunit
open Plutus.Core.Pipelines.Trading

[<Fact>]
let ``sma - returns None for empty series`` () = Assert.Equal(None, Indicators.sma 3 [])

[<Fact>]
let ``sma - returns None when series is shorter than period`` () = Assert.Equal(None, Indicators.sma 5 [ 1m; 2m; 3m ])

[<Fact>]
let ``sma - computes average of last period elements`` () =
    // last 3 of [10; 20; 30; 40] = [20; 30; 40], avg = 30
    Assert.Equal(Some 30m, Indicators.sma 3 [ 10m; 20m; 30m; 40m ])

[<Fact>]
let ``emaSeries - returns empty list when series is shorter than period`` () =
    Assert.Equal<decimal list>([], Indicators.emaSeries 3 [ 1m; 2m ])

[<Fact>]
let ``emaSeries - first value equals SMA of seed period`` () =
    // seed = (1+2+3)/3 = 2
    let result = Indicators.emaSeries 3 [ 1m; 2m; 3m; 4m; 5m ]
    Assert.Equal(2m, List.head result)

[<Fact>]
let ``emaSeries - applies exponential smoothing formula`` () =
    // k = 2/(3+1) = 0.5, seed = (1+2+3)/3 = 2
    // price=4: 4*0.5 + 2*0.5 = 3; price=5: 5*0.5 + 3*0.5 = 4
    Assert.Equal<decimal list>([ 2m; 3m; 4m ], Indicators.emaSeries 3 [ 1m; 2m; 3m; 4m; 5m ])

[<Fact>]
let ``ema - returns None when insufficient data`` () = Assert.Equal(None, Indicators.ema 5 [ 1m; 2m; 3m ])

[<Fact>]
let ``ema - returns last value of emaSeries`` () =
    // emaSeries 3 [1..5] = [2; 3; 4], last = 4
    Assert.Equal(Some 4m, Indicators.ema 3 [ 1m; 2m; 3m; 4m; 5m ])

[<Fact>]
let ``vwap - returns None for empty data`` () = Assert.Equal(None, Indicators.vwap [])

[<Fact>]
let ``vwap - returns None when total volume is zero`` () = Assert.Equal(None, Indicators.vwap [ (100m, 0m) ])

[<Fact>]
let ``vwap - computes volume-weighted average correctly`` () =
    // (100*2 + 200*3) / (2+3) = 800/5 = 160
    Assert.Equal(Some 160m, Indicators.vwap [ (100m, 2m); (200m, 3m) ])

[<Fact>]
let ``returns - returns empty list for empty series`` () = Assert.Equal<decimal list>([], Indicators.returns [])

[<Fact>]
let ``returns - returns empty list for single-element series`` () =
    Assert.Equal<decimal list>([], Indicators.returns [ 100m ])

[<Fact>]
let ``returns - computes percentage changes between consecutive values`` () =
    // [10; 20; 30]: (20-10)/10 = 1.0; (30-20)/20 = 0.5
    Assert.Equal<decimal list>([ 1m; 0.5m ], Indicators.returns [ 10m; 20m; 30m ])

[<Fact>]
let ``momentum - returns None when series is too short`` () = Assert.Equal(None, Indicators.momentum 3 [ 10m; 20m ])

[<Fact>]
let ``momentum - computes percentage change over lookback period`` () =
    // [10; 20; 30], lookback=2: current=30, past=series[0]=10, (30-10)/10*100 = 200
    Assert.Equal(Some 200m, Indicators.momentum 2 [ 10m; 20m; 30m ])
