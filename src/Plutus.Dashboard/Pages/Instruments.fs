namespace Plutus.App.Pages.Instruments

open System.Data
open Falco
open Falco.Markup
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Repositories

module View =
    let private renderOptions (items: (string * string * bool) list) =
        let sb = System.Text.StringBuilder()

        for (value, text, selected) in items do
            let enc = System.Web.HttpUtility.HtmlEncode

            if selected then
                sb.Append($"<option value=\"{enc value}\" selected>{enc text}</option>") |> ignore
            else
                sb.Append($"<option value=\"{enc value}\">{enc text}</option>") |> ignore

        Text.raw (sb.ToString())

    let currencyOptions (currencies: string list) =
        let items = ("", "-- Select --", false) :: (currencies |> List.map (fun c -> (c, c, false)))

        renderOptions items

    let currencyOptionsPreselected (currencies: string list) (selected: string) =
        let items = ("", "-- Select --", false) :: (currencies |> List.map (fun c -> (c, c, c = selected)))

        renderOptions items

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
