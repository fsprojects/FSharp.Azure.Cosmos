namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

type MultipleItemsScenario (testContext : TestContext) as this =
    inherit DatabaseTestApplicationFactory (testContext)

    let containerId = "operation-tests"

    let firstSeededItem : TestItem = {
        id = $"{testContext.TestName}-readmany-1"
        partitionKey = "integration"
        name = "item-readmany-1"
        quantity = 1
    }

    let secondSeededItem : TestItem = {
        id = $"{testContext.TestName}-readmany-2"
        partitionKey = "integration"
        name = "item-readmany-2"
        quantity = 2
    }

    member _.SeededItems = [ firstSeededItem; secondSeededItem ]

    override _.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        let! container = this.GetOrCreateContainerAsync (containerId, "/partitionKey", cancellationToken)

        for seededItem in this.SeededItems do
            let! createResponse =
                container.ExecuteAsync (
                    create {
                        item seededItem
                        partitionKey seededItem.partitionKey
                    },
                    cancellationToken
                )

            CosmosAssert.IsOk (createResponse.Result, $"ReadMany scenario seed create should succeed for '{seededItem.id}'.")
    }

[<TestClass; TestCategory(TestCategories.ReadMany)>]
type ReadManyOperationIntegrationTests () =
    inherit OperationTestBase<MultipleItemsScenario> ()

    override _.CreateApplication context = MultipleItemsScenario (context)

    [<TestMethod>]
    member this.``ReadMany execute returns matching items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem, secondItem =
            match this.Application.SeededItems with
            | [ firstItem; secondItem ] -> firstItem, secondItem
            | seededItems -> failwith $"Expected exactly two seeded items but got {seededItems.Length}."

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
            Assert.IsTrue (feed.Count = 2, "ReadMany should return requested number of items.")
            Assert.IsTrue (returnedIds.Contains firstItem.id, "ReadMany should include first item.")
            Assert.IsTrue (returnedIds.Contains secondItem.id, "ReadMany should include second item.")
            Assert.IsTrue (readManyResponse.HttpStatusCode = HttpStatusCode.OK, "ReadMany should return HTTP 200.")
        | result -> Assert.Fail ($"Expected read many success, got {result}.")
    }
