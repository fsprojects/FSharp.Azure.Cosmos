namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Threading.Tasks
open FSharp.Control
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; TestCategory(TestCategories.IterationExtensions)>]
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
            QueryDefinition("SELECT * FROM c WHERE c.partitionKey = @partitionKey").WithParameter ("@partitionKey", "integration")

        let iterator = container.GetItemQueryIterator<TestItem> (query)
        let expectedIds = set [ firstItem.id; secondItem.id ]
        let! iteratedItems =
            iterator.AsAsyncEnumerable<TestItem> (this.CancellationToken)
            |> TaskSeq.toListAsync
        let foundCount =
            iteratedItems
            |> Seq.filter (fun item -> expectedIds.Contains item.id)
            |> Seq.length
        Assert.IsTrue ((foundCount = 2), "FeedIterator.AsAsyncEnumerable should iterate seeded items.")
    }

    [<TestMethod>]
    member this.``IQueryable AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem, secondItem =
            match this.Application.SeededItems with
            | [ firstItem; secondItem ] -> firstItem, secondItem
            | seededItems -> failwith $"Expected exactly two seeded items but got {seededItems.Length}."

        let queryable =
            container.GetItemLinqQueryable<TestItem> (
                requestOptions = QueryRequestOptions (PartitionKey = PartitionKey "integration")
            )

        let expectedIds = set [ firstItem.id; secondItem.id ]
        let! iteratedItems =
            queryable.AsAsyncEnumerable<TestItem> (this.CancellationToken)
            |> TaskSeq.toListAsync
        let foundCount =
            iteratedItems
            |> Seq.filter (fun item -> expectedIds.Contains item.id)
            |> Seq.length
        Assert.IsTrue ((foundCount = 2), "IQueryable.AsAsyncEnumerable should iterate seeded items.")
    }
