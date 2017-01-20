﻿namespace FlowSharp.UnitTests

open FlowSharp
open FlowSharp.Actions
open FlowSharp.ExecutionContext
open FlowSharp.UnitTests.TestHelper
open FlowSharp.UnitTests.OfflineHistory

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open NUnit.Framework
open FsUnit

module TestExecutionContextManager =

    let private OfflineHistorySubstitutions =  
        Map.empty<string, string>
        |> Map.add "WorkflowType" "TestConfiguration.TestWorkflowType"
        |> Map.add "RunId" "\"Offline RunId\""
        |> Map.add "WorkflowId" "workflowId"
        |> Map.add "LambdaRole" "TestConfiguration.TestLambdaRole"
        |> Map.add "TaskList" "TestConfiguration.TestTaskList"
        |> Map.add "Identity" "TestConfiguration.TestIdentity"
        |> Map.add "ActivityId" "activityId"
        |> Map.add "ActivityType" "TestConfiguration.TestActivityType"
        |> Map.add "ActivityTaskCompleted.Result" "activityResult"

    let ``Simple Execution Context``() =
        let workflowId = "Simple Execution Context"
        let executionContext = "Test Execution Context"
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let activityResult = "Test Activity 1 Result"
        let signalName = "Test Signal"


        let action = FlowSharp.ScheduleAndWaitForActivityTask (
                            TestConfiguration.TestActivityType, 
                            activityId, 
                            input=activityInput,
                            taskList=TestConfiguration.TestTaskList, 
                            heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                            scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                            scheduleToStartTimeout=TestConfiguration.TwentyMinuteTimeout, 
                            startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout,
                            pushToContext=true
                        )

        let deciderFunc1(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder, Some(ExecutionContextManager() :> IContextManager)) {

            let! start = action

            return ()
        }

        let deciderFunc2(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder, Some(ExecutionContextManager() :> IContextManager)) {

            let! result = action

            match result with
            | ScheduleActivityTaskResult.Completed(attr) when attr.Result = activityResult -> 
                return "TEST PASS"
            | _ -> 
                return "TEST FAIL"
        }

        // OfflineDecisionTask
        let offlineFunc1 = OfflineDecisionTask (TestConfiguration.TestWorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 1
                              WorkflowExecutionStartedEventAttributes(ChildPolicy=ChildPolicy.TERMINATE, ExecutionStartToCloseTimeout="1200", LambdaRole=TestConfiguration.TestLambdaRole, TaskList=TestConfiguration.TestTaskList, TaskStartToCloseTimeout="1200", WorkflowType=TestConfiguration.TestWorkflowType))
                          |> OfflineHistoryEvent (        // EventId = 2
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 3
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=2L))
                          |> OfflineHistoryEvent (        // EventId = 4
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=2L, StartedEventId=3L))
                          |> OfflineHistoryEvent (        // EventId = 5
                              ActivityTaskScheduledEventAttributes(ActivityId=activityId, ActivityType=TestConfiguration.TestActivityType, DecisionTaskCompletedEventId=4L, HeartbeatTimeout="1200", Input="Test Activity 1 Input", ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=5L))
                          |> OfflineHistoryEvent (        // EventId = 7
                              ActivityTaskCompletedEventAttributes(Result="Test Activity 1 Result", ScheduledEventId=5L, StartedEventId=6L))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=8L))


        let offlineFunc2 = OfflineDecisionTask (TestConfiguration.TestWorkflowType) (WorkflowExecution(RunId="Offline RunId", WorkflowId = workflowId))
                          |> OfflineHistoryEvent (        // EventId = 10
                              DecisionTaskCompletedEventAttributes(ExecutionContext="ScheduleActivityTask(ActivityId=\"Test Activity 1\",ActivityType=ActivityType(Name=\"Activity1\",Version=\"2\"))=>Completed(Result=\"Test Activity 1 Result\")", ScheduledEventId=8L, StartedEventId=9L))
                          |> OfflineHistoryEvent (        // EventId = 11
                              WorkflowExecutionSignaledEventAttributes(Input="Some Signal Input", SignalName="Test Signal"))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskCompletedEventAttributes(ExecutionContext="ScheduleActivityTask(ActivityId=\"Test Activity 1\",ActivityType=ActivityType(Name=\"Activity1\",Version=\"2\"))=>Completed(Result=\"Test Activity 1 Result\")", ScheduledEventId=12L, StartedEventId=13L))
                          |> OfflineHistoryEvent (        // EventId = 15
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=14L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc1 offlineFunc1 true 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.ScheduleActivityTask
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityId
                                                        |> should equal activityId
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Name 
                                                        |> should equal TestConfiguration.TestActivityType.Name
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Version 
                                                        |> should equal TestConfiguration.TestActivityType.Version
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.Input  
                                                        |> should equal activityInput

                TestHelper.RespondDecisionTaskCompleted resp
                TestHelper.PollAndCompleteActivityTask (TestConfiguration.TestActivityType) (Some(fun _ -> activityResult))

            | 2 ->
                resp.Decisions.Count                    |> should equal 0

                TestHelper.RespondDecisionTaskCompleted resp

            | _ -> ()
        
        // Send signal to generate decision task
        TestHelper.SignalWorkflow runId workflowId "Test Signal" ""

        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc2 offlineFunc2 true 1 do
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
