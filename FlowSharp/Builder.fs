﻿namespace FlowSharp

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open FlowSharp.Actions

type Builder (DecisionTask:DecisionTask) =
    let response = new RespondDecisionTaskCompletedRequest(Decisions = ResizeArray<Decision>(), TaskToken = DecisionTask.TaskToken)            
    let mutable bindingId = 0
    let mutable blockFlag = false
    let mutable exceptionEvents = List.empty<int64>

    let NextBindingId() =
        bindingId <- bindingId + 1
        bindingId

    let AddExceptionEventId eventId = 
        exceptionEvents <- eventId :: exceptionEvents

    let HasExceptionBeenThrownForEvent eventId = 
        exceptionEvents
        |> List.exists ( (=) eventId )

    let FindSignalHistory (decisionTask:DecisionTask) (signalName:string) =
        let combinedHistory = new HistoryEvent()

        let findSignal =
            decisionTask.Events
            |> Seq.tryFindBack (fun hev -> hev.EventType = EventType.WorkflowExecutionSignaled && hev.WorkflowExecutionSignaledEventAttributes.SignalName = signalName)

        match findSignal with
        | Some(h) -> 
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp
            combinedHistory.WorkflowExecutionSignaledEventAttributes <- h.WorkflowExecutionSignaledEventAttributes
        | None -> ()
        
        // Return the combined history
        combinedHistory

    let FindMarkerHistory (decisionTask:DecisionTask) (markerName:string) (details:string) =
        let combinedHistory = new HistoryEvent()

        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            if hev.EventType = EventType.MarkerRecorded && hev.MarkerRecordedEventAttributes.MarkerName = markerName && hev.MarkerRecordedEventAttributes.Details = details then
                setCommonProperties(hev)
                combinedHistory.MarkerRecordedEventAttributes <- hev.MarkerRecordedEventAttributes

            elif hev.EventType = EventType.RecordMarkerFailed && hev.RecordMarkerFailedEventAttributes.MarkerName = markerName then
                setCommonProperties(hev)
                combinedHistory.RecordMarkerFailedEventAttributes <- hev.RecordMarkerFailedEventAttributes
        
        // Return the combined history
        combinedHistory

    let FindChildWorkflowExecutionHistory (decisionTask:DecisionTask) (bindingId:int) (workflowType:WorkflowType) (workflowId:string) =
        let combinedHistory = new HistoryEvent()
        let bindingIdString = bindingId.ToString()
        let mutable decisionTaskCompletedEventId = 0L
        let mutable initiatedEventId = 0L
        let mutable startedEventId = 0L

        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            // Skip these common DecisionTask events right away
            if hev.EventType = EventType.DecisionTaskScheduled || hev.EventType = EventType.DecisionTaskStarted || hev.EventType = EventType.DecisionTaskCompleted then ()

            // StartChildWorkflowExecutionInitiated
            elif hev.EventType = EventType.StartChildWorkflowExecutionInitiated &&
                                 hev.StartChildWorkflowExecutionInitiatedEventAttributes.Control = bindingIdString &&
                                 hev.StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.StartChildWorkflowExecutionInitiatedEventAttributes <- hev.StartChildWorkflowExecutionInitiatedEventAttributes
                decisionTaskCompletedEventId <- hev.StartChildWorkflowExecutionInitiatedEventAttributes.DecisionTaskCompletedEventId
                initiatedEventId <- hev.EventId

            // StartChildWorkflowExecutionFailed
            elif hev.EventType = EventType.StartChildWorkflowExecutionFailed &&
                                 hev.StartChildWorkflowExecutionFailedEventAttributes.Control = bindingIdString &&
                                 hev.StartChildWorkflowExecutionFailedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.StartChildWorkflowExecutionFailedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.StartChildWorkflowExecutionFailedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.StartChildWorkflowExecutionFailedEventAttributes.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.StartChildWorkflowExecutionFailedEventAttributes <- hev.StartChildWorkflowExecutionFailedEventAttributes

            // ChildWorkflowExecutionStarted
            elif hev.EventType = EventType.ChildWorkflowExecutionStarted &&
                                 hev.ChildWorkflowExecutionStartedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionStartedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionStartedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionStartedEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionStartedEventAttributes <- hev.ChildWorkflowExecutionStartedEventAttributes
                startedEventId <- hev.EventId

            // ChildWorkflowExecutionCompleted
            elif hev.EventType = EventType.ChildWorkflowExecutionCompleted &&
                                 hev.ChildWorkflowExecutionCompletedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionCompletedEventAttributes.StartedEventId = startedEventId &&
                                 hev.ChildWorkflowExecutionCompletedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionCompletedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionCompletedEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionCompletedEventAttributes <- hev.ChildWorkflowExecutionCompletedEventAttributes

            // ChildWorkflowExecutionFailed
            elif hev.EventType = EventType.ChildWorkflowExecutionFailed &&
                                 hev.ChildWorkflowExecutionFailedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionFailedEventAttributes.StartedEventId = startedEventId &&
                                 hev.ChildWorkflowExecutionFailedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionFailedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionFailedEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionFailedEventAttributes <- hev.ChildWorkflowExecutionFailedEventAttributes

            // ChildWorkflowExecutionTimedOut
            elif hev.EventType = EventType.ChildWorkflowExecutionTimedOut &&
                                 hev.ChildWorkflowExecutionTimedOutEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionTimedOutEventAttributes.StartedEventId = startedEventId &&
                                 hev.ChildWorkflowExecutionTimedOutEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionTimedOutEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionTimedOutEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionTimedOutEventAttributes <- hev.ChildWorkflowExecutionTimedOutEventAttributes

            // ChildWorkflowExecutionCanceled
            elif hev.EventType = EventType.ChildWorkflowExecutionCanceled &&
                                 hev.ChildWorkflowExecutionCanceledEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionCanceledEventAttributes.StartedEventId = startedEventId &&
                                 hev.ChildWorkflowExecutionCanceledEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionCanceledEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionCanceledEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionCanceledEventAttributes <- hev.ChildWorkflowExecutionCanceledEventAttributes

            // ChildWorkflowExecutionTerminated
            elif hev.EventType = EventType.ChildWorkflowExecutionTerminated &&
                                 hev.ChildWorkflowExecutionTerminatedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.ChildWorkflowExecutionTerminatedEventAttributes.StartedEventId = startedEventId &&
                                 hev.ChildWorkflowExecutionTerminatedEventAttributes.WorkflowType.Name = workflowType.Name &&
                                 hev.ChildWorkflowExecutionTerminatedEventAttributes.WorkflowType.Version = workflowType.Version &&
                                 hev.ChildWorkflowExecutionTerminatedEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ChildWorkflowExecutionTerminatedEventAttributes <- hev.ChildWorkflowExecutionTerminatedEventAttributes

        // Return the combined history
        combinedHistory

    let FindRequestCancelExternalWorkflowExecutionHistory (decisionTask:DecisionTask) (bindingId:int) (workflowId:string) =
        let combinedHistory = new HistoryEvent()
        let bindingIdString = bindingId.ToString()
        let mutable initiatedEventId = 0L

        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            if hev.EventType = EventType.RequestCancelExternalWorkflowExecutionInitiated && 
                               hev.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes.Control = bindingIdString &&
                               hev.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes <- hev.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes 
                initiatedEventId <- hev.EventId

            elif hev.EventType = EventType.RequestCancelExternalWorkflowExecutionFailed &&
                                 hev.RequestCancelExternalWorkflowExecutionFailedEventAttributes.Control = bindingIdString &&
                                 hev.RequestCancelExternalWorkflowExecutionFailedEventAttributes.InitiatedEventId = initiatedEventId &&
                                 hev.RequestCancelExternalWorkflowExecutionFailedEventAttributes.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.RequestCancelExternalWorkflowExecutionFailedEventAttributes <- hev.RequestCancelExternalWorkflowExecutionFailedEventAttributes 

            elif hev.EventType = EventType.ExternalWorkflowExecutionCancelRequested && 
                               hev.ExternalWorkflowExecutionCancelRequestedEventAttributes.InitiatedEventId = initiatedEventId &&
                               hev.ExternalWorkflowExecutionCancelRequestedEventAttributes.WorkflowExecution.WorkflowId = workflowId then
                setCommonProperties(hev)
                combinedHistory.ExternalWorkflowExecutionCancelRequestedEventAttributes <- hev.ExternalWorkflowExecutionCancelRequestedEventAttributes

        // Return the combined history
        combinedHistory

    let FindSignalExternalWorkflowExecutionHistory (decisionTask:DecisionTask) (bindingId:int) (signalName:string) (workflowId:string) =
        let combinedHistory = new HistoryEvent()
        let bindingIdString = bindingId.ToString()
        let mutable initiatedEventId = 0L

        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            if hev.EventType = EventType.SignalExternalWorkflowExecutionInitiated && 
               hev.SignalExternalWorkflowExecutionInitiatedEventAttributes.Control = bindingIdString &&
               hev.SignalExternalWorkflowExecutionInitiatedEventAttributes.SignalName = signalName && 
               hev.SignalExternalWorkflowExecutionInitiatedEventAttributes.WorkflowId = workflowId then
                
                setCommonProperties(hev)
                combinedHistory.SignalExternalWorkflowExecutionInitiatedEventAttributes <- hev.SignalExternalWorkflowExecutionInitiatedEventAttributes
                initiatedEventId <- hev.EventId

            elif hev.EventType = EventType.SignalExternalWorkflowExecutionFailed &&
                 hev.SignalExternalWorkflowExecutionFailedEventAttributes.Control = bindingIdString &&
                 hev.SignalExternalWorkflowExecutionFailedEventAttributes.InitiatedEventId = initiatedEventId &&
                 hev.SignalExternalWorkflowExecutionFailedEventAttributes.WorkflowId = workflowId then

                setCommonProperties(hev)
                combinedHistory.SignalExternalWorkflowExecutionFailedEventAttributes <- hev.SignalExternalWorkflowExecutionFailedEventAttributes

            elif hev.EventType = EventType.ExternalWorkflowExecutionSignaled &&
                 hev.ExternalWorkflowExecutionSignaledEventAttributes.InitiatedEventId = initiatedEventId && 
                 hev.ExternalWorkflowExecutionSignaledEventAttributes.WorkflowExecution.WorkflowId = workflowId then

                setCommonProperties(hev)
                combinedHistory.ExternalWorkflowExecutionSignaledEventAttributes <- hev.ExternalWorkflowExecutionSignaledEventAttributes
        
        // Return the combined history
        combinedHistory
        
    let FindReturnExceptionHistory (decisionTask:DecisionTask) =
        let combinedHistory = new HistoryEvent()

        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            if HasExceptionBeenThrownForEvent (hev.EventId) then
                // If an exception has been thrown once for this event, don't let it be flagged twice
                ()
            else
                if hev.EventType = EventType.CompleteWorkflowExecutionFailed then
                    setCommonProperties(hev)
                    combinedHistory.CompleteWorkflowExecutionFailedEventAttributes <- hev.CompleteWorkflowExecutionFailedEventAttributes

                elif hev.EventType = EventType.CancelWorkflowExecutionFailed  then
                    setCommonProperties(hev)
                    combinedHistory.CancelWorkflowExecutionFailedEventAttributes <- hev.CancelWorkflowExecutionFailedEventAttributes

                elif hev.EventType = EventType.FailWorkflowExecutionFailed then
                    setCommonProperties(hev)
                    combinedHistory.FailWorkflowExecutionFailedEventAttributes <- hev.FailWorkflowExecutionFailedEventAttributes

                elif hev.EventType = EventType.ContinueAsNewWorkflowExecutionFailed then
                    setCommonProperties(hev)
                    combinedHistory.ContinueAsNewWorkflowExecutionFailedEventAttributes <- hev.ContinueAsNewWorkflowExecutionFailedEventAttributes
                        
        // Return the combined history
        combinedHistory

    let FindActivityTaskHistory (decisionTask:DecisionTask) (bindingId:int) (activityId:string) =
        let combinedHistory = new HistoryEvent()
        let bindingIdString = bindingId.ToString()
        let mutable scheduledEventId = 0L
        let mutable startedEventId = 0L
        let mutable latestCancelRequestedEventId = 0L
            
        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            // Skip these common DecisionTask events right away
            if hev.EventType = EventType.DecisionTaskScheduled || hev.EventType = EventType.DecisionTaskStarted || hev.EventType = EventType.DecisionTaskCompleted then ()
            
            // ActivityTaskScheduled
            elif hev.EventType = EventType.ActivityTaskScheduled && hev.ActivityTaskScheduledEventAttributes.Control = bindingIdString then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskScheduledEventAttributes <- hev.ActivityTaskScheduledEventAttributes
                scheduledEventId <- hev.EventId

            // ActivityTaskStarted
            elif hev.EventType = EventType.ActivityTaskStarted && hev.ActivityTaskStartedEventAttributes.ScheduledEventId = scheduledEventId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskStartedEventAttributes <- hev.ActivityTaskStartedEventAttributes
                startedEventId <- hev.EventId

            // ActivityTaskCompleted
            elif hev.EventType = EventType.ActivityTaskCompleted && hev.ActivityTaskCompletedEventAttributes.ScheduledEventId = scheduledEventId && hev.ActivityTaskCompletedEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskCompletedEventAttributes <- hev.ActivityTaskCompletedEventAttributes

            // ActivityTaskCanceled
            elif hev.EventType = EventType.ActivityTaskCanceled && hev.ActivityTaskCanceledEventAttributes.ScheduledEventId = scheduledEventId && hev.ActivityTaskCanceledEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskCanceledEventAttributes <- hev.ActivityTaskCanceledEventAttributes

            // ActivityTaskTimedOut
            elif hev.EventType = EventType.ActivityTaskTimedOut && hev.ActivityTaskTimedOutEventAttributes.ScheduledEventId = scheduledEventId && hev.ActivityTaskTimedOutEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskTimedOutEventAttributes <- hev.ActivityTaskTimedOutEventAttributes

            // ActivityTaskFailed
            elif hev.EventType = EventType.ActivityTaskFailed && hev.ActivityTaskFailedEventAttributes.ScheduledEventId = scheduledEventId && hev.ActivityTaskFailedEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskFailedEventAttributes <- hev.ActivityTaskFailedEventAttributes

            // ActivityTaskCancelRequested
            elif hev.EventType = EventType.ActivityTaskCancelRequested && hev.ActivityTaskCancelRequestedEventAttributes.ActivityId = activityId then
                setCommonProperties(hev)
                combinedHistory.ActivityTaskCancelRequestedEventAttributes <- hev.ActivityTaskCancelRequestedEventAttributes

            // RequestCancelActivityTaskFailed
            elif hev.EventType = EventType.RequestCancelActivityTaskFailed && hev.RequestCancelActivityTaskFailedEventAttributes.ActivityId = activityId
                                                                           && hev.RequestCancelActivityTaskFailedEventAttributes.DecisionTaskCompletedEventId > decisionTask.PreviousStartedEventId then
                setCommonProperties(hev)
                combinedHistory.RequestCancelActivityTaskFailedEventAttributes <- hev.RequestCancelActivityTaskFailedEventAttributes

            // ScheduleActivityTaskFailed
            elif hev.EventType = EventType.ScheduleActivityTaskFailed && hev.ScheduleActivityTaskFailedEventAttributes.DecisionTaskCompletedEventId > decisionTask.PreviousStartedEventId then
                combinedHistory.ScheduleActivityTaskFailedEventAttributes <- hev.ScheduleActivityTaskFailedEventAttributes

        // Return the combined history
        combinedHistory
    
    let FindLambdaFunctionHistory (decisionTask:DecisionTask) (id:string) (name:string) =
        let combinedHistory = new HistoryEvent()
        let mutable scheduledEventId = 0L
        let mutable startedEventId = 0L
            
        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            // Skip these common DecisionTask events right away
            if hev.EventType = EventType.DecisionTaskScheduled || hev.EventType = EventType.DecisionTaskStarted || hev.EventType = EventType.DecisionTaskCompleted then ()
            
            // LambdaFunctionScheduled
            elif hev.EventType = EventType.LambdaFunctionScheduled && hev.LambdaFunctionScheduledEventAttributes.Id = id && hev.LambdaFunctionScheduledEventAttributes.Name = name then
                setCommonProperties(hev)
                combinedHistory.LambdaFunctionScheduledEventAttributes <- hev.LambdaFunctionScheduledEventAttributes
                scheduledEventId <- hev.EventId

            // LambdaFunctionStarted
            elif hev.EventType = EventType.LambdaFunctionStarted && hev.LambdaFunctionStartedEventAttributes.ScheduledEventId = scheduledEventId then
                setCommonProperties(hev)
                combinedHistory.LambdaFunctionStartedEventAttributes <- hev.LambdaFunctionStartedEventAttributes
                startedEventId <- hev.EventId

            // LambdaFunctionCompleted
            elif hev.EventType = EventType.LambdaFunctionCompleted && hev.LambdaFunctionCompletedEventAttributes.ScheduledEventId = scheduledEventId && hev.LambdaFunctionCompletedEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.LambdaFunctionCompletedEventAttributes <- hev.LambdaFunctionCompletedEventAttributes

            // LambdaFunctionTimedOut
            elif hev.EventType = EventType.LambdaFunctionTimedOut && hev.LambdaFunctionTimedOutEventAttributes.ScheduledEventId = scheduledEventId && hev.LambdaFunctionTimedOutEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.LambdaFunctionTimedOutEventAttributes <- hev.LambdaFunctionTimedOutEventAttributes

            // LambdaFunctionFailed
            elif hev.EventType = EventType.LambdaFunctionFailed && hev.LambdaFunctionFailedEventAttributes.ScheduledEventId = scheduledEventId && hev.LambdaFunctionFailedEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.LambdaFunctionFailedEventAttributes <- hev.LambdaFunctionFailedEventAttributes

            // Schedule Lambda Function Failed
            elif hev.EventType = EventType.ScheduleLambdaFunctionFailed && hev.ScheduleLambdaFunctionFailedEventAttributes.DecisionTaskCompletedEventId > decisionTask.PreviousStartedEventId then
                combinedHistory.ScheduleLambdaFunctionFailedEventAttributes <- hev.ScheduleLambdaFunctionFailedEventAttributes

            // Start Lambda Function Failed
            elif hev.EventType = EventType.StartLambdaFunctionFailed && hev.StartLambdaFunctionFailedEventAttributes.ScheduledEventId = scheduledEventId then
                combinedHistory.StartLambdaFunctionFailedEventAttributes <- hev.StartLambdaFunctionFailedEventAttributes

        // Return the combined history
        combinedHistory

    let FindTimerHistory (decisionTask:DecisionTask) (bindingId:int) =
        let combinedHistory = new HistoryEvent()
        let bindingIdString = bindingId.ToString()
        let mutable startedEventId = -1L
        let mutable timerId : string = null
            
        let setCommonProperties (h:HistoryEvent) =
            combinedHistory.EventType <- h.EventType
            combinedHistory.EventId <- h.EventId
            combinedHistory.EventTimestamp <- h.EventTimestamp

        for hev in decisionTask.Events do
            // Skip these common DecisionTask events right away
            if hev.EventType = EventType.DecisionTaskScheduled || hev.EventType = EventType.DecisionTaskStarted || hev.EventType = EventType.DecisionTaskCompleted then ()
            
            // CancelTimerFailed
            elif hev.EventType = EventType.CancelTimerFailed && hev.CancelTimerFailedEventAttributes.DecisionTaskCompletedEventId > decisionTask.PreviousStartedEventId then
                combinedHistory.CancelTimerFailedEventAttributes <- hev.CancelTimerFailedEventAttributes

            // StartTimerFailed
            elif hev.EventType = EventType.StartTimerFailed && hev.StartTimerFailedEventAttributes.DecisionTaskCompletedEventId > decisionTask.PreviousStartedEventId then
                combinedHistory.StartTimerFailedEventAttributes <- hev.StartTimerFailedEventAttributes

            // TimerStarted
            elif hev.EventType = EventType.TimerStarted && hev.TimerStartedEventAttributes.Control = bindingIdString then
                setCommonProperties(hev)
                combinedHistory.TimerStartedEventAttributes <- hev.TimerStartedEventAttributes
                startedEventId <- hev.EventId
                timerId <- hev.TimerStartedEventAttributes.TimerId

            // TimerFired
            elif hev.EventType = EventType.TimerFired && hev.TimerFiredEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.TimerFiredEventAttributes <- hev.TimerFiredEventAttributes

            // TimerCanceled
            elif hev.EventType = EventType.TimerCanceled && hev.TimerCanceledEventAttributes.StartedEventId = startedEventId then
                setCommonProperties(hev)
                combinedHistory.TimerCanceledEventAttributes <- hev.TimerCanceledEventAttributes

        // Return the combined history
        combinedHistory

    let (|EventOfType|_|) (etype:EventType) (hev:HistoryEvent) =
        if hev.EventType = etype then
            Some(hev)
        else
            None

    member this.Delay(f) =
        if DecisionTask.TaskToken = null then 
            // When PollForDecisionTask times out, the TaskToken is null. There's nothing to decide in this case so null is returned.
            (fun () -> null)
        else
            // There are decisions to be made, so call the decider.
            (fun () -> f())

    member this.Run(f) : RespondDecisionTaskCompletedRequest = 
        // Run is used to call the Delay function, making execution immediate which is what we want.
        f()

    member this.Zero() = new RespondDecisionTaskCompletedRequest(Decisions = ResizeArray<Decision>(), TaskToken = DecisionTask.TaskToken)

    member this.Return(result:ReturnResult) =
        blockFlag <- true

        // Look for possible return failures
        let combinedHistory = FindReturnExceptionHistory DecisionTask

        match result with
        | ReturnResult.RespondDecisionTaskCompleted ->
            ()

        | ReturnResult.CompleteWorkflowExecution(r) -> 
            match (combinedHistory) with
            | h when h.CompleteWorkflowExecutionFailedEventAttributes <> null ->
                // A previous attempt was made to complete this workflow, but it failed
                // Raise an exception that the decider can process
                blockFlag <- false
                AddExceptionEventId (h.EventId)
                raise (CompleteWorkflowExecutionFailedException(h.CompleteWorkflowExecutionFailedEventAttributes))

            | _ -> 
                let decision = new Decision();
                decision.DecisionType <- DecisionType.CompleteWorkflowExecution
                decision.CompleteWorkflowExecutionDecisionAttributes <- new CompleteWorkflowExecutionDecisionAttributes();
                decision.CompleteWorkflowExecutionDecisionAttributes.Result <- r
                response.Decisions.Add(decision)

        | ReturnResult.CancelWorkflowExecution(details) ->
            match (combinedHistory) with
            | h when h.CancelWorkflowExecutionFailedEventAttributes <> null ->
                // A previous attempt was made to cancel this workflow, but it failed
                // Raise an exception that the decider can process
                blockFlag <- false
                AddExceptionEventId (h.EventId)
                raise (CancelWorkflowExecutionFailedException(h.CancelWorkflowExecutionFailedEventAttributes))

            | _ ->
                let decision = new Decision();
                decision.DecisionType <- DecisionType.CancelWorkflowExecution
                decision.CancelWorkflowExecutionDecisionAttributes <- new CancelWorkflowExecutionDecisionAttributes();
                decision.CancelWorkflowExecutionDecisionAttributes.Details <- details
                response.Decisions.Add(decision)

        | ReturnResult.FailWorkflowExecution(reason, details) ->
            match (combinedHistory) with
            | h when h.FailWorkflowExecutionFailedEventAttributes <> null ->
                // A previous attempt was made to fail this workflow, but it failed
                // Raise an exception that the decider can process
                blockFlag <- false
                AddExceptionEventId (h.EventId)
                raise (FailWorkflowExecutionFailedException(h.FailWorkflowExecutionFailedEventAttributes))
            | _ ->
                let decision = new Decision();
                decision.DecisionType <- DecisionType.FailWorkflowExecution
                decision.FailWorkflowExecutionDecisionAttributes <- new FailWorkflowExecutionDecisionAttributes();
                decision.FailWorkflowExecutionDecisionAttributes.Reason <- reason
                decision.FailWorkflowExecutionDecisionAttributes.Details <- details
                response.Decisions.Add(decision)

        | ReturnResult.ContinueAsNewWorkflowExecution(attr) ->
            match (combinedHistory) with
            | h when h.ContinueAsNewWorkflowExecutionFailedEventAttributes <> null ->
                // A previous attempt was made to continue this workflow as new, but it failed
                // Raise an exception that the decider can process
                blockFlag <- false
                AddExceptionEventId (h.EventId)
                raise (ContinueAsNewWorkflowExecutionFailedException(h.ContinueAsNewWorkflowExecutionFailedEventAttributes))
            | _ ->
                let decision = new Decision();
                decision.DecisionType <- DecisionType.ContinueAsNewWorkflowExecution
                decision.ContinueAsNewWorkflowExecutionDecisionAttributes <- attr;
                response.Decisions.Add(decision)

        response

    member this.Return(result:string) = this.Return(ReturnResult.CompleteWorkflowExecution(result))
    member this.Return(result:unit) = this.Return(ReturnResult.RespondDecisionTaskCompleted)
            
    // Start and Wait for Activity Task
    member this.Bind(ScheduleAndWaitForActivityTaskAction.Attributes(attr), f:(WaitForActivityTaskResult -> RespondDecisionTaskCompletedRequest)) = 
        // The idea is that with the same decider, the sequence of calls to Bind will be the same. The bindingId is used in the .Control 
        // properties and is used when matching the execution history to a DeciderAction
        let bindingId = NextBindingId()

        let combinedHistory = FindActivityTaskHistory DecisionTask bindingId (attr.ActivityId)

        match (combinedHistory) with
        // Completed
        | EventOfType EventType.ActivityTaskCompleted h -> 
            f(WaitForActivityTaskResult.Completed(h.ActivityTaskCompletedEventAttributes))

        // TimedOut
        | EventOfType EventType.ActivityTaskTimedOut h -> 
            f(WaitForActivityTaskResult.TimedOut(h.ActivityTaskTimedOutEventAttributes))

        // Canceled
        | EventOfType EventType.ActivityTaskCanceled h -> 
            f(WaitForActivityTaskResult.Canceled(h.ActivityTaskCanceledEventAttributes))

        // Failed
        | EventOfType EventType.ActivityTaskFailed h -> 
            f(WaitForActivityTaskResult.Failed(h.ActivityTaskFailedEventAttributes))

        // ScheduleActivityTaskFailed
        | h when h.ScheduleActivityTaskFailedEventAttributes <> null && 
                    h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Name = attr.ActivityType.Name && 
                    h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Version = attr.ActivityType.Version ->
            f(WaitForActivityTaskResult.ScheduleFailed(h.ScheduleActivityTaskFailedEventAttributes))

        // Not Scheduled
        | h when h.ActivityTaskScheduledEventAttributes = null ->
            blockFlag <- true
            attr.Control <- bindingId.ToString()

            let d = new Decision();
            d.DecisionType <- DecisionType.ScheduleActivityTask
            d.ScheduleActivityTaskDecisionAttributes <- attr
            response.Decisions.Add(d)
            response
        | _ -> 
            // This activity is still running, continue blocking
            blockFlag <- true
            response

    // Schedule Activity Task
    member this.Bind(ScheduleActivityTaskAction.Attributes(attr), f:(ScheduleActivityTaskResult -> RespondDecisionTaskCompletedRequest)) = 
        let bindingId = NextBindingId()

        let combinedHistory = FindActivityTaskHistory DecisionTask bindingId (attr.ActivityId)

        match (combinedHistory) with
        // ScheduleActivityTaskFailed
        | h when h.ScheduleActivityTaskFailedEventAttributes <> null && 
                    h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Name = attr.ActivityType.Name && 
                    h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Version = attr.ActivityType.Version ->
            f(ScheduleActivityTaskResult.ScheduleFailed(h.ScheduleActivityTaskFailedEventAttributes))

        // Started
        | h when h.ActivityTaskStartedEventAttributes <> null && h.ActivityTaskScheduledEventAttributes <> null->
            f(ScheduleActivityTaskResult.Started(h.ActivityTaskStartedEventAttributes,  h.ActivityTaskScheduledEventAttributes.ActivityType, h.ActivityTaskScheduledEventAttributes.Control, h.ActivityTaskScheduledEventAttributes.ActivityId))

        // Scheduled
        | h when h.ActivityTaskScheduledEventAttributes <> null ->
            f(ScheduleActivityTaskResult.Scheduled(h.ActivityTaskScheduledEventAttributes))

        // Not Scheduled
        | h when h.ActivityTaskScheduledEventAttributes = null ->
            attr.Control <- bindingId.ToString()

            let d = new Decision();
            d.DecisionType <- DecisionType.ScheduleActivityTask
            d.ScheduleActivityTaskDecisionAttributes <- attr
            response.Decisions.Add(d)
                
            f(ScheduleActivityTaskResult.Scheduling(Activity=attr.ActivityType, ActivityId=attr.ActivityId))

        | _ -> failwith "error"

    // Wait For Activity Task
    member this.Bind(WaitForActivityTaskAction.ScheduleResult(result), f:(WaitForActivityTaskResult -> RespondDecisionTaskCompletedRequest)) =

        let bindWithHistory (activity:ActivityType) (control:string) (activityId:string) =
            let combinedHistory = FindActivityTaskHistory DecisionTask (Convert.ToInt32(control)) activityId

            match (combinedHistory) with
            // Completed
            | EventOfType EventType.ActivityTaskCompleted h -> 
                f(WaitForActivityTaskResult.Completed(h.ActivityTaskCompletedEventAttributes))

            // TimedOut
            | EventOfType EventType.ActivityTaskTimedOut h -> 
                f(WaitForActivityTaskResult.TimedOut(h.ActivityTaskTimedOutEventAttributes))

            // Canceled
            | EventOfType EventType.ActivityTaskCanceled h -> 
                f(WaitForActivityTaskResult.Canceled(h.ActivityTaskCanceledEventAttributes))

            // Failed
            | EventOfType EventType.ActivityTaskFailed h -> 
                f(WaitForActivityTaskResult.Failed(h.ActivityTaskFailedEventAttributes))

            // ScheduleActivityTaskFailed
            | h when h.ScheduleActivityTaskFailedEventAttributes <> null && 
                        h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Name = activity.Name && 
                        h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Version = activity.Version ->
                f(WaitForActivityTaskResult.ScheduleFailed(h.ScheduleActivityTaskFailedEventAttributes))

            | _ -> 
                // This activity is still running, continue blocking
                blockFlag <- true
                response

        match result with 
        // If this activity is being scheduled then block. Return the decision to schedule the activity and pick up here next decistion task
        | ScheduleActivityTaskResult.Scheduling(_,_) -> 
            blockFlag <- true
            response

        // The StartActivityResult checks for scheduling failure so no need to check history again.
        | ScheduleActivityTaskResult.ScheduleFailed(a) -> f(WaitForActivityTaskResult.ScheduleFailed(a))

        | ScheduleActivityTaskResult.Scheduled(a) ->
            bindWithHistory (a.ActivityType) (a.Control) (a.ActivityId)
        | ScheduleActivityTaskResult.Started(a, activityType, control, activityId) ->
            bindWithHistory activityType control activityId

    // Request Cancel Activity Task 
    member this.Bind(RequestCancelActivityTaskAction.ScheduleResult(result), f:(RequestCancelActivityTaskResult -> RespondDecisionTaskCompletedRequest)) = 
        let bindWithHistory (activity:ActivityType) (control:string) (activityId:string) =
            let combinedHistory = FindActivityTaskHistory DecisionTask (Convert.ToInt32(control)) activityId

            match (combinedHistory) with
            // ScheduleActivityTaskFailed
            | h when h.ScheduleActivityTaskFailedEventAttributes <> null && 
                        h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Name = activity.Name && 
                        h.ScheduleActivityTaskFailedEventAttributes.ActivityType.Version = activity.Version ->
                f(RequestCancelActivityTaskResult.ScheduleFailed(h.ScheduleActivityTaskFailedEventAttributes))

            // Canceled
            | h when h.ActivityTaskCanceledEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.Canceled(h.ActivityTaskCanceledEventAttributes))

            // RequestCancelFailed
            | h when h.RequestCancelActivityTaskFailedEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.RequestCancelFailed(h.RequestCancelActivityTaskFailedEventAttributes))

            // Completed
            | h when h.ActivityTaskCompletedEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.Completed(h.ActivityTaskCompletedEventAttributes))

            // Failed
            | h when h.ActivityTaskFailedEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.Failed(h.ActivityTaskFailedEventAttributes))

            // TimedOut
            | h when h.ActivityTaskTimedOutEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.TimedOut(h.ActivityTaskTimedOutEventAttributes))

            // CancelRequested
            | h when h.ActivityTaskCancelRequestedEventAttributes <> null ->
                f(RequestCancelActivityTaskResult.CancelRequested)

            // Request Cancel
            |_ ->
                blockFlag <- true

                let d = new Decision()
                d.DecisionType <- DecisionType.RequestCancelActivityTask
                d.RequestCancelActivityTaskDecisionAttributes <- new RequestCancelActivityTaskDecisionAttributes()
                d.RequestCancelActivityTaskDecisionAttributes.ActivityId <- activityId
                response.Decisions.Add(d)

                response                    

        match result with 
        // If this activity is being scheduled then block. Return the decision to schedule the activity and pick up here next decistion task
        | ScheduleActivityTaskResult.Scheduling(_,_) -> 
            blockFlag <- true
            response

        // The StartActivityResult checks for scheduling failure so no need to check history again.
        | ScheduleActivityTaskResult.ScheduleFailed(a) -> f(RequestCancelActivityTaskResult.ScheduleFailed(a))

        | ScheduleActivityTaskResult.Scheduled(a) ->
            bindWithHistory (a.ActivityType) (a.Control) (a.ActivityId)
        | ScheduleActivityTaskResult.Started(a, activityType, control, activityId:string) ->
            bindWithHistory activityType control activityId

    // Start and Wait for Lambda Function
    member this.Bind(ScheduleAndWaitForLambdaFunctionAction.Attributes(attr), f:(ScheduleAndWaitForLambdaFunctionResult -> RespondDecisionTaskCompletedRequest)) = 

        let combinedHistory = FindLambdaFunctionHistory DecisionTask (attr.Id) (attr.Name)

        match (combinedHistory) with
        // ScheduleLambdaFunctionFailed
        | h when h.ScheduleLambdaFunctionFailedEventAttributes <> null && 
                    h.ScheduleLambdaFunctionFailedEventAttributes.Id = attr.Id && 
                    h.ScheduleLambdaFunctionFailedEventAttributes.Name = attr.Name -> 
            f(ScheduleAndWaitForLambdaFunctionResult.ScheduleFailed(h.ScheduleLambdaFunctionFailedEventAttributes))

        // StartLambdaFunctionFailed
        | h when h.StartLambdaFunctionFailedEventAttributes <> null -> 
            f(ScheduleAndWaitForLambdaFunctionResult.StartFailed(h.StartLambdaFunctionFailedEventAttributes))

        // Lambda Function Completed
        | h when h.EventType = EventType.LambdaFunctionCompleted -> 
            f(ScheduleAndWaitForLambdaFunctionResult.Completed(h.LambdaFunctionCompletedEventAttributes))

        // Lambda Function TimedOut
        | EventOfType EventType.LambdaFunctionTimedOut h -> 
            f(ScheduleAndWaitForLambdaFunctionResult.TimedOut(h.LambdaFunctionTimedOutEventAttributes))

        // Lambda Function Failed
        | EventOfType EventType.LambdaFunctionFailed h -> 
            f(ScheduleAndWaitForLambdaFunctionResult.Failed(h.LambdaFunctionFailedEventAttributes))

        // Not Scheduled
        | h when h.ScheduleLambdaFunctionFailedEventAttributes = null ->
            blockFlag <- true

            let d = new Decision();
            d.DecisionType <- DecisionType.ScheduleLambdaFunction
            d.ScheduleLambdaFunctionDecisionAttributes <- attr
            response.Decisions.Add(d)
            response
        | _ -> 
            // This lambda function is still running, continue blocking
            blockFlag <- true
            response

    // Start Timer
    member this.Bind(StartTimerAction.Attributes(attr), f:(StartTimerResult -> RespondDecisionTaskCompletedRequest)) = 
        let bindingId = NextBindingId()

        let combinedHistory = FindTimerHistory DecisionTask bindingId

        match (combinedHistory) with
        // StartTimerFailed
        | h when h.StartTimerFailedEventAttributes <> null && 
                    h.StartTimerFailedEventAttributes.TimerId = attr.TimerId ->
            f(StartTimerResult.StartTimerFailed(h.StartTimerFailedEventAttributes))

        // TimerStarted
        | h when h.TimerStartedEventAttributes <> null ->
            f(StartTimerResult.Started(h.TimerStartedEventAttributes))

        // Timer Not Started
        | h when h.TimerStartedEventAttributes = null ->
            attr.Control <- bindingId.ToString()

            let d = new Decision();
            d.DecisionType <- DecisionType.StartTimer
            d.StartTimerDecisionAttributes <- attr
            response.Decisions.Add(d)
                
            f(StartTimerResult.Starting)

        | _ -> failwith "error"

    // Cancel Timer
    member this.Bind(CancelTimerAction.StartResult(result), f:(CancelTimerResult -> RespondDecisionTaskCompletedRequest)) =

        let bindWithHistory (timerId:string) (control:string) =
            let combinedHistory = FindTimerHistory DecisionTask (Convert.ToInt32(control))

            match (combinedHistory) with
            // TimerCanceled
            | EventOfType EventType.TimerCanceled h -> 
                f(CancelTimerResult.Canceled(h.TimerCanceledEventAttributes))

            // TimerFired, could have fired before canceled
            | EventOfType EventType.TimerFired h -> 
                f(CancelTimerResult.Fired(h.TimerFiredEventAttributes))

            // CancelTimerFailed
            | h when h.CancelTimerFailedEventAttributes <> null && 
                        h.CancelTimerFailedEventAttributes.TimerId = timerId ->
                f(CancelTimerResult.CancelTimerFailed(h.CancelTimerFailedEventAttributes))

            | _ -> 
                // This timer has not been canceled yet, make cancel decision
                let d = new Decision();
                d.DecisionType <- DecisionType.CancelTimer
                d.CancelTimerDecisionAttributes <- new CancelTimerDecisionAttributes(TimerId=timerId)
                response.Decisions.Add(d)

                f(CancelTimerResult.Canceling)

        match result with 
        // If this timer is being started then block. Return the current decisions.
        | StartTimerResult.Starting -> 
            blockFlag <- true
            response

        // The StartTimerResult checks for starting failure so no need to check history again.
        | StartTimerResult.StartTimerFailed(_) -> f(CancelTimerResult.NotStarted)

        | StartTimerResult.Started(a) -> bindWithHistory (a.TimerId) (a.Control)

    // Wait For Timer
    member this.Bind(WaitForTimerAction.StartResult(result), f:(WaitForTimerResult -> RespondDecisionTaskCompletedRequest)) =

        let bindWithHistory (timerId:string) (control:string) =
            let combinedHistory = FindTimerHistory DecisionTask (Convert.ToInt32(control))

            match (combinedHistory) with
            // TimerFired
            | EventOfType EventType.TimerFired h -> 
                f(WaitForTimerResult.Fired(h.TimerFiredEventAttributes))

            // TimerCanceled
            | EventOfType EventType.TimerCanceled h -> 
                f(WaitForTimerResult.Canceled(h.TimerCanceledEventAttributes))

            // StartTimerFailed
            | h when h.StartTimerFailedEventAttributes <> null && 
                        h.StartTimerFailedEventAttributes.TimerId = timerId ->
                f(WaitForTimerResult.StartTimerFailed(h.StartTimerFailedEventAttributes))

            | _ -> 
                // This timer is still running, continue blocking
                blockFlag <- true
                response

        match result with 
        // If this timer is being started then block. Return the decision to start the timer and pick up here next decistion task
        | StartTimerResult.Starting -> 
            blockFlag <- true
            response

        // The StartTimerResult checks for starting failure so no need to check history again.
        | StartTimerResult.StartTimerFailed(a) -> f(WaitForTimerResult.StartTimerFailed(a))

        | StartTimerResult.Started(a) -> bindWithHistory (a.TimerId) (a.Control)

    // Record Marker
    member this.Bind(RecordMarkerAction.Attributes(attr), f:(RecordMarkerResult -> RespondDecisionTaskCompletedRequest)) =
        let combinedHistory = FindMarkerHistory DecisionTask attr.MarkerName attr.Details

        match combinedHistory with

        // RecordMarkerFailed
        | h when h.RecordMarkerFailedEventAttributes <> null ->
            f(RecordMarkerResult.RecordMarkerFailed(h.RecordMarkerFailedEventAttributes))

        // MarkerRecorded
        | h when h.MarkerRecordedEventAttributes <> null ->
            f(RecordMarkerResult.MarkerRecorded(h.MarkerRecordedEventAttributes))

        // The marker was never recorded, record it now
        | _ ->
            let d = new Decision();
            d.DecisionType <- DecisionType.RecordMarker
            d.RecordMarkerDecisionAttributes <- attr
            response.Decisions.Add(d)

            f(RecordMarkerResult.Recording)

    // Start Child Workflow Execution
    member this.Bind(StartChildWorkflowExecutionAction.Attributes(attr), f:(StartChildWorkflowExecutionResult -> RespondDecisionTaskCompletedRequest)) =
        let bindingId = NextBindingId()

        let combinedHistory = FindChildWorkflowExecutionHistory DecisionTask bindingId (attr.WorkflowType) (attr.WorkflowId)

        match (combinedHistory) with
        // StartChildWorkflowExecutionFailed
        | h when h.StartChildWorkflowExecutionFailedEventAttributes <> null && 
                    h.StartChildWorkflowExecutionFailedEventAttributes.WorkflowType.Name = attr.WorkflowType.Name && 
                    h.StartChildWorkflowExecutionFailedEventAttributes.WorkflowType.Version = attr.WorkflowType.Version ->
            f(StartChildWorkflowExecutionResult.StartFailed(h.StartChildWorkflowExecutionFailedEventAttributes))

        // Started
        | h when h.ChildWorkflowExecutionStartedEventAttributes <> null ->
            f(StartChildWorkflowExecutionResult.Started(h.ChildWorkflowExecutionStartedEventAttributes, (bindingId.ToString())))

        // Initiated
        | h when h.StartChildWorkflowExecutionInitiatedEventAttributes <> null &&
                    h.StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowType.Name = attr.WorkflowType.Name && 
                    h.StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowType.Version = attr.WorkflowType.Version ->
            f(StartChildWorkflowExecutionResult.Initiated(h.StartChildWorkflowExecutionInitiatedEventAttributes))

        // Not Scheduled
        | h when h.ActivityTaskScheduledEventAttributes = null ->
            attr.Control <- bindingId.ToString()

            let d = new Decision();
            d.DecisionType <- DecisionType.StartChildWorkflowExecution
            d.StartChildWorkflowExecutionDecisionAttributes <- attr
            response.Decisions.Add(d)
                
            f(StartChildWorkflowExecutionResult.Scheduling)

        | _ -> failwith "error"

    // Wait For Child Workflow Execution
    member this.Bind(WaitForChildWorkflowExecutionAction.StartResult(result), f:(WaitForChildWorkflowExecutionResult -> RespondDecisionTaskCompletedRequest)) =

        let bindWithHistory (workflowType:WorkflowType) (control:string) (workflowId:string) =
            let combinedHistory = FindChildWorkflowExecutionHistory DecisionTask (Convert.ToInt32(control)) workflowType workflowId

            match (combinedHistory) with
            // Completed
            | EventOfType EventType.ChildWorkflowExecutionCompleted h -> 
                f(WaitForChildWorkflowExecutionResult.Completed(h.ChildWorkflowExecutionCompletedEventAttributes))

            // TimedOut
            | EventOfType EventType.ChildWorkflowExecutionTimedOut h -> 
                f(WaitForChildWorkflowExecutionResult.TimedOut(h.ChildWorkflowExecutionTimedOutEventAttributes))

            // Canceled
            | EventOfType EventType.ChildWorkflowExecutionCanceled h -> 
                f(WaitForChildWorkflowExecutionResult.Canceled(h.ChildWorkflowExecutionCanceledEventAttributes))

            // Failed
            | EventOfType EventType.ChildWorkflowExecutionFailed h -> 
                f(WaitForChildWorkflowExecutionResult.Failed(h.ChildWorkflowExecutionFailedEventAttributes))

            // Terminated
            | EventOfType EventType.ChildWorkflowExecutionTerminated h -> 
                f(WaitForChildWorkflowExecutionResult.Terminated(h.ChildWorkflowExecutionTerminatedEventAttributes))

            | _ -> 
                // This child workflow execution is still running, continue blocking
                blockFlag <- true
                response

        match result with 
        // If this child workflow execution is being started then block. Return the decision to start the child workflow and pick up here next decision task
        | StartChildWorkflowExecutionResult.Scheduling -> 
            blockFlag <- true
            response

        // The StartChildWorkflowExecutionResult checks for starting failure so no need to check history again.
        | StartChildWorkflowExecutionResult.StartFailed(a) -> f(WaitForChildWorkflowExecutionResult.StartFailed(a))

        | StartChildWorkflowExecutionResult.Initiated(a) ->
            bindWithHistory (a.WorkflowType) (a.Control) (a.WorkflowId)
        | StartChildWorkflowExecutionResult.Started(a, control) ->
            bindWithHistory (a.WorkflowType) (control) (a.WorkflowExecution.WorkflowId)

    // Request Cancel External Workflow Execution
    member this.Bind(RequestCancelExternalWorkflowExecutionAction.Attributes(attr), f:(RequestCancelExternalWorkflowExecutionResult -> RespondDecisionTaskCompletedRequest)) =
        let bindingId = NextBindingId()

        let combinedHistory = FindRequestCancelExternalWorkflowExecutionHistory DecisionTask bindingId attr.WorkflowId

        match (combinedHistory) with
        // Request Delivered
        | h when h.ExternalWorkflowExecutionCancelRequestedEventAttributes <> null ->
            f(RequestCancelExternalWorkflowExecutionResult.Delivered(h.ExternalWorkflowExecutionCancelRequestedEventAttributes))

        // Request Failed
        | h when h.RequestCancelExternalWorkflowExecutionFailedEventAttributes <> null ->
            f(RequestCancelExternalWorkflowExecutionResult.Failed(h.RequestCancelExternalWorkflowExecutionFailedEventAttributes))
 
        // Request Initiated
        | h when h.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes <> null ->
            f(RequestCancelExternalWorkflowExecutionResult.Initiated(h.RequestCancelExternalWorkflowExecutionInitiatedEventAttributes))

        // Request not initiated yet
        | _ ->
            attr.Control <- bindingId.ToString()

            let d = new Decision();
            d.DecisionType <- DecisionType.RequestCancelExternalWorkflowExecution
            d.RequestCancelExternalWorkflowExecutionDecisionAttributes <- attr
            response.Decisions.Add(d)
                
            f(RequestCancelExternalWorkflowExecutionResult.Requesting)

    // Signal External Workflow Execution
    member this.Bind(SignalExternalWorkflowExecutionAction.Attributes(attr), f:(SignalExternalWorkflowExecutionResult -> RespondDecisionTaskCompletedRequest)) =
        let bindingId = NextBindingId()

        let combinedHistory = FindSignalExternalWorkflowExecutionHistory DecisionTask bindingId attr.SignalName attr.WorkflowId

        match combinedHistory with
        // Failed
        | h when h.SignalExternalWorkflowExecutionFailedEventAttributes <> null ->
            f(SignalExternalWorkflowExecutionResult.Failed(h.SignalExternalWorkflowExecutionFailedEventAttributes))
        
        // Signaled
        | h when h.ExternalWorkflowExecutionSignaledEventAttributes <> null ->
            f(SignalExternalWorkflowExecutionResult.Signaled(h.ExternalWorkflowExecutionSignaledEventAttributes))

        // Initiated
        | h when h.SignalExternalWorkflowExecutionInitiatedEventAttributes <> null ->
            f(SignalExternalWorkflowExecutionResult.Initiated(h.SignalExternalWorkflowExecutionInitiatedEventAttributes))

        // Signaling
        | _ -> 
            // The signal was never sent, send it now
            attr.Control <- bindingId.ToString()
                
            let d = new Decision();
            d.DecisionType <- DecisionType.SignalExternalWorkflowExecution
            d.SignalExternalWorkflowExecutionDecisionAttributes <- attr
            response.Decisions.Add(d)
            f(SignalExternalWorkflowExecutionResult.Signaling)

    // Workflow Execution Signaled
    member this.Bind(WorkflowExecutionSignaledAction.Attributes(signalName), f:(WorkflowExecutionSignaledResult -> RespondDecisionTaskCompletedRequest)) =
        let combinedHistory = FindSignalHistory DecisionTask signalName

        match combinedHistory with
        // Signaled
        | h when h.WorkflowExecutionSignaledEventAttributes <> null ->
            f(WorkflowExecutionSignaledResult.Signaled(h.WorkflowExecutionSignaledEventAttributes))
        
        // Not Signaled
        | _ ->
            f(WorkflowExecutionSignaledResult.NotSignaled)

    // Wait For Workflow Execution Signaled
    member this.Bind(WaitForWorkflowExecutionSignaledAction.Attributes(signalName), f:(WaitForWorkflowExecutionSignaledResult -> RespondDecisionTaskCompletedRequest)) =
        let combinedHistory = FindSignalHistory DecisionTask signalName

        match combinedHistory with
        // Signaled
        | h when h.WorkflowExecutionSignaledEventAttributes <> null ->
            f(WaitForWorkflowExecutionSignaledResult.Signaled(h.WorkflowExecutionSignaledEventAttributes))
        
        // Not Signaled, keep waiting
        | _ ->
            blockFlag <- true
            response

    // Workflow Execution Cancel Requested
    member this.Bind(WorkflowExecutionCancelRequestedAction.Attributes(), f:(WorkflowExecutionCancelRequestedResult -> RespondDecisionTaskCompletedRequest)) =
        let cancelRequestedEvent = 
            DecisionTask.Events |>
            Seq.tryFindBack (fun hev -> hev.EventType = EventType.WorkflowExecutionCancelRequested)

        match cancelRequestedEvent with
        // Workflow Cancel Requsted
        | Some(event) ->
            f(WorkflowExecutionCancelRequestedResult.CancelRequested(event.WorkflowExecutionCancelRequestedEventAttributes))
            
        // NotRequested
        | None ->
            f(WorkflowExecutionCancelRequestedResult.NotRequested)            

    // Get Workflow Execution Input
    member this.Bind(GetWorkflowExecutionInputAction.Attributes(), f:(string -> RespondDecisionTaskCompletedRequest)) =
        if DecisionTask.Events.Count >= 1 then
            let firstEvent = DecisionTask.Events.[0]
            if firstEvent.EventType = EventType.WorkflowExecutionStarted then
                f(firstEvent.WorkflowExecutionStartedEventAttributes.Input)
            elif firstEvent.EventType = EventType.WorkflowExecutionContinuedAsNew then
                f(firstEvent.WorkflowExecutionContinuedAsNewEventAttributes.Input)
            else
                f(null)
        else 
            f(null)

    member this.For(enumeration:seq<'T>, f:(_ -> RespondDecisionTaskCompletedRequest)) =

        let processForBlock x = 
            if not blockFlag then f(x) |> ignore
            (not blockFlag)

        enumeration |>
        Seq.takeWhile processForBlock |>
        Seq.iter (fun x -> ())

    member this.While(condition:(unit -> bool), f:(unit -> RespondDecisionTaskCompletedRequest)) =
        while (not blockFlag) && condition() do
            f() |> ignore

    member this.Combine(exprBefore, fAfter) =
        // We assume the exprBefore decisions have been added to the response already
        // Just need to run the expression after this, which will add their decisions while executing
        if blockFlag then response else fAfter()

    member this.TryFinally(exprInside:(unit -> RespondDecisionTaskCompletedRequest), exprFinally:(unit -> unit)) =
        try 
            exprInside()
        finally
            if not blockFlag then exprFinally()

    member this.TryWith(exprInside:(unit -> RespondDecisionTaskCompletedRequest), exprWith:(Exception -> RespondDecisionTaskCompletedRequest)) =
        try
            exprInside()
        with
        | e ->
            exprWith e

