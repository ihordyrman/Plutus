namespace Plutus.Core.MarketData.Infrastructure

open System
open System.Collections.Generic
open System.Text.Json
open Dapper
open Plutus.Core.MarketData.Domain

module TypeHandlers =
    (*type private InstrumentTypeHandler() =*)
    (*    inherit SqlMapper.TypeHandler<Instrument>()*)
    (**)
    (*    override _.SetValue(parameter, value) = parameter.Value <- string value*)
    (**)
    (*    override _.Parse(value) =*)
    (*        match value with*)
    (*        | :? string as str when not (String.IsNullOrEmpty str) ->*)
    (*            match Instrument.create str with    *)
    (*            | Ok instrumentType -> instrumentType*)
    (*            | Error e -> failwith $"Invalid Instrument type value: {e}"*)
    (*        | _ -> failwith $"Invalid value for Instrument type: {value}"*)

    type private IntervalTypeHandler() =
        inherit SqlMapper.TypeHandler<Interval>()

        override _.SetValue(parameter, value) = parameter.Value <- value.ToString()

        override _.Parse value =
            match value with
            | :? string as str when not (String.IsNullOrEmpty str) ->
                match Interval.create str with
                | Ok interval -> interval
                | Error e -> failwith $"Invalid Interval value: {e}"
            | _ -> failwith $"Invalid value for Interval type: {value}"

    let registerTypeHandlers () =
        DefaultTypeMap.MatchNamesWithUnderscores <- true

        do
            SqlMapper.AddTypeHandler(IntervalTypeHandler())
