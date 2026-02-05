namespace Plutus.Core.Markets.Exchanges.Okx

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Plutus.Core.Infrastructure
open Plutus.Core.Domain
open Plutus.Core.Markets.Stores

module OkxAdapter =

    type private State =
        { ConnectionState: ConnectionState
          Subscriptions: Set<string>
          Uri: Uri option
          ReconnectAttempts: int
          LastHeartbeat: DateTime option }

        static member Initial =
            { ConnectionState = ConnectionState.Disconnected
              Subscriptions = Set.empty
              Uri = None
              ReconnectAttempts = 0
              LastHeartbeat = None }

    type private Message =
        | Connect of market: Market option * AsyncReplyChannel<Result<unit, string>>
        | Disconnect of AsyncReplyChannel<unit>
        | Subscribe of channel: string * symbol: string * AsyncReplyChannel<Result<unit, string>>
        | Unsubscribe of channel: string * symbol: string * AsyncReplyChannel<Result<unit, string>>
        | GetState of AsyncReplyChannel<State>
        | GetSubscriptions of AsyncReplyChannel<Set<string>>

        | WebSocketEvent of WebSocketEvent
        | HeartbeatTick
        | ReconnectTick

    type T =
        { Connect: Market option -> CancellationToken -> Task<Result<unit, string>>
          Disconnect: CancellationToken -> Task<unit>
          Subscribe: string -> string -> CancellationToken -> Task<Result<unit, string>>
          Unsubscribe: string -> string -> CancellationToken -> Task<Result<unit, string>>
          GetConnectionState: unit -> ConnectionState
          GetSubscriptions: unit -> Set<string>
          Dispose: unit -> unit }

    let private heartbeatInterval = TimeSpan.FromSeconds 15.0
    let private reconnectDelay = TimeSpan.FromSeconds 5.0
    let private maxReconnectAttempts = 10

    let private getWsUri (market: Market option) =
        match market with
        | Some credentials when credentials.IsSandbox -> Uri SocketEndpoints.DEMO_PUBLIC_WS_URL
        | _ -> Uri(SocketEndpoints.PUBLIC_WS_URL)

    let private subscriptionKey channel symbol = $"{channel}:{symbol}"

    let create (webSocket: WebSocketClient.T) (liveDataStore: LiveDataStore.T) (logger: ILogger) : T =

        let mutable currentState = State.Initial
        let mutable heartbeatTimer: Timer option = None
        let mutable reconnectTimer: Timer option = None
        let mutable eventSubscription: IDisposable option = None

        let agent =
            MailboxProcessor<Message>.Start(fun inbox ->

                let sendJson obj = webSocket.SendJson obj CancellationToken.None |> Async.AwaitTask

                let startHeartbeat () =
                    heartbeatTimer |> Option.iter _.Dispose()

                    heartbeatTimer <-
                        Some(
                            new Timer((fun _ -> inbox.Post HeartbeatTick), null, heartbeatInterval, heartbeatInterval)
                        )

                let stopHeartbeat () =
                    heartbeatTimer |> Option.iter _.Dispose()
                    heartbeatTimer <- None

                let startReconnect () =
                    reconnectTimer |> Option.iter _.Dispose()

                    reconnectTimer <-
                        Some(
                            new Timer(
                                (fun _ -> inbox.Post ReconnectTick),
                                null,
                                reconnectDelay,
                                Timeout.InfiniteTimeSpan
                            )
                        )

                let stopReconnect () =
                    reconnectTimer |> Option.iter _.Dispose()
                    reconnectTimer <- None

                let resubscribeAll (subscriptions: Set<string>) =
                    async {
                        for subscription in subscriptions do
                            let parts = subscription.Split(':')

                            if parts.Length = 2 then
                                let channel, symbol = parts[0], parts[1]
                                let request = MessageParser.createSubscribeRequest channel symbol

                                match! sendJson request with
                                | Ok() -> logger.LogDebug("Resubscribed to {Key}", subscription)
                                | Error ex -> logger.LogWarning(ex, "Failed to resubscribe to {Key}", subscription)
                    }

                let processWebSocketMessage text (state: State) =
                    match MessageParser.parse text with
                    | MessageParser.Pong ->
                        logger.LogTrace("Heartbeat pong received")
                        { state with LastHeartbeat = Some DateTime.UtcNow }

                    | MessageParser.SubscriptionConfirmed(channel, symbol) ->
                        logger.LogInformation("Subscription confirmed: {Channel}:{Symbol}", channel, symbol)
                        state

                    | MessageParser.UnsubscriptionConfirmed(channel, symbol) ->
                        logger.LogInformation("Unsubscription confirmed: {Channel}:{Symbol}", channel, symbol)
                        state

                    | MessageParser.LoginSuccess connectionId ->
                        logger.LogInformation("Login successful. ConnectionId: {ConnectionId}", connectionId)
                        state

                    | MessageParser.LoginFailed(code, message) ->
                        logger.LogError("Login failed: {Code} - {Message}", code, message)
                        state

                    | MessageParser.MarketData data ->
                        liveDataStore.Update(data)
                        state

                    | MessageParser.OkxError(code, message) ->
                        logger.LogError("OKX error: {Code} - {Message}", code, message)
                        state

                    | MessageParser.Unknown raw ->
                        logger.LogTrace("Unknown message: {Raw}", raw)
                        state

                let rec loop (state: State) =
                    async {
                        let! msg = inbox.Receive()
                        currentState <- state

                        match msg with
                        | Connect(credentials, reply) ->
                            if state.ConnectionState = ConnectionState.Connected then
                                logger.LogDebug("Already connected")
                                reply.Reply(Ok())
                                return! loop state
                            else
                                let uri = getWsUri credentials
                                logger.LogInformation("Connecting to {Uri}...", uri)

                                let newState =
                                    { state with ConnectionState = ConnectionState.Connecting; Uri = Some uri }

                                match! webSocket.Connect uri CancellationToken.None |> Async.AwaitTask with
                                | Ok() ->
                                    logger.LogInformation("Connected to OKX")
                                    startHeartbeat ()
                                    stopReconnect ()

                                    match credentials with
                                    | Some credentials when not (isNull credentials.ApiKey) ->
                                        let authRequest = Auth.createAuthRequest credentials
                                        let! _ = sendJson authRequest
                                        ()
                                    | _ -> ()

                                    reply.Reply(Ok())

                                    return!
                                        loop
                                            { newState with
                                                ConnectionState = ConnectionState.Connected
                                                ReconnectAttempts = 0 }

                                | Error ex ->
                                    logger.LogError(ex, "Failed to connect")
                                    reply.Reply(Error ex.Message)

                                    return! loop { newState with ConnectionState = ConnectionState.Failed }

                        | Disconnect reply ->
                            logger.LogInformation("Disconnecting...")
                            stopHeartbeat ()
                            stopReconnect ()

                            do!
                                webSocket.Disconnect "User requested disconnect" CancellationToken.None
                                |> Async.AwaitTask

                            reply.Reply()

                            return!
                                loop
                                    { state with
                                        ConnectionState = ConnectionState.Disconnected
                                        Subscriptions = Set.empty }

                        | Subscribe(channel, symbol, reply) ->
                            let key = subscriptionKey channel symbol

                            if state.ConnectionState <> ConnectionState.Connected then
                                reply.Reply(Error $"Cannot subscribe in state: {state.ConnectionState}")
                                return! loop state
                            elif state.Subscriptions.Contains key then
                                logger.LogDebug("Already subscribed to {Key}", key)
                                reply.Reply(Ok())
                                return! loop state
                            else
                                let request = MessageParser.createSubscribeRequest channel symbol

                                match! sendJson request with
                                | Ok() ->
                                    logger.LogInformation("Subscribing to {Channel}:{Symbol}", channel, symbol)
                                    reply.Reply(Ok())

                                    return! loop { state with Subscriptions = Set.add key state.Subscriptions }

                                | Error ex ->
                                    logger.LogError(ex, "Failed to subscribe to {Key}", key)
                                    reply.Reply(Error ex.Message)
                                    return! loop state

                        | Unsubscribe(channel, symbol, reply) ->
                            let key = subscriptionKey channel symbol

                            if not (state.Subscriptions.Contains key) then
                                logger.LogDebug("Not subscribed to {Key}", key)
                                reply.Reply(Ok())
                                return! loop state
                            else
                                let request = MessageParser.createUnsubscribeRequest channel symbol

                                match! sendJson request with
                                | Ok() ->
                                    logger.LogInformation("Unsubscribing from {Channel}:{Symbol}", channel, symbol)
                                    reply.Reply(Ok())

                                    return! loop { state with Subscriptions = Set.remove key state.Subscriptions }

                                | Error ex ->
                                    logger.LogError(ex, "Failed to unsubscribe from {Key}", key)
                                    reply.Reply(Error ex.Message)
                                    return! loop state

                        | GetState reply ->
                            reply.Reply(state)
                            return! loop state

                        | GetSubscriptions reply ->
                            reply.Reply(state.Subscriptions)
                            return! loop state

                        | WebSocketEvent evt ->
                            match evt with
                            | WebSocketEvent.MessageReceived(WebSocketMessage.Text text) ->
                                let newState = processWebSocketMessage text state
                                return! loop newState

                            | WebSocketEvent.MessageReceived(WebSocketMessage.Binary _) ->
                                // binary messages not expected from OKX, ignore
                                return! loop state

                            | WebSocketEvent.Disconnected reason ->
                                logger.LogWarning("WebSocket disconnected: {Reason}", reason)
                                stopHeartbeat ()

                                if
                                    not state.Subscriptions.IsEmpty && state.ReconnectAttempts < maxReconnectAttempts
                                then
                                    startReconnect ()

                                return! loop { state with ConnectionState = ConnectionState.Disconnected }

                            | WebSocketEvent.SocketError ex ->
                                logger.LogError(ex, "WebSocket error")
                                return! loop state

                            | WebSocketEvent.Connected -> return! loop state

                        | HeartbeatTick ->
                            if state.ConnectionState = ConnectionState.Connected then
                                match! webSocket.Send "ping" CancellationToken.None |> Async.AwaitTask with
                                | Ok() -> logger.LogTrace("Heartbeat ping sent")
                                | Error ex -> logger.LogWarning(ex, "Failed to send heartbeat")

                            return! loop state

                        | ReconnectTick ->
                            if state.ReconnectAttempts >= maxReconnectAttempts then
                                logger.LogError("Max reconnect attempts reached")
                                stopReconnect ()

                                return! loop { state with ConnectionState = ConnectionState.Failed }
                            else
                                logger.LogInformation(
                                    "Reconnect attempt {Attempt}/{Max}",
                                    state.ReconnectAttempts + 1,
                                    maxReconnectAttempts
                                )

                                match state.Uri with
                                | Some uri ->
                                    match! webSocket.Connect uri CancellationToken.None |> Async.AwaitTask with
                                    | Ok() ->
                                        logger.LogInformation("Reconnected successfully")
                                        startHeartbeat ()
                                        stopReconnect ()

                                        do! resubscribeAll state.Subscriptions

                                        return!
                                            loop
                                                { state with
                                                    ConnectionState = ConnectionState.Connected
                                                    ReconnectAttempts = 0 }

                                    | Error ex ->
                                        logger.LogWarning(ex, "Reconnect attempt failed")
                                        startReconnect ()
                                        return! loop { state with ReconnectAttempts = state.ReconnectAttempts + 1 }

                                | None ->
                                    logger.LogError("No URI for reconnection")
                                    stopReconnect ()
                                    return! loop { state with ConnectionState = ConnectionState.Failed }
                    }

                loop State.Initial
            )

        eventSubscription <- Some(webSocket.Events |> Observable.subscribe (WebSocketEvent >> agent.Post))

        { Connect =
            fun credentials _ -> agent.PostAndAsyncReply(fun reply -> Connect(credentials, reply)) |> Async.StartAsTask
          Disconnect = fun _ -> agent.PostAndAsyncReply Disconnect |> Async.StartAsTask
          Subscribe =
            fun channel symbol _ ->
                agent.PostAndAsyncReply(fun reply -> Subscribe(channel, symbol, reply)) |> Async.StartAsTask
          Unsubscribe =
            fun channel symbol _ ->
                agent.PostAndAsyncReply(fun reply -> Unsubscribe(channel, symbol, reply)) |> Async.StartAsTask
          GetConnectionState = fun () -> currentState.ConnectionState
          GetSubscriptions = fun () -> currentState.Subscriptions
          Dispose =
            fun () ->
                heartbeatTimer |> Option.iter _.Dispose()
                reconnectTimer |> Option.iter _.Dispose()
                eventSubscription |> Option.iter _.Dispose()
                webSocket.Dispose() }

    let subscribeBooks (adapter: T) (symbol: string) (ct: CancellationToken) = adapter.Subscribe "books" symbol ct
    let unsubscribeBooks (adapter: T) (symbol: string) (ct: CancellationToken) = adapter.Unsubscribe "books" symbol ct
