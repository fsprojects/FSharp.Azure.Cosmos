namespace Tests.Integration

open System.Threading.Tasks
open FSharp.Control
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type IterationExtensionsIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``FeedIterator AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "iterator-1"
        let secondItem = this.NewItem "iterator-2"
        do! this.SeedItemsAsync (container, [ firstItem; secondItem ])

        let query =
            QueryDefinition("SELECT * FROM c WHERE c.partitionKey = @partitionKey").WithParameter ("@partitionKey", "integration")

        let iterator = container.GetItemQueryIterator<TestItem> (query)
        let expectedIds = set [ firstItem.id; secondItem.id ]
        let! iteratedItems =
            iterator.AsAsyncEnumerable<TestItem> (this.CancellationToken)
            |> TaskSeq.toListAsync
        let foundCount =
            iteratedItems
            |> List.filter (fun item -> expectedIds.Contains item.id)
            |> List.length
        Assert.IsTrue ((foundCount = 2), "FeedIterator.AsAsyncEnumerable should iterate seeded items.")
    }

    [<TestMethod>]
    member this.``IQueryable AsAsyncEnumerable iterates seeded items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "queryable-1"
        let secondItem = this.NewItem "queryable-2"
        do! this.SeedItemsAsync (container, [ firstItem; secondItem ])

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
            |> List.filter (fun item -> expectedIds.Contains item.id)
            |> List.length
        Assert.IsTrue ((foundCount = 2), "IQueryable.AsAsyncEnumerable should iterate seeded items.")
    }
