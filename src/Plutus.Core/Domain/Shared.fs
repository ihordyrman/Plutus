namespace Plutus.Core.Domain

type PositiveDecimal = private PositiveDecimal of decimal

module PositiveDecimal =
    let create (value: decimal) : Result<PositiveDecimal, string> =
        match value with
        | x when x <= 0m -> Error "Value must be a positive decimal."
        | _ -> Ok(PositiveDecimal value)

    let value (PositiveDecimal value) = value
