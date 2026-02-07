namespace Plutus.Core.Infrastructure

open System
open System.Data
open Microsoft.Extensions.Logging
open Dapper

module ExecutionLogger =

    type T = { Post: ExecutionLog -> unit; Stop: unit -> unit }

    let create (getConnection: unit -> IDbConnection) (logger: ILogger) : T =
        let mailbox =
            MailboxProcessor.Start(fun inbox ->
                let rec loop () =
                    async {
                        let! log = inbox.Receive()

                        try
                            use db = getConnection ()

                            let outcomeValue =
                                match log.Outcome with
                                | StepOutcome.Success -> 0
                                | StepOutcome.Stopped -> 1
                                | StepOutcome.Failed -> 2

                            let! _ =
                                db.ExecuteAsync(
                                    CommandDefinition(
                                        """INSERT INTO execution_logs
                                           (pipeline_id, execution_id, step_type_key, outcome, message, context, start_time, end_time)
                                           VALUES (@PipelineId, @ExecutionId, @StepTypeKey, @Outcome, @Message, @ContextSnapshot::jsonb, @StartTime, @EndTime)""",
                                        {| PipelineId = log.PipelineId
                                           ExecutionId = log.ExecutionId
                                           StepTypeKey = log.StepTypeKey
                                           Outcome = outcomeValue
                                           Message = log.Message
                                           ContextSnapshot = log.ContextSnapshot
                                           StartTime = log.StartTime
                                           EndTime = log.EndTime |}
                                    )
                                )
                                |> Async.AwaitTask

                            ()
                        with ex ->
                            logger.LogError(
                                ex,
                                "Failed to insert execution log for pipeline {PipelineId}, execution {ExecutionId}, step {StepKey}",
                                log.PipelineId,
                                log.ExecutionId,
                                log.StepTypeKey
                            )

                        return! loop ()
                    }

                loop ()
            )

        { Post = mailbox.Post; Stop = fun () -> (mailbox :> IDisposable).Dispose() }
