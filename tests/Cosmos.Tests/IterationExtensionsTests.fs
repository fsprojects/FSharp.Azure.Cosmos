namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Threading.Tasks
open FSharp.Control
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; IterationExtensionsTestCategory>]
type IterationExtensionsIntegrationTests () =
    inherit OperationTestBase<MultipleItemsScenario> ()

    override _.CreateApplication context = MultipleItemsScenario (context)

    [<TestMethod>]
    member this.``FeedIterator AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem, secondItem =
            match this.Application.SeededItems with
            | [ firstItem; secondItem ] -> firstItem, secondItem
            | seededItems -> failwith $"Expected exactly two seeded items but got {seededItems.Length}."

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
            iteratedItems |> List.map _.id |> Array.ofList,
            "FeedIterator.AsAsyncEnumerable should iterate seeded items without duplicates or omissions."
        )
    }

    [<TestMethod>]
    member this.``IQueryable AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem, secondItem =
            match this.Application.SeededItems with
            | [ firstItem; secondItem ] -> firstItem, secondItem
            | seededItems -> failwith $"Expected exactly two seeded items but got {seededItems.Length}."

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
            iteratedItems |> List.map _.id |> Array.ofList,
            "IQueryable.AsAsyncEnumerable should iterate seeded items without duplicates or omissions."
        )
    }
