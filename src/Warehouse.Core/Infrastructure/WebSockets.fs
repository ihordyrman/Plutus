namespace Warehouse.Core.Infrastructure

open System
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open System.Buffers
open System.Text
open System.Text.Json
open Microsoft.Extensions.Logging

type WebSocketMessage =
    | Text of string
    | Binary of byte[]

type WebSocketEvent =
    | Connected
    | Disconnected of reason: string
    | MessageReceived of WebSocketMessage
    | SocketError of exn

type WebSocketCommand =
    | Connect of uri: Uri * AsyncReplyChannel<Result<unit, exn>>
    | Disconnect of reason: string * AsyncReplyChannel<uint>
    | Send of byte[] * WebSocketMessageType * AsyncReplyChannel<Result<unit, exn>>
    | GetState of AsyncReplyChannel<WebSocketState>

module MessageProcessing =
    let assembleMessage (chunks: (byte[] * int) list) (finalChunk: byte[] * int) : byte[] =
        let allChunks = chunks @ [ finalChunk ]
        let totalLength = allChunks |> List.sumBy snd
        let result = Array.zeroCreate<byte> totalLength

        let mutable position = 0

        for (data, length) in allChunks do
            Array.Copy(data, 0, result, position, length)
            position <- position + length

        result

    let toWebSocketMessage (messageType: WebSocketMessageType) (data: byte[]) : WebSocketMessage option =
        match messageType with
        | WebSocketMessageType.Text -> Some(Text(Encoding.UTF8.GetString(data)))
        | WebSocketMessageType.Binary -> Some(Binary data)
        | _ -> None

module WebSocketClient =

    type State = { Socket: ClientWebSocket; ListenCts: CancellationTokenSource option; IsDisposed: bool }

    type T =
        { Connect: Uri -> CancellationToken -> Task<Result<unit, exn>>
          Disconnect: string -> CancellationToken -> Task<unit>
          Send: string -> CancellationToken -> Task<Result<unit, exn>>
          SendBytes: byte[] -> CancellationToken -> Task<Result<unit, exn>>
          SendJson: obj -> CancellationToken -> Task<Result<unit, exn>>
          GetState: unit -> WebSocketState
          Events: IObservable<WebSocketEvent>
          Dispose: unit -> unit }

    let create (logger: ILogger) : T =

        let eventSubject = Event<WebSocketEvent>()
        let arrayPool = ArrayPool<byte>.Shared
        let sendLock = new SemaphoreSlim(1, 1)

        let mutable socket = new ClientWebSocket()
        let mutable listenCts: CancellationTokenSource option = None
        let mutable disposed = false

        let raiseEvent evt = eventSubject.Trigger evt

        let receiveLoop (ws: ClientWebSocket) (ct: CancellationToken) =
            task {
                let buffer = arrayPool.Rent(4096)
                let chunks = ResizeArray<byte[] * int>()

                try
                    while ws.State = WebSocketState.Open && not ct.IsCancellationRequested do
                        try
                            let! result = ws.ReceiveAsync(ArraySegment<byte>(buffer), ct)

                            match result.MessageType with
                            | WebSocketMessageType.Close ->
                                raiseEvent (Disconnected "Server closed connection")
                                return ()

                            | _ when not result.EndOfMessage ->
                                let chunk = arrayPool.Rent(result.Count)
                                Array.Copy(buffer, 0, chunk, 0, result.Count)
                                chunks.Add((chunk, result.Count))

                            | msgType ->
                                let messageData =
                                    if chunks.Count > 0 then
                                        let finalChunk = Array.zeroCreate result.Count
                                        Array.Copy(buffer, 0, finalChunk, 0, result.Count)

                                        let assembled =
                                            MessageProcessing.assembleMessage
                                                (chunks |> Seq.toList)
                                                (finalChunk, result.Count)

                                        for arr, _ in chunks do
                                            arrayPool.Return(arr)

                                        chunks.Clear()
                                        assembled
                                    else
                                        let data = Array.zeroCreate result.Count
                                        Array.Copy(buffer, 0, data, 0, result.Count)
                                        data

                                match MessageProcessing.toWebSocketMessage msgType messageData with
                                | Some msg -> raiseEvent (MessageReceived msg)
                                | None -> ()

                        with
                        | :? OperationCanceledException -> ()
                        | ex ->
                            logger.LogError(ex, "Error in receive loop")
                            raiseEvent (SocketError ex)
                finally
                    arrayPool.Return(buffer)

                    for arr, _ in chunks do
                        arrayPool.Return(arr)
            }

        let connectAsync (uri: Uri) (ct: CancellationToken) : Task<Result<unit, exn>> =
            task {
                try
                    if socket.State = WebSocketState.Open then
                        logger.LogInformation("Already connected to {Uri}", uri)
                        return Error(InvalidOperationException("WebSocket is already connected") :> exn)
                    else
                        if socket.State <> WebSocketState.None then
                            socket.Dispose()
                            socket <- new ClientWebSocket()

                        do! socket.ConnectAsync(uri, ct)
                        logger.LogInformation("Connected to {Uri}", uri)
                        raiseEvent Connected

                        let cts = new CancellationTokenSource()
                        listenCts <- Some cts
                        Task.Run(fun () -> receiveLoop socket cts.Token, cts.Token) |> ignore

                        return Ok()
                with ex ->
                    logger.LogError(ex, "Failed to connect to {Uri}", uri)
                    raiseEvent (SocketError ex)
                    return Error ex
            }

        let disconnectAsync (reason: string) (ct: CancellationToken) =
            task {
                try
                    listenCts
                    |> Option.iter (fun cts ->
                        cts.Cancel()
                        cts.Dispose()
                    )

                    listenCts <- None

                    if socket.State = WebSocketState.Open then
                        do! socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct)
                        logger.LogInformation("Disconnected: {Reason}", reason)

                    raiseEvent (Disconnected reason)
                with ex ->
                    logger.LogWarning(ex, "Error during disconnect")
            }

        let sendBytesAsync (data: byte[]) (msgType: WebSocketMessageType) (ct: CancellationToken) =
            task {
                if socket.State <> WebSocketState.Open then
                    return Error(InvalidOperationException($"Cannot send. State: {socket.State}") :> exn)
                else
                    try
                        do! sendLock.WaitAsync(ct)

                        try
                            do! socket.SendAsync(ArraySegment<byte>(data), msgType, true, ct)
                            logger.LogDebug("Sent {Size} bytes", data.Length)
                            return Ok()
                        finally
                            sendLock.Release() |> ignore
                    with ex ->
                        logger.LogError(ex, "Send failed")
                        return Error ex
            }

        let dispose () =
            if not disposed then
                disposed <- true

                listenCts
                |> Option.iter (fun cts ->
                    cts.Cancel()
                    cts.Dispose()
                )

                socket.Dispose()
                sendLock.Dispose()

        { Connect = connectAsync
          Disconnect = disconnectAsync
          Send =
            fun text ct ->
                let bytes = Encoding.UTF8.GetBytes(text)
                sendBytesAsync bytes WebSocketMessageType.Text ct
          SendBytes = fun bytes -> sendBytesAsync bytes WebSocketMessageType.Binary
          SendJson =
            fun obj ct ->
                let json = JsonSerializer.Serialize(obj)
                let bytes = Encoding.UTF8.GetBytes(json)
                sendBytesAsync bytes WebSocketMessageType.Text ct
          GetState = fun () -> socket.State
          Events = eventSubject.Publish
          Dispose = dispose }
