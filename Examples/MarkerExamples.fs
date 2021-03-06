﻿module FsDecider.Examples.MarkerExamples

open System

open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open FsDecider
open FsDecider.Actions
open FsDecider.Examples.CommandInterpreter
open FsDecider.UnitTests

// Example m1 : Recording markers
//      This example demonstrates recording and detecting a marker.
// To Run, start the project and type these commands into the command line interpreter.
//    sw m1             (Starts the workflow)
//    dt m1             (Processes the initial decision task, records marker and waits)
//    sg m1             (Sends a signal to the workflow to force a decision task)
//    dt m1             (Processes the final decision task, detects marker and completes workflow)
let private LoadRecordAndDetectMarker() =
    let workflowId = "FsDecider Markers Example"

    let decider(dt:DecisionTask) =
        Decider(dt) {
            do! FsDeciderAction.RecordMarker("Some Marker")

            let! marker = FsDeciderAction.MarkerRecorded("Some Marker")

            match marker with
            | MarkerRecordedResult.Recorded(attr) ->
                // Complete the workflow execution with a result of "OK"
                return "OK"
            | _ -> 
                do! FsDeciderAction.Wait()
        }

    // The code below supports the example runner
    let start = Operation.StartWorkflowExecution(TestConfiguration.WorkflowType, workflowId, None, None)
    AddOperation (Command.StartWorkflow("m1")) start
    AddOperation (Command.SignalWorkflow("m1")) (Operation.SignalWorkflow(workflowId, "Some Signal"))
    AddOperation (Command.DecisionTask("m1")) (Operation.DecisionTask(decider, false, None))

let Load() =
    LoadRecordAndDetectMarker()
