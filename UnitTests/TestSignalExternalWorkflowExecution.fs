﻿namespace FsDecider.UnitTests

open FsDecider
open FsDecider.Actions
open FsDecider.UnitTests.TestHelper
open FsDecider.UnitTests.OfflineHistory

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open NUnit.Framework
open FsUnit

module TestSignalExternalWorkflowExecution =
    let private OfflineHistorySubstitutions =  
        Map.empty<string, string>
        |> Map.add "WorkflowType" "TestConfiguration.WorkflowType"
        |> Map.add "RunId" "\"Offline RunId\""
        |> Map.add "WorkflowId" "workflowId"
        |> Map.add "LambdaRole" "TestConfiguration.LambdaRole"
        |> Map.add "TaskList" "TestConfiguration.TaskList"
        |> Map.add "Identity" "TestConfiguration.Identity"
        |> Map.add "SignalName" "signalName"
        |> Map.add "ParentWorkflowExecution" "WorkflowExecution(RunId=\"Parent RunId\", WorkflowId=workflowId)"
        |> Map.add "StartChildWorkflowExecutionInitiatedEventAttributes.TaskList" "childTaskList"
        |> Map.add "StartChildWorkflowExecutionInitiatedEventAttributes.WorkflowId" "childWorkflowId"
        |> Map.add "StartChildWorkflowExecutionFailedEventAttributes.WorkflowId" "childWorkflowId"
        |> Map.add "ChildWorkflowExecutionStartedEventAttributes.WorkflowId" "childWorkflowId"
        |> Map.add "StartChildWorkflowExecutionInitiatedEventAttributes.Input" "signalInput"
        |> Map.add "SignalExternalWorkflowExecutionInitiatedEventAttributes.Input" "signalInput"
        |> Map.add "StartChildWorkflowExecutionInitiatedEventAttributes.Input" "childInput"
        |> Map.add "SignalExternalWorkflowExecutionInitiatedEventAttributes.WorkflowId" "childWorkflowId"
        |> Map.add "SignalExternalWorkflowExecutionFailedEventAttributes.WorkflowId" "childWorkflowId"
        |> Map.add "ChildWorkflowExecutionStartedEventAttributes.WorkflowExecution" "WorkflowExecution(RunId=\"Child RunId\", WorkflowId=childWorkflowId)"
        |> Map.add "ChildWorkflowExecutionCompletedEventAttributes.WorkflowExecution" "WorkflowExecution(RunId=\"Child RunId\", WorkflowId=childWorkflowId)"
        |> Map.add "ExternalWorkflowExecutionSignaledEventAttributes.WorkflowExecution" "WorkflowExecution(RunId=\"Child RunId\", WorkflowId=childWorkflowId)"

    let ``Signal External Workflow Execution with result of Signaling``() =
        let workflowId = "Signal External Workflow Execution with result of Signaling"
        let childWorkflowId = "Child of " + workflowId
        let childInput = "Test Child Input"
        let childTaskList = TaskList(Name="Child")
        let signalName = "Test Signal"
        let signalInput = "Test Signal Input"
        let childRunId = ref ""

        let deciderFunc(dt:DecisionTask) =
            Decider(dt, TestConfiguration.ReverseOrder) {
            
            // Start a Child Workflow Execution
            let! start = FsDeciderAction.StartChildWorkflowExecution
                          (
                            TestConfiguration.WorkflowType,
                            childWorkflowId,
                            input=childInput,
                            childPolicy=ChildPolicy.TERMINATE,
                            lambdaRole=TestConfiguration.LambdaRole,
                            taskList=childTaskList,
                            executionStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout,
                            taskStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                          )

            match start with 
            | StartChildWorkflowExecutionResult.Starting(_) ->
                do! FsDeciderAction.Wait()

            | StartChildWorkflowExecutionResult.Started(attr) ->
                let! signal = FsDeciderAction.SignalExternalWorkflowExecution(signalName, attr.WorkflowExecution.WorkflowId, signalInput, attr.WorkflowExecution.RunId)
                
                match signal with
                | SignalExternalWorkflowExecutionResult.Signaling -> return "TEST PASS"

                | _ -> return "TEST FAIL"
            
            | _ -> return "TEST FAIL"

        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.WorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.LambdaRole, TaskList=TestConfiguration.TaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              StartChildWorkflowExecutionInitiatedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, Control="1", DecisionTaskCompletedEventId=4L, ExecutionStartToCloseTimeout="1200", Input=childInput, LambdaRole=TestConfiguration.LambdaRole, TaskList=childTaskList, TaskStartToCloseTimeout="1200", WorkflowId=childWorkflowId, WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ChildWorkflowExecutionStartedEventAttributes(InitiatedEventId=5L, WorkflowExecution=WorkflowExecution(RunId="Child RunId", WorkflowId=childWorkflowId), WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              SignalExternalWorkflowExecutionInitiatedEventAttributes(Control="2", DecisionTaskCompletedEventId=9L, Input=signalInput, RunId="Child RunId", SignalName=signalName, WorkflowId=childWorkflowId))
                          |> OfflineHistoryEvent (        // EventId = 11
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=9L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartChildWorkflowExecution
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowId 
                                                        |> should equal childWorkflowId
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Name 
                                                        |> should equal TestConfiguration.WorkflowType.Name
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Version 
                                                        |> should equal TestConfiguration.WorkflowType.Version
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.Input 
                                                        |> should equal childInput
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ChildPolicy  
                                                        |> should equal ChildPolicy.TERMINATE
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.LambdaRole  
                                                        |> should equal TestConfiguration.LambdaRole
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskList.Name  
                                                        |> should equal childTaskList.Name
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ExecutionStartToCloseTimeout 
                                                        |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskStartToCloseTimeout 
                                                        |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())

                TestHelper.RespondDecisionTaskCompleted resp

            | 2 -> 
                resp.Decisions.Count                    |> should equal 2
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.SignalExternalWorkflowExecution
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.SignalName
                                                        |> should equal signalName
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.Input
                                                        |> should equal signalInput
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.WorkflowId
                                                        |> should equal childWorkflowId
                resp.Decisions.[1].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[1].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Signal External Workflow Execution with result of Initiated``() =
        let workflowId = "Signal External Workflow Execution with result of Initiated"
        let childWorkflowId = "Child of " + workflowId
        let childInput = "Test Child Input"
        let childTaskList = TaskList(Name="Child")
        let signalName = "Test Signal"
        let signalInput = "Test Signal Input"

        let deciderFunc(dt:DecisionTask) =
            Decider(dt, TestConfiguration.ReverseOrder) {
            
            // Start a Child Workflow Execution
            let! start = FsDeciderAction.StartChildWorkflowExecution
                          (
                            TestConfiguration.WorkflowType,
                            childWorkflowId,
                            input=childInput,
                            childPolicy=ChildPolicy.TERMINATE,
                            lambdaRole=TestConfiguration.LambdaRole,
                            taskList=childTaskList,
                            executionStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout,
                            taskStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                          )

            match start with 
            | StartChildWorkflowExecutionResult.Starting(_) ->
                do! FsDeciderAction.Wait()

            | StartChildWorkflowExecutionResult.Started(attr) ->
                let! signal = FsDeciderAction.SignalExternalWorkflowExecution(signalName, attr.WorkflowExecution.WorkflowId, signalInput, attr.WorkflowExecution.RunId)
                
                match signal with
                | SignalExternalWorkflowExecutionResult.Signaling -> 
                    do! FsDeciderAction.Wait()

                | SignalExternalWorkflowExecutionResult.Initiated(ia) when
                        ia.SignalName = signalName &&
                        ia.Input = signalInput &&
                        ia.WorkflowId = childWorkflowId -> return "TEST PASS"

                | _ -> return "TEST FAIL"
            
            | _ -> return "TEST FAIL"

        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.WorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.LambdaRole, TaskList=TestConfiguration.TaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              StartChildWorkflowExecutionInitiatedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, Control="1", DecisionTaskCompletedEventId=4L, ExecutionStartToCloseTimeout="1200", Input=childInput, LambdaRole=TestConfiguration.LambdaRole, TaskList=childTaskList, TaskStartToCloseTimeout="1200", WorkflowId=childWorkflowId, WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ChildWorkflowExecutionStartedEventAttributes(InitiatedEventId=5L, WorkflowExecution=WorkflowExecution(RunId="Child RunId", WorkflowId=childWorkflowId), WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              SignalExternalWorkflowExecutionInitiatedEventAttributes(Control="2", DecisionTaskCompletedEventId=9L, Input=signalInput, RunId="Child RunId", SignalName=signalName, WorkflowId=childWorkflowId))
                          |> OfflineHistoryEvent (        // EventId = 11
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=11L))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=11L, StartedEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=13L, Result="TEST PASS"))

        // Start the workflow
        if TestConfiguration.IsConnected then
            // Only offline is supported for this unit test
            ()
        else
            let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

            // Poll and make decisions
            for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 3 do
                match i with
                | 1 -> 
                    resp.Decisions.Count                    |> should equal 1
                    resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartChildWorkflowExecution
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowId 
                                                            |> should equal childWorkflowId
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Name 
                                                            |> should equal TestConfiguration.WorkflowType.Name
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Version 
                                                            |> should equal TestConfiguration.WorkflowType.Version
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.Input 
                                                            |> should equal childInput
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ChildPolicy  
                                                            |> should equal ChildPolicy.TERMINATE
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.LambdaRole  
                                                            |> should equal TestConfiguration.LambdaRole
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskList.Name  
                                                            |> should equal childTaskList.Name
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ExecutionStartToCloseTimeout 
                                                            |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())
                    resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskStartToCloseTimeout 
                                                            |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())

                    TestHelper.RespondDecisionTaskCompleted resp

                | 2 -> 
                    resp.Decisions.Count                    |> should equal 1
                    resp.Decisions.[0].DecisionType         |> should equal DecisionType.SignalExternalWorkflowExecution
                    resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.SignalName
                                                            |> should equal signalName
                    resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.Input
                                                            |> should equal signalInput
                    resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.WorkflowId
                                                            |> should equal childWorkflowId

                    TestHelper.RespondDecisionTaskCompleted resp

                    // Send signal to force a decision task
                    //TestHelper.SignalWorkflow runId workflowId "Parent Signal" ""

                | 3 ->
                    resp.Decisions.Count                    |> should equal 1
                    resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                    resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                            |> should equal "TEST PASS"

                    TestHelper.RespondDecisionTaskCompleted resp
                | _ -> ()

    let ``Signal External Workflow Execution with result of Signaled``() =
        let workflowId = "Signal External Workflow Execution with result of Signaled"
        let childWorkflowId = "Child of " + workflowId
        let childInput = "Test Child Input"
        let childTaskList = TaskList(Name="Child")
        let signalName = "Test Signal"
        let signalInput = "Test Signal Input"
        let childRunId = ref ""

        let deciderFunc(dt:DecisionTask) =
            Decider(dt, TestConfiguration.ReverseOrder) {
            
            // Start a Child Workflow Execution
            let! start = FsDeciderAction.StartChildWorkflowExecution
                          (
                            TestConfiguration.WorkflowType,
                            childWorkflowId,
                            input=childInput,
                            childPolicy=ChildPolicy.TERMINATE,
                            lambdaRole=TestConfiguration.LambdaRole,
                            taskList=childTaskList,
                            executionStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout,
                            taskStartToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                          )

            match start with 
            | StartChildWorkflowExecutionResult.Starting(_) ->
                do! FsDeciderAction.Wait()

            | StartChildWorkflowExecutionResult.Started(attr) ->
                let! signal = FsDeciderAction.SignalExternalWorkflowExecution(signalName, attr.WorkflowExecution.WorkflowId, signalInput, attr.WorkflowExecution.RunId)
                
                match signal with
                | SignalExternalWorkflowExecutionResult.Signaling -> 
                    do! FsDeciderAction.Wait()

                | SignalExternalWorkflowExecutionResult.Signaled(sa) when
                        sa.WorkflowExecution.WorkflowId = childWorkflowId -> return "TEST PASS"

                | _ -> return "TEST FAIL"
            
            | _ -> return "TEST FAIL"

        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.WorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.LambdaRole, TaskList=TestConfiguration.TaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              StartChildWorkflowExecutionInitiatedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, Control="1", DecisionTaskCompletedEventId=4L, ExecutionStartToCloseTimeout="1200", Input=childInput, LambdaRole=TestConfiguration.LambdaRole, TaskList=childTaskList, TaskStartToCloseTimeout="1200", WorkflowId=childWorkflowId, WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ChildWorkflowExecutionStartedEventAttributes(InitiatedEventId=5L, WorkflowExecution=WorkflowExecution(RunId="Child RunId", WorkflowId=childWorkflowId), WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              SignalExternalWorkflowExecutionInitiatedEventAttributes(Control="2", DecisionTaskCompletedEventId=9L, Input=signalInput, RunId="Child RunId", SignalName=signalName, WorkflowId=childWorkflowId))
                          |> OfflineHistoryEvent (        // EventId = 11
                              ExternalWorkflowExecutionSignaledEventAttributes(InitiatedEventId=10L, WorkflowExecution=WorkflowExecution(RunId="Child RunId", WorkflowId=childWorkflowId)))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=12L, StartedEventId=13L))
                          |> OfflineHistoryEvent (        // EventId = 15
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=14L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 3 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartChildWorkflowExecution
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowId 
                                                        |> should equal childWorkflowId
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Name 
                                                        |> should equal TestConfiguration.WorkflowType.Name
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.WorkflowType.Version 
                                                        |> should equal TestConfiguration.WorkflowType.Version
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.Input 
                                                        |> should equal childInput
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ChildPolicy  
                                                        |> should equal ChildPolicy.TERMINATE
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.LambdaRole  
                                                        |> should equal TestConfiguration.LambdaRole
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskList.Name  
                                                        |> should equal childTaskList.Name
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.ExecutionStartToCloseTimeout 
                                                        |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())
                resp.Decisions.[0].StartChildWorkflowExecutionDecisionAttributes.TaskStartToCloseTimeout 
                                                        |> should equal (TestConfiguration.TwentyMinuteTimeout.ToString())

                TestHelper.RespondDecisionTaskCompleted resp

            | 2 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.SignalExternalWorkflowExecution
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.SignalName
                                                        |> should equal signalName
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.Input
                                                        |> should equal signalInput
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.WorkflowId
                                                        |> should equal childWorkflowId

                TestHelper.RespondDecisionTaskCompleted resp

            | 3 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Signal External Workflow Execution with result of Failed``() =
        let workflowId = "Signal External Workflow Execution with result of Failed"
        let childWorkflowId = "DoesNotExist_18091B56-07F6-4635-9C65-0E49BDA2F62C"
        let signalName = "Test Signal"
        let signalInput = "Test Signal Input"
        let cause = SignalExternalWorkflowExecutionFailedCause.UNKNOWN_EXTERNAL_WORKFLOW_EXECUTION

        let deciderFunc(dt:DecisionTask) =
            Decider(dt, TestConfiguration.ReverseOrder) {
            
            let! signal = FsDeciderAction.SignalExternalWorkflowExecution(signalName, childWorkflowId, signalInput)
                
            match signal with
            | SignalExternalWorkflowExecutionResult.Signaling -> 
                do! FsDeciderAction.Wait()

            | SignalExternalWorkflowExecutionResult.Failed(attr) when
                        attr.Cause = cause &&
                        attr.WorkflowId = childWorkflowId -> return "TEST PASS"

            | _ -> return "TEST FAIL"

        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.WorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.LambdaRole, TaskList=TestConfiguration.TaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.WorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              SignalExternalWorkflowExecutionInitiatedEventAttributes(Control="1", DecisionTaskCompletedEventId=4L, Input=signalInput, SignalName=signalName, WorkflowId=childWorkflowId))
                          |> OfflineHistoryEvent (        // EventId = 6
                              SignalExternalWorkflowExecutionFailedEventAttributes(Cause=SignalExternalWorkflowExecutionFailedCause.UNKNOWN_EXTERNAL_WORKFLOW_EXECUTION, Control="1", DecisionTaskCompletedEventId=4L, InitiatedEventId=5L, WorkflowId=childWorkflowId))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=9L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.SignalExternalWorkflowExecution
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.SignalName
                                                        |> should equal signalName
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.Input
                                                        |> should equal signalInput
                resp.Decisions.[0].SignalExternalWorkflowExecutionDecisionAttributes.WorkflowId
                                                        |> should equal childWorkflowId

                TestHelper.RespondDecisionTaskCompleted resp

            | 2 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

