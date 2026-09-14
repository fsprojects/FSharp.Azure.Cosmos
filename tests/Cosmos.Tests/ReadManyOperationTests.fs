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

[<TestClass; ReadManyTestCategory>]
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
            Assert.HasCount (2, feed, "ReadMany should return requested number of items.")
            Assert.Contains (firstItem.id, returnedIds, "ReadMany should include first item.")
            Assert.Contains (secondItem.id, returnedIds, "ReadMany should include second item.")
            Assert.AreEqual (HttpStatusCode.OK, readManyResponse.HttpStatusCode, "ReadMany should return HTTP 200.")
        | result -> Assert.Fail ($"Expected read many success, got {result}.")
    }

    [<TestMethod>]
    member this.``ReadMany execute currently returns Ok instead of NotModified for a matching eTag`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem, secondItem =
            match this.Application.SeededItems with
            | [ firstItem; secondItem ] -> firstItem, secondItem
            | seededItems -> failwith $"Expected exactly two seeded items but got {seededItems.Length}."

        let! baselineResponse =
            container.ExecuteAsync (
                readMany {
                    item firstItem.id firstItem.partitionKey
                    item secondItem.id secondItem.partitionKey
                },
                this.CancellationToken
            )

        match baselineResponse.Result with
        | ReadManyResult.Ok _ -> ()
        | result -> Assert.Fail ($"Expected baseline read many success, got {result}.")

        let! notModifiedResponse =
            container.ExecuteAsync (
                readMany {
                    item firstItem.id firstItem.partitionKey
                    item secondItem.id secondItem.partitionKey
                    eTag baselineResponse.ETag
                },
                this.CancellationToken
            )

        // KNOWN GAP: the successFn in ReadMany.fs decides NotModified by comparing the whole
        // FeedResponse<'T> to Unchecked.defaultof<'T> (the ITEM type's default) — a type
        // mismatch that can never be true, so ReadManyResult.NotModified is unreachable and a
        // matching eTag is silently ignored. This test pins today's actual (buggy) Ok outcome;
        // if it starts failing, that comparison has likely been fixed to inspect the feed's own
        // status, and this test should be replaced with one asserting ReadManyResult.NotModified.
        match notModifiedResponse.Result with
        | ReadManyResult.Ok _ -> ()
        | result -> Assert.Fail ($"Expected the current (buggy) ReadManyResult.Ok, got {result}.")
    }
