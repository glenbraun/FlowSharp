﻿namespace FlowSharp.UnitTests

open FlowSharp
open FlowSharp.UnitTests.TestHelper
open FlowSharp.UnitTests.OfflineHistory

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open NUnit.Framework
open FsUnit

module TestWaitForTimer =
    let private OfflineHistorySubstitutions =  
        Map.empty<string, string>
        |> Map.add "WorkflowType" "TestConfiguration.TestWorkflowType"
        |> Map.add "RunId" "\"Offline RunId\""
        |> Map.add "WorkflowId" "workflowId"
        |> Map.add "LambdaRole" "TestConfiguration.TestLambdaRole"
        |> Map.add "TaskList" "TestConfiguration.TestTaskList"
        |> Map.add "Identity" "TestConfiguration.TestIdentity"
        |> Map.add "TimerId" "timerId"

    let ``Wait for Timer with result of Fired``() =
        let workflowId = "Wait for Timer with result of Fired"
        let timerId = "timer1"
        let startToFireTimeout = 5u

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // Start a Timer
            let! timer1 = FlowSharp.StartTimer(timerId=timerId, startToFireTimeout = startToFireTimeout)

            let! wait = FlowSharp.WaitForTimer(timer1)

            match wait with
            | WaitForTimerResult.Fired(attr) when attr.TimerId = timerId -> return "TEST PASS"
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
                              TimerStartedEventAttributes(Control="1", DecisionTaskCompletedEventId=4L, StartToFireTimeout="5", TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 6
                              TimerFiredEventAttributes(StartedEventId=5L, TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=9L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 2 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartTimer
                resp.Decisions.[0].StartTimerDecisionAttributes.TimerId
                                                        |> should equal timerId
                resp.Decisions.[0].StartTimerDecisionAttributes.StartToFireTimeout
                                                        |> should equal (startToFireTimeout.ToString())

                TestHelper.RespondDecisionTaskCompleted resp
                
                if TestConfiguration.IsConnected then
                    System.Diagnostics.Debug.WriteLine("Sleeping for 5 seconds to give timer time to complete.")
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5.0))

            | 2 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Wait for Timer with result of Canceled``() =
        let workflowId = "Wait for Timer with result of Canceled"
        let timerId = "timer1"
        let startToFireTimeout = uint32(TimeSpan.FromDays(100.0).TotalSeconds)

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // Start a Timer
            let! timer1 = FlowSharp.StartTimer(timerId=timerId, startToFireTimeout = startToFireTimeout)

            match timer1 with
            | StartTimerResult.Starting -> 
                let! wait = FlowSharp.WaitForTimer(timer1)
                ()
            | StartTimerResult.Started(_) ->
                let! cancel = FlowSharp.CancelTimer(timer1)

                let! wait = FlowSharp.WaitForTimer(timer1)
                match wait with
                | WaitForTimerResult.Canceled(attr) when attr.TimerId = timerId -> return "TEST PASS"
                | _ -> return "TEST FAIL"
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
                              TimerStartedEventAttributes(Control="1", DecisionTaskCompletedEventId=4L, StartToFireTimeout="8640000", TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 6
                              WorkflowExecutionSignaledEventAttributes(Input="", SignalName="Test Signal"))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              TimerCanceledEventAttributes(DecisionTaskCompletedEventId=9L, StartedEventId=5L, TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 11
                              WorkflowExecutionSignaledEventAttributes(Input="", SignalName="Test Signal"))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=12L, StartedEventId=13L))
                          |> OfflineHistoryEvent (        // EventId = 15
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=14L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 3 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartTimer
                resp.Decisions.[0].StartTimerDecisionAttributes.TimerId
                                                        |> should equal timerId
                resp.Decisions.[0].StartTimerDecisionAttributes.StartToFireTimeout
                                                        |> should equal (startToFireTimeout.ToString())

                TestHelper.RespondDecisionTaskCompleted resp

                // Signal workflow to force a decision task
                TestHelper.SignalWorkflow runId workflowId "Test Signal" ""
                
            | 2 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CancelTimer
                resp.Decisions.[0].CancelTimerDecisionAttributes.TimerId 
                                                        |> should equal timerId

                TestHelper.RespondDecisionTaskCompleted resp

                // Signal workflow to force a decision task
                TestHelper.SignalWorkflow runId workflowId "Test Signal" ""

            | 3 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.CompleteWorkflowExecution
                resp.Decisions.[0].CompleteWorkflowExecutionDecisionAttributes.Result 
                                                        |> should equal "TEST PASS"

                TestHelper.RespondDecisionTaskCompleted resp
            | _ -> ()

        // Generate Offline History
        TestHelper.GenerateOfflineDecisionTaskCodeSnippet runId workflowId OfflineHistorySubstitutions

    let ``Wait for Timer with result of StartTimerFailed``() =
        let workflowId = "Wait for Timer with result of StartTimerFailed"
        let timerId = "timer1"
        let inUseTimerId = "Value Will Be Swapped Intentionally"
        let startToFireTimeout = uint32(TimeSpan.FromDays(100.0).TotalSeconds)
        let cause = StartTimerFailedCause.TIMER_ID_ALREADY_IN_USE

        let deciderFunc(dt:DecisionTask) =
            FlowSharp.Builder(dt) {
            
            // Start a Timer
            let! timer1 = FlowSharp.StartTimer(timerId=timerId, startToFireTimeout = startToFireTimeout)

            match timer1 with
            | StartTimerResult.Starting -> 
                let! wait = FlowSharp.WaitForTimer(timer1)
                ()
            | StartTimerResult.Started(_) ->
                // Start another timer, but swap the id in the decisions to force the StartTimerFailed condition
                let! timerInUse = FlowSharp.StartTimer(timerId=inUseTimerId, startToFireTimeout = startToFireTimeout)

                let! wait = FlowSharp.WaitForTimer(timer1)
                ()

            | StartTimerResult.StartTimerFailed(_) ->
                let! wait = FlowSharp.WaitForTimer(timer1)

                match wait with
                | WaitForTimerResult.StartTimerFailed(attr) when attr.TimerId = timerId && attr.Cause = cause -> return "TEST PASS"
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
                              TimerStartedEventAttributes(Control="1", DecisionTaskCompletedEventId=4L, StartToFireTimeout="8640000", TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 6
                              WorkflowExecutionSignaledEventAttributes(Input="", SignalName="Test Signal"))
                          |> OfflineHistoryEvent (        // EventId = 7
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 8
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=7L))
                          |> OfflineHistoryEvent (        // EventId = 9
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=7L, StartedEventId=8L))
                          |> OfflineHistoryEvent (        // EventId = 10
                              StartTimerFailedEventAttributes(Cause=StartTimerFailedCause.TIMER_ID_ALREADY_IN_USE, DecisionTaskCompletedEventId=9L, TimerId=timerId))
                          |> OfflineHistoryEvent (        // EventId = 11
                              DecisionTaskScheduledEventAttributes(StartToCloseTimeout="1200", TaskList=TestConfiguration.TestTaskList))
                          |> OfflineHistoryEvent (        // EventId = 12
                              DecisionTaskStartedEventAttributes(Identity=TestConfiguration.TestIdentity, ScheduledEventId=11L))
                          |> OfflineHistoryEvent (        // EventId = 13
                              DecisionTaskCompletedEventAttributes(ScheduledEventId=11L, StartedEventId=12L))
                          |> OfflineHistoryEvent (        // EventId = 14
                              WorkflowExecutionCompletedEventAttributes(DecisionTaskCompletedEventId=13L, Result="TEST PASS"))

        // Start the workflow
        let runId = TestHelper.StartWorkflowExecutionOnTaskList (TestConfiguration.TestWorkflowType) workflowId (TestConfiguration.TestTaskList) None None

        // Poll and make decisions
        for (i, resp) in TestHelper.PollAndDecide (TestConfiguration.TestTaskList) deciderFunc offlineFunc 3 do
            match i with
            | 1 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartTimer
                resp.Decisions.[0].StartTimerDecisionAttributes.TimerId
                                                        |> should equal timerId
                resp.Decisions.[0].StartTimerDecisionAttributes.StartToFireTimeout
                                                        |> should equal (startToFireTimeout.ToString())

                TestHelper.RespondDecisionTaskCompleted resp

                // Signal workflow to force a decision task
                TestHelper.SignalWorkflow runId workflowId "Test Signal" ""
                
            | 2 -> 
                resp.Decisions.Count                    |> should equal 1
                resp.Decisions.[0].DecisionType         |> should equal DecisionType.StartTimer
                resp.Decisions.[0].StartTimerDecisionAttributes.TimerId 
                                                        |> should equal inUseTimerId

                // Intentionally swap the StartTimer TimerId
                resp.Decisions.[0].StartTimerDecisionAttributes.TimerId <- timerId

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
