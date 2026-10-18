namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Threading.Tasks
open FSharp.Control
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration.MultipleItemsScenario
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; IterationExtensionsTestCategory>]
type IterationExtensionsIntegrationTests () =
    inherit OperationTestBase<MultipleItemsScenario.TestApplicationFactory> ()

    override _.CreateApplication context = MultipleItemsScenario.TestApplicationFactory (context)

    [<TestMethod>]
    member this.``FeedIterator AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let { FirstSeededItem = firstItem; SecondSeededItem = secondItem } = this.Application.TestData

        let query =
            QueryDefinition("SELECT * FROM c WHERE c.partitionKey = @partitionKey").WithParameter("@partitionKey", "integration")

        let iterator = container.GetItemQueryIterator<TestItem>(query)
        let expectedIds = [| firstItem.id; secondItem.id |]
        let! iteratedItems =
            iterator.AsAsyncEnumerable<TestItem>(this.CancellationToken)
            |> TaskSeq.toListAsync

        Assert.HasCount (2, iteratedItems, "FeedIterator.AsAsyncEnumerable should return exactly the seeded items.")
        CollectionAssert.AreEquivalent (
            expectedIds,
            iteratedItems |> List.map _.id |> List.toArray,
            "FeedIterator.AsAsyncEnumerable should iterate seeded items without duplicates or omissions."
        )
    }

    [<TestMethod>]
    member this.``IQueryable AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let { FirstSeededItem = firstItem; SecondSeededItem = secondItem } = this.Application.TestData

        let queryable =
            container.GetItemLinqQueryable<TestItem>(
                requestOptions = QueryRequestOptions (PartitionKey = PartitionKey "integration")
            )

        let expectedIds = [| firstItem.id; secondItem.id |]
        let! iteratedItems =
            queryable.AsAsyncEnumerable<TestItem>(this.CancellationToken)
            |> TaskSeq.toListAsync

        Assert.HasCount (2, iteratedItems, "IQueryable.AsAsyncEnumerable should return exactly the seeded items.")
        CollectionAssert.AreEquivalent (
            expectedIds,
            iteratedItems |> List.map _.id |> List.toArray,
            "IQueryable.AsAsyncEnumerable should iterate seeded items without duplicates or omissions."
        )
    }
