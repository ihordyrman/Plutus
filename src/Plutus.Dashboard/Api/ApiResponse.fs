namespace Plutus.Dashboard.Api

open System.Text.Json
open Falco
open Microsoft.AspNetCore.Http

module ApiResponse =

    let internal jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private writeJson (statusCode: int) (body: obj) : HttpHandler =
        fun ctx ->
            task {
                ctx.Response.StatusCode <- statusCode
                ctx.Response.ContentType <- "application/json; charset=utf-8"
                let json = JsonSerializer.Serialize(body, jsonOptions)
                do! ctx.Response.WriteAsync(json)
            }

    let ok (data: obj) : HttpHandler = writeJson 200 {| Ok = true; Data = data |}

    let okList (data: obj) (total: int) : HttpHandler = writeJson 200 {| Ok = true; Data = data; Total = total |}

    let created (data: obj) : HttpHandler = writeJson 201 {| Ok = true; Data = data |}

    let notFound (msg: string) : HttpHandler =
        writeJson 404 {| Ok = false; Error = {| Code = "NOT_FOUND"; Message = msg |} |}

    let validationFailed (msg: string) (details: obj) : HttpHandler =
        writeJson 422 {| Ok = false; Error = {| Code = "VALIDATION_ERROR"; Message = msg; Details = details |} |}

    let conflict (code: string) (msg: string) : HttpHandler =
        writeJson 409 {| Ok = false; Error = {| Code = code; Message = msg |} |}

    let unauthorized (msg: string) : HttpHandler =
        writeJson 401 {| Ok = false; Error = {| Code = "UNAUTHORIZED"; Message = msg |} |}

    let internalError (msg: string) : HttpHandler =
        writeJson 500 {| Ok = false; Error = {| Code = "INTERNAL_ERROR"; Message = msg |} |}

    let readBody<'T> (ctx: HttpContext) =
        task {
            try
                let! body = JsonSerializer.DeserializeAsync<'T>(ctx.Request.Body, jsonOptions)
                return Ok body
            with ex ->
                return Error ex.Message
        }
