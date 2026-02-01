namespace Warehouse.Core.Markets.Exchanges.Okx

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Warehouse.Core.Infrastructure

module Heartbeat =
    type private HeartbeatState = { Cts: CancellationTokenSource; Task: Task }
    type T = { Start: unit -> unit; Stop: unit -> Task; IsRunning: unit -> bool }

    let create (logger: ILogger) (client: WebSocketClient.T) : T =
        let interval = TimeSpan.FromSeconds(10.0)
        let mutable state: HeartbeatState option = None
        let stateLock = obj ()

        let heartbeatLoop (cts: CancellationTokenSource) =
            task {
                use timer = new PeriodicTimer(interval)

                try
                    while not cts.Token.IsCancellationRequested do
                        let! shouldContinue = timer.WaitForNextTickAsync(cts.Token)

                        if shouldContinue then
                            match! client.Send "ping" cts.Token with
                            | Ok() -> logger.LogDebug("Heartbeat sent")
                            | Error ex -> logger.LogWarning(ex, "Failed to send heartbeat")
                with
                | :? OperationCanceledException -> ()
                | ex -> logger.LogError(ex, "Heartbeat loop failed")

                logger.LogDebug("Heartbeat stopped")
            }

        let start () =
            lock
                stateLock
                (fun () ->
                    match state with
                    | Some _ -> logger.LogDebug("Heartbeat already running")
                    | None ->
                        let cts = new CancellationTokenSource()
                        let task = Task.Run(fun () -> heartbeatLoop cts, cts.Token)
                        state <- Some { Cts = cts; Task = task }
                        logger.LogDebug("Heartbeat started")
                )

        let stop () =
            task {
                let currentState =
                    lock
                        stateLock
                        (fun () ->
                            let s = state
                            state <- None
                            s
                        )

                match currentState with
                | None -> ()
                | Some s ->
                    s.Cts.Cancel()

                    try
                        let! _ = Task.WhenAny(s.Task, Task.Delay(TimeSpan.FromSeconds(5.0)))
                        ()
                    with _ ->
                        ()

                    s.Cts.Dispose()
            }
            :> Task

        let isRunning () = lock stateLock (fun () -> state.IsSome)

        { Start = start; Stop = stop; IsRunning = isRunning }
