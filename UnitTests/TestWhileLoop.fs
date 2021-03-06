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

module TestWhileLoop =
    let private OfflineHistorySubstitutions =  
        Map.empty<string, string>
        |> Map.add "WorkflowType" "TestConfiguration.WorkflowType"
        |> Map.add "RunId" "\"Offline RunId\""
        |> Map.add "WorkflowId" "workflowId"
        |> Map.add "LambdaRole" "TestConfiguration.LambdaRole"
        |> Map.add "TaskList" "TestConfiguration.TaskList"
        |> Map.add "Identity" "TestConfiguration.Identity"
        |> Map.add "ActivityType" "TestConfiguration.ActivityType"

    let ``A While Loop with an empty body expression which results in Unit``() =
        let workflowId = "A While Loop with an empty body expression which results in Unit"

        let deciderFunc(dt:DecisionTask) =
            let keepgoing = ref true

            Decider(dt, TestConfiguration.ReverseOrder) {
                while !keepgoing do
                    keepgoing := false
                   
                return "TEST PASS"
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
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=4L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 1 do
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

    let ``A While Loop with a body that tries up to three times for a successful Activity Task completion``() =
        let workflowId = "A While Loop with a body that tries up to three times for a successful Activity Task completion"
        let activityId = "Test Activity 1"
        let activityInput = "Test Activity 1 Input"
        let activityResult = "Test Activity 1 Result"
        let failReason = "Fail Reason"
        let failDetails = "Fail Details"

        let deciderFunc(dt:DecisionTask) =
            let tries = ref 0

            Decider(dt, TestConfiguration.ReverseOrder) {
                while !tries < 3 do
                    tries := !tries + 1

                    // Schedule and Wait for an Activity Task
                    let! result = FsDeciderAction.ScheduleActivityTask (
                                    TestConfiguration.ActivityType, 
                                    activityId+((!tries).ToString()), 
                                    input=(!tries).ToString(),
                                    taskList=TestConfiguration.TaskList, 
                                    heartbeatTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                    scheduleToCloseTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                    scheduleToStartTimeout=TestConfiguration.TwentyMinuteTimeout, 
                                    startToCloseTimeout=TestConfiguration.TwentyMinuteTimeout
                                )

                    do! FsDeciderAction.WaitForActivityTask(result)

                    match result with
                    | ScheduleActivityTaskResult.Completed(attr) -> return "TEST PASS"
                    | ScheduleActivityTaskResult.Failed(attr) when attr.Reason = failReason && attr.Details = failDetails -> ()
                    | _ -> ()
                        
                return "TEST FAIL"
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
                              ActivityTaskScheduledEventAttributes(ActivityId="Test Activity 11", ActivityType=TestConfiguration.ActivityType, Control="1", DecisionTaskCompletedEventId=4L, HeartbeatTimeout="1200", Input="1", ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 6
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=5L))
                          |> OfflineHistoryEvent (        // EventId = 7
                              ActivityTaskFailedEventAttributes(Details="Fail Details", Reason="Fail Reason", ScheduledEventId=5L, StartedEventId=6L))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=8L, StartedEventId=9L))
                          |> OfflineHistoryEvent (        // EventId = 11
                              ActivityTaskScheduledEventAttributes(ActivityId="Test Activity 12", ActivityType=TestConfiguration.ActivityType, Control="2", DecisionTaskCompletedEventId=10L, HeartbeatTimeout="1200", Input="2", ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 12
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=11L))
                          |> OfflineHistoryEvent (        // EventId = 13
                              ActivityTaskFailedEventAttributes(Details="Fail Details", Reason="Fail Reason", ScheduledEventId=11L, StartedEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 15
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=14L))
                          |> OfflineHistoryEvent (        // EventId = 16
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=14L, StartedEventId=15L))
                          |> OfflineHistoryEvent (        // EventId = 17
                              ActivityTaskScheduledEventAttributes(ActivityId="Test Activity 13", ActivityType=TestConfiguration.ActivityType, Control="3", DecisionTaskCompletedEventId=16L, HeartbeatTimeout="1200", Input="3", ScheduleToCloseTimeout="1200", ScheduleToStartTimeout="1200", StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 18
                              ActivityTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=17L))
                          |> OfflineHistoryEvent (        // EventId = 19
                              ActivityTaskCompletedEventAttributes(Result="Test Activity 1 Result1", ScheduledEventId=17L, StartedEventId=18L))
                          |> OfflineHistoryEvent (        // EventId = 20
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TaskList))
                          |> OfflineHistoryEvent (        // EventId = 21
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.Identity, ScheduledEventId=20L))
                          |> OfflineHistoryEvent (        // EventId = 22
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=20L, StartedEventId=21L))
                          |> OfflineHistoryEvent (        // EventId = 23
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=22L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.WorkflowType) workflowId (TestConfiguration.TaskList) None None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TaskList) deciderFunc offlineFunc false 4 do
            match i with
            | 1 | 2 | 3 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.ScheduleActivityTask
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityId
                                                        |> should equal (activityId+(i.ToString()))
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Name 
                                                        |> should equal TestConfiguration.ActivityType.Name
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.ActivityType.Version 
                                                        |> should equal TestConfiguration.ActivityType.Version
                resp.Decisions.[0].ScheduleActivityTaskDecisionAttributes.Input  
                                                        |> should equal (i.ToString())

                TestHelper.RespondDecisionTaskCompleted resp

                if i < 3 then
                    TestHelper.PollAndFailActivityTask (TestConfiguration.ActivityType) (Some(failReason)) (Some(failDetails))
                else
                    TestHelper.PollAndCompleteActivityTask (TestConfiguration.ActivityType) (Some(fun _ -> activityResult + "1"))

            | 4 ->
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp

            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

