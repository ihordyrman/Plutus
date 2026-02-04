namespace Warehouse.Core.Pipelines.Trading

open Warehouse.Core.Pipelines.Trading

module TradingSteps =
    open CheckPosition
    open PositionGateStep
    open EntryStep
    open ExponentialMovingAverageSignal
    open MacdSignal
    open VwapSignal
    open EwmacSignal

    let private executionSteps = [ entry ]
    let private validationSteps = [ checkPosition; positionGate ]
    let private signalSteps = [ ema; macd; vwap; ewmac ]

    let all = executionSteps @ validationSteps @ signalSteps
