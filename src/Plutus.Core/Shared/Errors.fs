namespace Plutus.Core.Shared

open Plutus.Core.Domain

module Errors =
    type ExternalError =
        | HttpError of message: string * statusCode: int option
        | Timeout of operation: string
        | Unexpected of exn

    let externalMessage =
        function
        | HttpError(msg, _) -> msg
        | Timeout operation -> $"{operation} timed out"
        | Unexpected ex -> ex.Message

    type ServiceError =
        | ApiError of message: string * statusCode: int option
        | NotFound of entity: string
        | NoProvider of MarketType
        | Unexpected of exn

    let serviceMessage =
        function
        | ApiError(msg, _) -> msg
        | NotFound entity -> $"{entity} not found"
        | NoProvider market -> $"No provider registered for {market}"
        | Unexpected ex -> ex.Message
