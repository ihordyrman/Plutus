namespace Plutus.Core.Markets.Exchanges.Okx

open System.Text.Json
open Plutus.Core.Domain
open Plutus.Core.Markets.Abstractions

module MessageParser =

    type ParsedMessage =
        | Pong
        | SubscriptionConfirmed of channel: string * symbol: string
        | UnsubscriptionConfirmed of channel: string * symbol: string
        | LoginSuccess of connectionId: string option
        | LoginFailed of code: string * message: string
        | MarketData of MarketDataEvent
        | OkxError of code: string * message: string
        | Unknown of raw: string

    let private serializerOptions =
        let opts = JsonSerializerOptions()
        opts.PropertyNameCaseInsensitive <- true
        opts

    let private tryParseMarketData (response: OkxSocketResponse) : ParsedMessage option =
        match response.Arguments, response.Data with
        | Some args, Some dataArray when dataArray.Length > 0 ->
            let symbol = args.InstrumentId |> Option.defaultValue ""
            let data = dataArray[0]

            match data.Asks, data.Bids with
            | Some asks, Some bids ->
                Some(MarketData { Symbol = symbol; Source = MarketType.Okx; Asks = asks; Bids = bids })
            | _ -> None
        | _ -> None

    let parse (text: string) : ParsedMessage =
        if text = "pong" then
            Pong
        else
            try
                let response = JsonSerializer.Deserialize<OkxSocketResponse>(text, serializerOptions)

                if isNull (box response) then
                    Unknown text
                else
                    match response.Event with
                    | Some OkxEvent.Subscribe ->
                        let channel = response.Arguments |> Option.bind _.Channel |> Option.defaultValue ""
                        let symbol = response.Arguments |> Option.bind _.InstrumentId |> Option.defaultValue ""
                        SubscriptionConfirmed(channel, symbol)

                    | Some OkxEvent.Unsubscribe ->
                        let channel = response.Arguments |> Option.bind _.Channel |> Option.defaultValue ""
                        let symbol = response.Arguments |> Option.bind _.InstrumentId |> Option.defaultValue ""
                        UnsubscriptionConfirmed(channel, symbol)

                    | Some OkxEvent.Login ->
                        match response.Code with
                        | Some code when code = OkxResponseCode.Success -> LoginSuccess None
                        | Some code -> LoginFailed(string code, response.Message |> Option.defaultValue "")
                        | None -> LoginSuccess None

                    | Some OkxEvent.Error ->
                        let code = response.Code |> Option.map string |> Option.defaultValue "unknown"
                        let msg = response.Message |> Option.defaultValue ""
                        OkxError(code, msg)

                    | None ->
                        match tryParseMarketData response with
                        | Some marketData -> marketData
                        | None -> Unknown text

                    | _ -> Unknown text
            with _ ->
                Unknown text

    let createSubscribeRequest (channel: string) (symbol: string) =
        {| op = "subscribe"; args = [| {| channel = channel; instId = symbol |} |] |}

    let createUnsubscribeRequest (channel: string) (symbol: string) =
        {| op = "unsubscribe"; args = [| {| channel = channel; instId = symbol |} |] |}
