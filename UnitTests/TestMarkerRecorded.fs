﻿namespace FlowSharp.UnitTests

open FlowSharp
open FlowSharp.Actions
open FlowSharp.UnitTests.TestHelper
open FlowSharp.UnitTests.OfflineHistory

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open NUnit.Framework
open FsUnit

module TestMarkerRecorded =
    let private OfflineHistorySubstitutions =  
        Map.empty<string, string>
        |> Map.add "WorkflowType" "TestConfiguration.TestWorkflowType"
        |> Map.add "RunId" "\"Offline RunId\""
        |> Map.add "WorkflowId" "workflowId"
        |> Map.add "LambdaRole" "TestConfiguration.TestLambdaRole"
        |> Map.add "TaskList" "TestConfiguration.TestTaskList"
        |> Map.add "Identity" "TestConfiguration.TestIdentity"
        |> Map.add "MarkerName" "markerName"
        |> Map.add "MarkerRecordedEventAttributes.Details" "markerDetails"

    let ``Marker Recorded with result of NotRecorded``() =
        let workflowId = "Marker Recorded with result of NotRecorded"
        let markerName = "Test Marker"
        let markerDetails = "Test Marker Details"

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // See if Marker was Recorded
            let! recorded = FlowSharp.MarkerRecorded(markerName)

            match recorded with
            | MarkerRecordedResult.NotRecorded -> return "TEST PASS"

            | _ -> return "TEST FAIL"
        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.TestWorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.TestLambdaRole, TaskList=TestConfiguration.TestTaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.TestWorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=4L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 1 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Marker Recorded with result of MarkerRecorded``() =
        let workflowId = "Marker Recorded with result of MarkerRecorded"
        let markerName = "Test Marker"
        let markerDetails = "Test Marker Details"

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // Record a Marker
            let! marker = FlowSharp.RecordMarker(markerName, markerDetails)

            match marker with
            | RecordMarkerResult.Recording -> return ()
            | RecordMarkerResult.RecordMarkerFailed(_) -> return "TEST FAIL"
            | RecordMarkerResult.MarkerRecorded(_) -> ()

            // See if marker was recorded
            let! recorded = FlowSharp.MarkerRecorded(markerName)
            match recorded with
            | MarkerRecordedResult.MarkerRecorded(attr) when attr.MarkerName = markerName && attr.Details = markerDetails -> return "TEST PASS"
            |_ -> return "TEST FAIL"
        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.TestWorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.TestLambdaRole, TaskList=TestConfiguration.TestTaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.TestWorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              MarkerRecordedEventAttributes(DecisionTaskCompletedEventId=4L, Details=markerDetails, MarkerName=markerName))
                          |> OfflineHistoryEvent (        // EventId = 6
                              WorkflowExecutionSignaledEventAttributes(Input="", SignalName="Some Signal"))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=9L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.RecordMarker
                resp.Decisions.[0].RecordMarkerDecisionAttributes.MarkerName
                                                        |> should equal markerName
                resp.Decisions.[0].RecordMarkerDecisionAttributes.Details
                                                        |> should equal markerDetails

                TestHelper.RespondDecisionTaskCompleted resp

                // Send a signal to trigger a decision task
                TestHelper.SignalWorkflow runId workflowId "Some Signal" ""

            | 2 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Marker Recorded with result of RecordMarkerFailed``() =
        let workflowId = "Marker Recorded with result of RecordMarkerFailed"
        let signalName = "Test Signal"
        let markerName = "Test Marker"
        let markerDetails = "Test Marker Details"
        let cause = RecordMarkerFailedCause.OPERATION_NOT_PERMITTED

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // See if Marker was recorded.
            let! recorded = FlowSharp.MarkerRecorded(markerName)

            match recorded with
            | MarkerRecordedResult.NotRecorded -> return ()
            | MarkerRecordedResult.RecordMarkerFailed(attr) when attr.MarkerName = markerName && attr.Cause = cause -> return "TEST PASS"
            | _ -> return "TEST FAIL"                        
        }

        // OfflineDecisionTask
        let offlineFunc = OfflineDecisionTask (TestConfiguration.TestWorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.TestLambdaRole, TaskList=TestConfiguration.TestTaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.TestWorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              RecordMarkerFailedEventAttributes(Cause=RecordMarkerFailedCause.OPERATION_NOT_PERMITTED, DecisionTaskCompletedEventId=4L, MarkerName=markerName))
                          |> OfflineHistoryEvent (        // EventId = 6
                              WorkflowExecutionSignaledEventAttributes(Input="", SignalName=signalName))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=9L, Result="TEST PASS"))

        if (TestConfiguration.IsConnected) then
            // Only offline is supported for this unit test.
            ()
        else
            // Start the workflow
            let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

            // Poll and make decisions
            for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 2 do
                match i with
                | 1 ->
                    resp.Decisions.Count                    |> should equal 0

                    TestHelper.RespondDecisionTaskCompleted resp                    

                    // Send a signal to trigger a decision task
                    TestHelper.SignalWorkflow runId workflowId "Some Signal" ""

                | 2 -> 
                    resp.Decisions.Count                    |> should equal 1
                    resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                    resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                            |> should equal "TEST PASS"

                    TestHelper.RespondDecisionTaskCompleted resp
                | _ -> ()

