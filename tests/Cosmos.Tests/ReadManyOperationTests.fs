namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration.MultipleItemsScenario
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; ReadManyTestCategory>]
type ReadManyOperationIntegrationTests () =
    inherit OperationTestBase<MultipleItemsScenario.TestApplicationFactory> ()

    override _.CreateApplication context = MultipleItemsScenario.TestApplicationFactory (context)

    [<TestMethod>]
    member this.``ReadMany execute returns matching items`` () : Task = task {
        let! container = this.GetContainer ()
        let { FirstSeededItem = firstItem; SecondSeededItem = secondItem } = this.Application.TestData

        let! readManyResponse =
            container.ExecuteAsync (
                readMany {
                    item firstItem.id firstItem.partitionKey
                    item secondItem.id secondItem.partitionKey
                },
                this.CancellationToken
            )

        match readManyResponse.Result with
        | ReadManyResult.Ok (feed : FeedResponse<TestItem>) ->
            let returnedIds = feed |> Seq.map _.id |> Set.ofSeq
            Assert.HasCount (2, feed, "ReadMany should return requested number of items.")
            Assert.Contains (firstItem.id, returnedIds, "ReadMany should include first item.")
            Assert.Contains (secondItem.id, returnedIds, "ReadMany should include second item.")
            Assert.AreEqual (HttpStatusCode.OK, readManyResponse.HttpStatusCode, "ReadMany should return HTTP 200.")
        | result -> Assert.Fail ($"Expected read many success, got {result}.")
    }
