namespace Plutus.Shared


// todo: questionable. Review if I need such separation of errors.
module Errors =

    type ServiceError =
        | ApiError of message: string * statusCode: int option
        | NotFound of entity: string
        | NoProvider of marketType: obj
        | Unexpected of exn

    let serviceMessage =
        function
        | ApiError(msg, _) -> msg
        | NotFound entity -> $"{entity} not found"
        | NoProvider market -> $"No provider registered for {market}"
        | Unexpected ex -> ex.Message
