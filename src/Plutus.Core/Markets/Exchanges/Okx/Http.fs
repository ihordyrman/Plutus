namespace Plutus.Core.Markets.Exchanges.Okx

open System
open System.Collections.Generic
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open System.Web
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Markets.Stores
open Plutus.Core.Markets.Stores.CredentialsStore
open Plutus.Core.Shared

module Http =
    open Errors

    type HttpMethod =
        | Get
        | Post

        override this.ToString() =
            match this with
            | Get -> "GET"
            | Post -> "POST"

    type Request = { Endpoint: string; Method: HttpMethod; Parameters: Map<string, string>; Body: obj option }

    type CandlestickParams = { Bar: string option; After: string option; Before: string option; Limit: int option }

    type T =
        { getBalance: string option -> Task<Result<OkxBalanceDetail[], ServiceError>>
          getFundingBalance: string option -> Task<Result<OkxFundingBalance[], ServiceError>>
          getAccountBalance: string option -> Task<Result<OkxAccountBalance[], ServiceError>>
          getAssetsValuation: string option -> Task<Result<OkxAssetsValuation[], ServiceError>>
          getCandlesticks: string -> CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>
          getHistoryCandlesticks: string -> CandlestickParams -> Task<Result<OkxCandlestick[], ServiceError>>
          placeOrder: OkxPlaceOrderRequest -> Task<Result<OkxPlaceOrderResponse[], ServiceError>>
          getOrder: string -> string -> Task<Result<OkxOrderDetail[], ServiceError>>
          getInstruments: InstrumentType -> Task<Result<OkxInstrument[], ServiceError>> }

    let get endpoint = { Endpoint = endpoint; Method = Get; Parameters = Map.empty; Body = None }
    let post endpoint body = { Endpoint = endpoint; Method = Post; Parameters = Map.empty; Body = Some body }
    let withParam key value (req: Request) = { req with Parameters = Map.add key value req.Parameters }
    let withParamOpt key valueOpt req = valueOpt |> Option.map (fun x -> withParam key x req) |> Option.defaultValue req

    let private buildPath req =
        if Map.isEmpty req.Parameters then
            req.Endpoint
        else
            let query = HttpUtility.ParseQueryString String.Empty
            req.Parameters |> Map.iter (fun k v -> query[k] <- v)
            $"{req.Endpoint}?{query}"

    let private serializeBody (opts: JsonSerializerOptions) =
        function
        | None -> ""
        | Some b -> JsonSerializer.Serialize(b, opts)

    let private addAuthHeaders
        (client: HttpClient)
        (credentials: Credentials)
        (timestamp: string)
        (method: string)
        (path: string)
        (body: string)
        =
        let signature = Auth.generateSignature timestamp credentials.Secret method path body
        client.DefaultRequestHeaders.Add("OK-ACCESS-SIGN", signature)
        client.DefaultRequestHeaders.Add("OK-ACCESS-TIMESTAMP", timestamp)
        client.DefaultRequestHeaders.Add("OK-ACCESS-KEY", credentials.Key)
        client.DefaultRequestHeaders.Add("OK-ACCESS-PASSPHRASE", credentials.Passphrase |> Option.defaultValue "")
        client.DefaultRequestHeaders.Add("x-simulated-trading", if credentials.IsSandbox then "1" else "0")

    let private clearAuthHeaders (client: HttpClient) =
        [ "OK-ACCESS-SIGN"
          "OK-ACCESS-TIMESTAMP"
          "OK-ACCESS-KEY"
          "OK-ACCESS-PASSPHRASE"
          "x-simulated-trading" ]
        |> List.iter (client.DefaultRequestHeaders.Remove >> ignore)

    let private execute<'T>
        (client: HttpClient)
        (jsonOpts: JsonSerializerOptions)
        (credentials: Credentials)
        (req: Request)
        : Task<Result<'T, ServiceError>>
        =
        task {
            let path = buildPath req
            let method = req.Method.ToString()
            let body = serializeBody jsonOpts req.Body
            let timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

            addAuthHeaders client credentials timestamp method path body

            try
                let! response =
                    match req.Method with
                    | Get -> client.GetAsync(path)
                    | Post -> client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"))

                if not response.IsSuccessStatusCode then
                    let! err = response.Content.ReadAsStringAsync()
                    return Error(ApiError($"HTTP {int response.StatusCode}: {err}", Some(int response.StatusCode)))
                else
                    let! json = response.Content.ReadAsStringAsync()
                    let okxResp = JsonSerializer.Deserialize<OkxHttpResponse<'T>>(json, jsonOpts)

                    match okxResp.Data with
                    | Some data -> return Ok data
                    | None -> return Error(ApiError($"No data: {okxResp.Message}", None))
            finally
                clearAuthHeaders client
        }

    type private RateLimitCounter =
        { Limit: int; Window: TimeSpan; Semaphore: SemaphoreSlim; TimeStamps: Queue<DateTime> }

    let private rateLimits =
        let defaultWindow = TimeSpan.FromSeconds(1.0)

        let create limit =
            { Limit = limit
              Window = defaultWindow
              Semaphore = new SemaphoreSlim(1, 1)
              TimeStamps = Queue<DateTime>() }

        Map<string, RateLimitCounter>(
            [ ("/api/v5/public/instruments", create 20)
              ("/api/v5/market/candles", create 40)
              ("/api/v5/market/history-candles", { create 20 with Window = TimeSpan.FromSeconds(2.0) })
              ("/api/v5/asset/asset-valuation", { create 1 with Window = TimeSpan.FromSeconds(2.0) })
              ("/api/v5/account/balance", create 10)
              ("/api/v5/asset/balances", create 10)
              ("/api/v5/trade/order", create 60) ]
        )

    let private checkRateLimit (req: Request) (logger: ILogger) =
        match rateLimits.TryFind req.Endpoint with
        | Some counter ->
            task {
                do! counter.Semaphore.WaitAsync()

                try
                    let now = DateTime.UtcNow

                    while counter.TimeStamps.Count > 0 && now - counter.TimeStamps.Peek() > counter.Window do
                        counter.TimeStamps.Dequeue() |> ignore

                    if counter.TimeStamps.Count >= counter.Limit then
                        let waitTime = counter.Window - (now - counter.TimeStamps.Peek())

                        logger.LogDebug(
                            "Rate limit hit for {Endpoint}. Waiting for {WaitTime} ms before retrying.",
                            req.Endpoint,
                            waitTime.TotalMilliseconds
                        )

                        do! Task.Delay(waitTime)

                        let now = DateTime.UtcNow

                        while counter.TimeStamps.Count > 0 && now - counter.TimeStamps.Peek() > counter.Window do
                            counter.TimeStamps.Dequeue() |> ignore

                    counter.TimeStamps.Enqueue(DateTime.UtcNow)
                finally
                    counter.Semaphore.Release() |> ignore
            }
        | None -> Task.FromResult<unit>(())


    let private exec<'T>
        (httpClient: HttpClient)
        (jsonOpts: JsonSerializerOptions)
        (credentials: Credentials)
        (logger: ILogger)
        (req: Request)
        : Task<Result<'T, ServiceError>>
        =
        task {
            try
                do! checkRateLimit req logger
                return! execute<'T> httpClient jsonOpts credentials req
            with ex ->
                logger.LogError(ex, "Request failed: {Endpoint}", req.Endpoint)
                return Error(Unexpected ex)
        }

    let create (httpClient: HttpClient) (credentialsStore: CredentialsStore.T) (logger: ILogger) : T =

        let jsonOpts = JsonSerializerOptions()
        jsonOpts.Converters.Add(OkxCandlestickConverter())

        let credentials =
            match
                credentialsStore.GetCredentials MarketType.Okx CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously
            with
            | Ok credentials -> credentials
            | Error err ->
                logger.LogError("Failed to retrieve OKX credentials: {Error}", err)
                failwith "Cannot create OkxHttp without valid credentials"

        let run req = exec httpClient jsonOpts credentials logger req

        { getBalance = fun ccy -> get "/api/v5/asset/balances" |> withParamOpt "ccy" ccy |> run
          getFundingBalance = fun ccy -> get "/api/v5/asset/balances" |> withParamOpt "ccy" ccy |> run
          getAccountBalance = fun ccy -> get "/api/v5/account/balance" |> withParamOpt "ccy" ccy |> run
          getAssetsValuation =
            fun ccy -> get "/api/v5/asset/asset-valuation" |> withParam "ccy" (defaultArg ccy "USDT") |> run

          getCandlesticks =
            fun instId p ->
                get "/api/v5/market/candles"
                |> withParam "instId" instId
                |> withParamOpt "bar" p.Bar
                |> withParamOpt "after" p.After
                |> withParamOpt "before" p.Before
                |> withParamOpt "limit" (p.Limit |> Option.map string)
                |> run

          getHistoryCandlesticks =
            fun instId p ->
                get "/api/v5/market/history-candles"
                |> withParam "instId" instId
                |> withParamOpt "bar" p.Bar
                |> withParamOpt "after" p.After
                |> withParamOpt "before" p.Before
                |> withParamOpt "limit" (p.Limit |> Option.map string)
                |> run

          placeOrder = fun order -> post "/api/v5/trade/order" order |> run
          getOrder =
            fun instId ordId -> get "/api/v5/trade/order" |> withParam "instId" instId |> withParam "ordId" ordId |> run
          getInstruments =
            fun instType ->
                match instType with
                | InstrumentType.Spot -> get "/api/v5/public/instruments" |> withParam "instType" "SPOT" |> run
                | InstrumentType.Futures -> get "/api/v5/public/instruments" |> withParam "instType" "FUTURES" |> run
                | InstrumentType.Swap -> get "/api/v5/public/instruments" |> withParam "instType" "SWAP" |> run
                | InstrumentType.Option -> get "/api/v5/public/instruments" |> withParam "instType" "OPTION" |> run
                | _ -> failwith "Unsupported instrument type" }
