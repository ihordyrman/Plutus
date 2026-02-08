namespace Plutus.App.Pages.Instruments

open System.Data
open Falco
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Repositories

module View =
    let currencyOptions (currencies: string list) =
        _select [] [
            _option [ _value_ "" ] [ Text.raw "-- Select --" ]
            for currency in currencies do
                _option [ _value_ currency ] [ Text.raw currency ]
        ]

    let currencyOptionsPreselected (currencies: string list) (selected: string) =
        _select [] [
            _option [ _value_ "" ] [ Text.raw "-- Select --" ]
            for currency in currencies do
                if currency = selected then
                    _option [ _value_ currency; Attr.create "selected" "selected" ] [ Text.raw currency ]
                else
                    _option [ _value_ currency ] [ Text.raw currency ]
        ]

module Handler =
    let baseCurrencies: HttpHandler =
        fun ctx ->
            task {
                let query = Request.getQuery ctx
                let marketType = query.TryGetInt "marketType" |> Option.defaultValue 0
                use scope = ctx.Plug<IServiceScopeFactory>().CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! result = InstrumentRepository.getBaseCurrencies db marketType "SPOT" ctx.RequestAborted

                let currencies =
                    match result with
                    | Ok c -> c
                    | Error _ -> []

                return! Response.ofHtml (View.currencyOptions currencies) ctx
            }

    let quoteCurrencies: HttpHandler =
        fun ctx ->
            task {
                let query = Request.getQuery ctx
                let marketType = query.TryGetInt "marketType" |> Option.defaultValue 0
                let baseCurrency = query.TryGetString "baseCurrency" |> Option.defaultValue ""

                if System.String.IsNullOrEmpty baseCurrency then
                    return! Response.ofHtml (View.currencyOptions []) ctx
                else
                    use scope = ctx.Plug<IServiceScopeFactory>().CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    let! result =
                        InstrumentRepository.getQuoteCurrencies db marketType "SPOT" baseCurrency ctx.RequestAborted

                    let currencies =
                        match result with
                        | Ok c -> c
                        | Error _ -> []

                    return! Response.ofHtml (View.currencyOptions currencies) ctx
            }
