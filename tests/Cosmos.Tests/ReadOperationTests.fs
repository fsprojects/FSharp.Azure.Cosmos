namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

type SingleItemScenario (testContext : TestContext) as this =
    inherit DatabaseTestApplicationFactory (testContext)

    let containerId = "operation-tests"

    let seededItem : TestItem = {
        id = $"{testContext.TestName}-read"
        partitionKey = "integration"
        name = "item-read"
        quantity = 1
    }

    member _.SeededItem = seededItem

    override _.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        let! container = this.GetOrCreateContainerAsync (containerId, "/partitionKey", cancellationToken)

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item seededItem
                    partitionKey seededItem.partitionKey
                },
                cancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Read scenario seed create should succeed.")
    }

[<TestClass; ReadTestCategory>]
type ReadOperationIntegrationTests () =
    inherit OperationTestBase<SingleItemScenario> ()

    override _.CreateApplication context = SingleItemScenario (context)

    [<TestMethod>]
    member this.``Read execute returns existing and not found states`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

        let! foundResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let found =
            CosmosAssert.WantOk (foundResponse.Result, "Read should return ReadResult.Ok for existing item.")
        Assert.AreEqual (testItem.id, found.id, "Read should return created item.")
        Assert.AreEqual (HttpStatusCode.OK, foundResponse.HttpStatusCode, "Read should return HTTP 200 for existing item.")

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id $"{testItem.id}-missing"
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (missingResponse.Result, "Read should return ReadResult.NotFound for missing item.")
        Assert.AreEqual (HttpStatusCode.NotFound, missingResponse.HttpStatusCode, "Read missing should return HTTP 404.")
    }

    [<TestMethod>]
    member this.``Read execute returns NotModified when eTag matches current item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

        let! foundResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (foundResponse.Result, "Baseline read should succeed.")
        Assert.IsFalse (String.IsNullOrEmpty foundResponse.ETag, "Baseline read should return an ETag.")

        let! notModifiedResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    eTag foundResponse.ETag
                },
                this.CancellationToken
            )

        match notModifiedResponse.Result with
        | ReadResult.NotModified -> ()
        | ReadResult.Ok _ when notModifiedResponse.HttpStatusCode = HttpStatusCode.OK ->
            // The service ignored If-None-Match and returned the full document (observed on the Linux
            // vnext-preview emulator). NotModified cannot be produced without a 304 from the service, so this
            // environment cannot verify conditional reads; report that instead of passing or failing.
            Assert.Inconclusive (
                "The Cosmos DB endpoint ignored If-None-Match and returned HTTP 200, so conditional reads cannot be verified against it."
            )
        | result ->
            Assert.Fail (
                $"Expected ReadResult.NotModified for a matching eTag, got {result} (HTTP {int notModifiedResponse.HttpStatusCode})."
            )

        Assert.AreEqual (
            HttpStatusCode.NotModified,
            notModifiedResponse.HttpStatusCode,
            "Read with a matching eTag should report HTTP 304."
        )
    }

    [<TestMethod>]
    member this.``ExecuteAsyncOption returns Some for existing item and None for missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

        let! foundOption =
            container.ExecuteAsyncOption (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let found =
            Assert.WantSome (foundOption.Result, "ExecuteAsyncOption should return Some for an existing item.")
        Assert.AreEqual (testItem.id, found.id, "ExecuteAsyncOption should return the existing item.")

        let! missingOption =
            container.ExecuteAsyncOption (
                read {
                    id $"{testItem.id}-missing"
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        Assert.IsNone (missingOption.Result, "ExecuteAsyncOption should return None for a missing item.")
    }

    [<TestMethod>]
    member this.``ExecuteAsyncValueOption returns ValueSome for existing item and ValueNone for missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

        let! foundValueOption =
            container.ExecuteAsyncValueOption (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let found =
            Assert.WantValueSome (
                foundValueOption.Result,
                "ExecuteAsyncValueOption should return ValueSome for an existing item."
            )
        Assert.AreEqual (testItem.id, found.id, "ExecuteAsyncValueOption should return the existing item.")

        let! missingValueOption =
            container.ExecuteAsyncValueOption (
                read {
                    id $"{testItem.id}-missing"
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        Assert.IsValueNone (missingValueOption.Result, "ExecuteAsyncValueOption should return ValueNone for a missing item.")
    }

    [<TestMethod>]
    member this.``FeedIterator FirstAsync returns Ok for matching query and NotFound for empty query`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

        let matchingQuery =
            QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", testItem.id)
        let matchingIterator = container.GetItemQueryIterator<TestItem>(matchingQuery)
        let! matchingResponse = matchingIterator.FirstAsync (this.CancellationToken)

        let found =
            CosmosAssert.WantOk (matchingResponse.Result, "FirstAsync should return ReadResult.Ok for a matching query.")
        Assert.AreEqual (testItem.id, found.id, "FirstAsync should return the matching item.")

        let emptyQuery =
            QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", $"{testItem.id}-missing")
        let emptyIterator = container.GetItemQueryIterator<TestItem>(emptyQuery)
        let! emptyResponse = emptyIterator.FirstAsync (this.CancellationToken)

        CosmosAssert.IsNotFound (emptyResponse.Result, "FirstAsync should return ReadResult.NotFound for an empty query result.")
    }
