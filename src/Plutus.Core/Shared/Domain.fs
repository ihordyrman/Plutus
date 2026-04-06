namespace Plutus.Core.Shared.Domain

type MarketType =
    | Okx = 0
    | Binance = 1
    | IBKR = 2

type PositiveDecimal = private PositiveDecimal of decimal
type NonNegativeDecimal = private NonNegativeDecimal of decimal
type PositiveInt = private PositiveInt of int
type NonEmptyString = private NonEmptyString of string

module PositiveDecimal =
    let create (value: decimal) : Result<PositiveDecimal, string> =
        match value with
        | x when x <= 0m -> Error "Value must be a positive decimal."
        | _ -> Ok(PositiveDecimal value)

    let value (PositiveDecimal value) = value

module NonNegativeDecimal =
    let create (value: decimal) : Result<NonNegativeDecimal, string> =
        match value with
        | x when x < 0m -> Error "Value must be a non-negative decimal."
        | _ -> Ok(NonNegativeDecimal value)

    let value (NonNegativeDecimal value) = value

module PositiveInt =
    let create (value: int) : Result<PositiveInt, string> =
        match value with
        | x when x <= 0 -> Error "Value must be a positive integer."
        | _ -> Ok(PositiveInt value)

module NonEmptyString =
    let create (value: string) : Result<NonEmptyString, string> =
        match value with
        | null
        | "" -> Error "Value must be a non-empty string."
        | _ -> Ok(NonEmptyString value)

    let value (NonEmptyString value) = value

