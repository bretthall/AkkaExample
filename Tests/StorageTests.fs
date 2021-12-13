module Tests.StorageTests

open NUnit.Framework
open FsUnitTyped

open Akkling

open AkkaExample.Storage

[<Test>]
let ``query for non-existing value gives correct result`` () =
    TestKit.testDefault <| fun tk ->
        let storage = startStorageActor tk.Sys
        
        let key = "key"
        let receiver = tk.CreateTestProbe "receiver"
        storage <! Query {|key = key; receiver = typed receiver|}
        let response = receiver.ExpectMsg<Response> ()
        response.key |> shouldEqual key
        response.value |> shouldEqual None

[<Test>]
let ``query for existing value gives correct result`` () =
    TestKit.testDefault <| fun tk ->
        let receiver = tk.CreateTestProbe "receiver"
        let storage = startStorageActor tk.Sys
        let key = "key"
        let value = "value"
        storage <! Update {|key = key; value = value|}
        
        storage <! Query {|key = key; receiver = typed receiver|}
        let response = receiver.ExpectMsg<Response> ()
        response.key |> shouldEqual key
        response.value |> shouldEqual (Some value)
        