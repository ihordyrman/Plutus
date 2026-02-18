namespace Plutus.Core.Pipelines.Trading

open Plutus.Core.Pipelines.Core.Ports
open Plutus.Core.Pipelines.Trading

module TradingSteps =
    open CheckPosition
    open PositionGateStep
    open EntryStep
    open ExponentialMovingAverageSignal
    open MacdSignal
    open VwapSignal
    open EwmacSignal
    open TrendFollowingSignal

    let private signalSteps = [ ema; macd; vwap; ewmac; trendFollowing ]

    let all (getPosition: GetPosition) (executor: TradeExecutor) =
        [ checkPosition getPosition; positionGate getPosition; entry executor ] @ signalSteps
