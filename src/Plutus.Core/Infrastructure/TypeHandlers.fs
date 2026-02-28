namespace Plutus.Core.Infrastructure

open System
open System.Collections.Generic
open System.Text.Json
open Dapper
open Plutus.Core.Shared

module TypeHandlers =

    type private InstrumentTypeHandler() =
        inherit SqlMapper.TypeHandler<Instrument>()

        override _.SetValue(parameter, value) = parameter.Value <- string value

        override _.Parse(value) =
            match value with
            | :? string as str when not (String.IsNullOrEmpty str) -> Instrument.parse str
            | _ -> failwith $"Invalid value for Instrument type: {value}"

    type private StringListTypeHandler() =
        inherit SqlMapper.TypeHandler<string list>()

        override _.SetValue(parameter, value) = parameter.Value <- JsonSerializer.Serialize value

        override _.Parse(value) =
            match value with
            | :? string as json when not (String.IsNullOrEmpty json) -> JsonSerializer.Deserialize<string list> json
            | _ -> failwith "Invalid value for string list type"

    type private DictionaryStringStringTypeHandler() =
        inherit SqlMapper.TypeHandler<Dictionary<string, string>>()

        override _.SetValue(parameter, value) = parameter.Value <- JsonSerializer.Serialize value

        override _.Parse(value) =
            match value with
            | :? string as json when not (String.IsNullOrEmpty json) ->
                JsonSerializer.Deserialize<Dictionary<string, string>> json
            | _ -> failwith "Invalid value for dictionary<string, string> type"

    type OptionHandler<'T>() =
        inherit SqlMapper.TypeHandler<option<'T>>()

        override _.SetValue(param, value) =
            param.Value <-
                match value with
                | Some x -> box x
                | None -> null

        override _.Parse value =
            match isNull value || value = box DBNull.Value with
            | true -> None
            | false -> Some(value :?> 'T)

    let registerTypeHandlers () =
        DefaultTypeMap.MatchNamesWithUnderscores <- true

        do
            SqlMapper.AddTypeHandler(typeof<option<string>>, OptionHandler<string>())
            SqlMapper.AddTypeHandler(typeof<option<decimal>>, OptionHandler<decimal>())
            SqlMapper.AddTypeHandler(typeof<option<DateTime>>, OptionHandler<DateTime>())
            SqlMapper.AddTypeHandler(InstrumentTypeHandler())
            SqlMapper.AddTypeHandler(StringListTypeHandler())
            SqlMapper.AddTypeHandler(DictionaryStringStringTypeHandler())
