namespace Plutus.Shared

module Errors =
    type ServiceError =
        | Unathorized of message: string
        | ApiError of message: string * statusCode: int option
        | Validation of message: string
        | NotFound of entity: string
        | NoProvider of marketType: obj
        | Unexpected of exn
