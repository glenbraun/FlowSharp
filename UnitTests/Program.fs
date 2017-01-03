﻿namespace FlowSharp.UnitTests

open System
open Amazon
open Amazon.SimpleWorkflow
open Amazon.SimpleWorkflow.Model

open NUnit.Framework
open FsUnit
open Fuchu


(*
    let inline add x y = x + y

    let ``When 2 is added to 2 expect 4``() = 
        add 2 2 |> should equal 5

    let ``When 2.0 is added to 2.0 expect 4.01``() = 
        add 2.0 2.0 |> should (equalWithin 0.1) 4.01

    let ``When ToLower(), expect lowercase letters``() = 
        "FSHARP".ToLower() |> should startWith "fs"

    let simpleTest = 
        testCase "A simple test" <| 
            fun _ -> Assert.Equal("2+2", 4, 2+2)

    let tests = 
        testList "A test group" [
            testCase "one test" <|
                ``When 2 is added to 2 expect 4``
            testCase "another test" <|
                ``When 2.0 is added to 2.0 expect 4.01``
        ]
*)

module Main =

    [<EntryPoint>]
    let main argv = 
        0
        //run tests 

