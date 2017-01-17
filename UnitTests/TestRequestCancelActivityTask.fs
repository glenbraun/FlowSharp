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

module TestRequestCancelActivityTask =
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
        |> Map.add "ActivityTaskScheduledEventAttributes.Input" "activityInput"
        |> Map.add "ActivityTaskCompleted.Result" "activityResult"
        |> Map.add "ActivityTaskCanceled.Details" "activityDetails"

    let ``Request Cancel Activity Task with result of CancelRequested``() =
        let workflowId = "Request Cancel Activity Task with result of CancelRequested"
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let activityResult = "Test Activity 1 Result"
        let signalName = "Signal for RequestCancelActivityTask"
        
        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder) {
            
            // Schedule an Activity Task
            let! activity = FlowSharp.ScheduleActivityTask (
                                TestConfiguration.TestActivityType, 
                                activityId, 
                                input=activityInput,
                                taskList=TestConfiguration.TestTaskList, 
                                heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToStartTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                            )
            
            let! wait = FlowSharp.WaitForWorkflowExecutionSignaled(signalName)

            let! cancel = FlowSharp.RequestCancelActivityTask(activity)

            match cancel with
            | RequestCancelActivityTaskResult.CancelRequested(attr) when attr.ActivityId = activityId -> return "TEST PASS"
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
                              ActivityTaskScheduledEventAttributes(ActivityId=activityId, ActivityType=TestConfiguration.TestActivityType, Control="1", DecisionTaskCompletedEventId=4L, HeartbeatTimeout="1200", Input=activityInput, ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=5L))
                          |> OfflineHistoryEvent (        // EventId = 7
                              WorkflowExecutionSignaledEventAttributes(Input="Signal Input", SignalName="Signal for RequestCancelActivityTask"))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=8L, StartedEventId=9L))
                          |> OfflineHistoryEvent (        // EventId = 11
                              ActivityTaskCancelRequestedEventAttributes(ActivityId=activityId, DecisionTaskCompletedEventId=10L))
                          |> OfflineHistoryEvent (        // EventId = 12
                              WorkflowExecutionSignaledEventAttributes(Input="Signal Input", SignalName="Signal for RequestCancelActivityTask"))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=13L))
                          |> OfflineHistoryEvent (        // EventId = 15
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=13L, StartedEventId=14L))
                          |> OfflineHistoryEvent (        // EventId = 16
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=15L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 3 do
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

                TestHelper.PollAndStartActivityTask (TestConfiguration.TestActivityType) |> ignore

                TestHelper.SignalWorkflow runId workflowId signalName "Signal Input"

            | 2 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.RequestCancelActivityTask
                resp.Decisions.[0].RequestCancelActivityTaskDecisionAttributes.ActivityId
                                                        |> should equal activityId
                
                TestHelper.RespondDecisionTaskCompleted resp

                TestHelper.SignalWorkflow runId workflowId signalName "Signal Input"

            | 3 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
                
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Request Cancel Activity Task with result of RequestCancelFailed``() =
        let workflowId = "Request Cancel Activity Task with result of RequestCancelFailed"
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let cancelCause = RequestCancelActivityTaskFailedCause.ACTIVITY_ID_UNKNOWN
        let signalName = "Signal for RequestCancelActivityTask"
        let fakeActivityId = activityId + "_DoesNotExist_E8F98536-F45F-4D3B-BA86-8EA9CAF5D674"
        
        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder) {
            
            // Schedule an Activity Task
            let! activity = FlowSharp.ScheduleActivityTask (
                                TestConfiguration.TestActivityType, 
                                activityId, 
                                input=activityInput,
                                taskList=TestConfiguration.TestTaskList, 
                                heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToStartTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                            )
            
            let! wait = FlowSharp.WaitForWorkflowExecutionSignaled(signalName)

            // Improper usage here but requried to force RequestCancelFailed 
            let fakeStart = 
                match activity with
                | ScheduleActivityTaskResult.Started(started, scheduled) -> 
                    // Make a copy of the scheduled event attributes.
                    // Everything is the same except the ActivityId
                    let fakeScheduled = ActivityTaskScheduledEventAttributes()
                    fakeScheduled.ActivityId <- fakeActivityId
                    fakeScheduled.ActivityType <- scheduled.ActivityType
                    fakeScheduled.Control <- scheduled.Control
                    fakeScheduled.DecisionTaskCompletedEventId <- scheduled.DecisionTaskCompletedEventId
                    fakeScheduled.HeartbeatTimeout <- scheduled.HeartbeatTimeout
                    fakeScheduled.Input <- scheduled.Input
                    fakeScheduled.ScheduleToCloseTimeout <- scheduled.ScheduleToCloseTimeout
                    fakeScheduled.ScheduleToStartTimeout <- scheduled.ScheduleToStartTimeout
                    fakeScheduled.StartToCloseTimeout <- scheduled.StartToCloseTimeout
                    fakeScheduled.TaskList <- scheduled.TaskList
                    fakeScheduled.TaskPriority <- scheduled.TaskPriority

                    ScheduleActivityTaskResult.Started(started, fakeScheduled)
                | s -> s

            let! cancel = FlowSharp.RequestCancelActivityTask(fakeStart)

            match cancel with
            | RequestCancelActivityTaskResult.RequestCancelFailed(attr) 
                when attr.ActivityId = fakeActivityId &&
                     attr.Cause = cancelCause -> return "TEST PASS"
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
                              ActivityTaskScheduledEventAttributes(ActivityId=activityId, ActivityType=TestConfiguration.TestActivityType, Control="1", DecisionTaskCompletedEventId=4L, HeartbeatTimeout="1200", Input=activityInput, ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=5L))
                          |> OfflineHistoryEvent (        // EventId = 7
                              WorkflowExecutionSignaledEventAttributes(Input="Signal Input", SignalName="Signal for RequestCancelActivityTask"))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=8L, StartedEventId=9L))
                          |> OfflineHistoryEvent (        // EventId = 11
                              RequestCancelActivityTaskFailedEventAttributes(ActivityId=fakeActivityId, Cause=RequestCancelActivityTaskFailedCause.ACTIVITY_ID_UNKNOWN, DecisionTaskCompletedEventId=10L))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=12L, StartedEventId=13L))
                          |> OfflineHistoryEvent (        // EventId = 15
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=14L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 3 do
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

                TestHelper.PollAndStartActivityTask (TestConfiguration.TestActivityType) |> ignore

                TestHelper.SignalWorkflow runId workflowId signalName "Signal Input"

            | 2 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.RequestCancelActivityTask
                resp.Decisions.[0].RequestCancelActivityTaskDecisionAttributes.ActivityId
                                                        |> should equal fakeActivityId
                
                TestHelper.RespondDecisionTaskCompleted resp

            | 3 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
                
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId (OfflineHistorySubstitutions.Add("RequestCancelActivityTaskFailedEventAttributes.ActivityId", "fakeActivityId"))

    let ``Request Cancel Activity Task with result of ActivityFinished``() =
        let workflowId = "Request Cancel Activity Task with result of ActivityFinished"
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let activityResult = "Test Activity 1 Result"
        let signalName = "Signal for RequestCancelActivityTask"
        let timeoutType = ActivityTaskTimeoutType.SCHEDULE_TO_START
        
        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder) {
            
            // Schedule an Activity Task
            let! activity = FlowSharp.ScheduleActivityTask (
                                TestConfiguration.TestActivityType, 
                                activityId, 
                                input=activityInput,
                                taskList=TestConfiguration.TestTaskList, 
                                heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToStartTimeout="5", 
                                startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                            )
            
            let! wait = FlowSharp.WaitForWorkflowExecutionSignaled(signalName)

            let! cancel = FlowSharp.RequestCancelActivityTask(activity)

            match cancel with
            | RequestCancelActivityTaskResult.ActivityFinished -> return "TEST PASS"
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
                              ActivityTaskScheduledEventAttributes(ActivityId=activityId, ActivityType=TestConfiguration.TestActivityType, Control="1", DecisionTaskCompletedEventId=4L, HeartbeatTimeout="1200", Input=activityInput, ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="5", StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ActivityTaskTimedOutEventAttributes(ScheduledEventId=5L, TimeoutType=ActivityTaskTimeoutType.SCHEDULE_TO_START))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              WorkflowExecutionSignaledEventAttributes(Input="Signal Input", SignalName="Signal for RequestCancelActivityTask"))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=9L))
                          |> OfflineHistoryEvent (        // EventId = 11
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=10L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 2 do
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
                if TestConfiguration.IsConnected then
                    System.Diagnostics.Debug.WriteLine("Sleeping to wait for activity task to timeout.")
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(6.0)) // Sleep one second longer than the schedule to start timeout

                TestHelper.SignalWorkflow runId workflowId signalName "Signal Input"

            | 2 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
                
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Request Cancel Activity Task with result of ActivityScheduleFailed``() =
        let workflowId = "Request Cancel Activity Task with result of ActivityScheduleFailed"
        let activityType = ActivityType(Name="ActivityDoesNotExist_D8E3FFCA-E25A-4D2E-B328-584F2A387EF8", Version="1")
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let activityResult = "Test Activity 1 Result"
        
        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt, TestConfiguration.ReverseOrder) {
            
            // Schedule an Activity Task
            let! activity = FlowSharp.ScheduleActivityTask (
                                activityType, 
                                activityId, 
                                input=activityInput,
                                taskList=TestConfiguration.TestTaskList, 
                                heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                scheduleToStartTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                            )
            
            let! cancel = FlowSharp.RequestCancelActivityTask(activity)

            match cancel with
            | RequestCancelActivityTaskResult.ActivityScheduleFailed -> return "TEST PASS"
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
                              ScheduleActivityTaskFailedEventAttributes(ActivityId=activityId, ActivityType=activityType, Cause=ScheduleActivityTaskFailedCause.ACTIVITY_TYPE_DOES_NOT_EXIST, DecisionTaskCompletedEventId=4L))
                          |> OfflineHistoryEvent (        // EventId = 6
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=6L))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=6L, StartedEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=8L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.ScheduleActivityTask
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityId
                                                        |> should equal activityId
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Name 
                                                        |> should equal activityType.Name
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Version 
                                                        |> should equal activityType.Version
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.Input  
                                                        |> should equal activityInput
            
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
