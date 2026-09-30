namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; ReadExtensionsTestCategory>]
type ReadExtensionsIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``CountAsync and LongCountAsync return seeded item counts`` () : Task = task {
        let! container = this.GetContainer ()
        let seededItems = [ this.NewItem "count-1"; this.NewItem "count-2"; this.NewItem "count-3" ]
        do! this.SeedItemsAsync (container, seededItems)

        let! countByPartition = container.CountAsync ("integration", cancellationToken = this.CancellationToken)
        let! countByQuery =
            container.CountAsync (QueryRequestOptions (), cancellationToken = this.CancellationToken)
        let! longCountByPartition =
            container.LongCountAsync (PartitionKey "integration", cancellationToken = this.CancellationToken)

        Assert.AreEqual (3, countByPartition, "CountAsync by partition should return seeded item count.")
        Assert.AreEqual (3, countByQuery, "CountAsync by query options should return seeded item count.")
        Assert.AreEqual (3L, longCountByPartition, "LongCountAsync should return seeded item count.")
    }

    [<TestMethod>]
    member this.``ExistsAsync returns expected values for partition key variants`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "exists"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! existsWithPartition =
            container.ExistsAsync (testItem.id, PartitionKey testItem.partitionKey, this.CancellationToken)

        let! existsWithoutPartition =
            container.ExistsAsync (testItem.id, cancellationToken = this.CancellationToken)

        let! missingExists =
            container.ExistsAsync ($"{testItem.id}-missing", cancellationToken = this.CancellationToken)

        Assert.IsTrue (existsWithPartition, "ExistsAsync with partition key should return true for existing item.")
        Assert.IsTrue (existsWithoutPartition, "ExistsAsync without partition key should return true for existing item.")
        Assert.IsFalse (missingExists, "ExistsAsync should return false for missing item.")
    }

    [<TestMethod>]
    member this.``ExistsAsync returns the result for its own id when called concurrently`` () : Task = task {
        let! container = this.GetContainer ()
        let seededItems = [| for i in 1..10 -> this.NewItem $"concurrent-exists-{i}" |]
        do! this.SeedItemsAsync (container, seededItems)

        // Interleave existing and missing ids so that a query sent with another call's id changes the result,
        // and repeat them to widen the window in which calls overlap
        let expectations = [|
            for _ in 1..10 do
                for item in seededItems do
                    struct (item.id, true)
                    struct ($"{item.id}-missing", false)
        |]

        // Task.Run so that the calls really overlap on different threads instead of starting one after another
        let! results =
            expectations
            |> Seq.map (fun struct (id, _) ->
                Task.Run<bool>(fun () -> container.ExistsAsync (id, cancellationToken = this.CancellationToken))
            )
            |> Task.WhenAll

        let mismatches = [|
            for struct (id, expected), actual in Array.zip expectations results do
                if expected <> actual then
                    $"{id}: expected {expected}, got {actual}"
        |]

        Assert.IsEmpty (mismatches, "Concurrent ExistsAsync calls should each check their own id.")
    }

    [<TestMethod>]
    member this.``ExistsAsync with partition key returns false for an item in another partition`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "exists-other-partition"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! exists =
            container.ExistsAsync (testItem.id, PartitionKey $"{testItem.partitionKey}-other", this.CancellationToken)

        Assert.IsFalse (exists, "ExistsAsync should return false when the item is in a different partition.")
    }

    /// <summary>
    /// Creates a container with a two-level hierarchical partition key, <c>/partitionKey</c> then <c>/subKey</c>.
    /// <see cref="TestItem"/> has no <c>subKey</c> field, so its full key ends with a None level, as in issue #31.
    /// </summary>
    member private this.GetHierarchicalContainer () : Task<Container> = task {
        let database =
            this.Application.Database
            |> ValueOption.defaultWith (fun () -> invalidOp "Database is not initialized.")
        let! response =
            database.CreateContainerIfNotExistsAsync (
                ContainerProperties ("hierarchical-tests", [| "/partitionKey"; "/subKey" |]),
                cancellationToken = this.CancellationToken
            )
        return response.Container
    }

    [<TestMethod>]
    member this.``ExistsAsync with a full hierarchical partition key checks the item by point read`` () : Task = task {
        let! container = this.GetHierarchicalContainer ()
        let testItem = this.NewItem "hierarchical-full"
        let fullKey = PartitionKeyBuilder().Add(testItem.partitionKey).AddNoneType().Build()
        let! _ = container.CreateItemAsync (testItem, fullKey, cancellationToken = this.CancellationToken)

        let! exists = container.ExistsAsync (testItem.id, fullKey, this.CancellationToken)
        let! missingExists = container.ExistsAsync ($"{testItem.id}-missing", fullKey, this.CancellationToken)

        Assert.IsTrue (exists, "ExistsAsync should return true for an existing item with a full hierarchical key.")
        Assert.IsFalse (missingExists, "ExistsAsync should return false for a missing item with a full hierarchical key.")
    }

    [<TestMethod>]
    member this.``ExistsAsync with a hierarchical partition key prefix matches the item beneath it`` () : Task = task {
        let! container = this.GetHierarchicalContainer ()
        let testItem = this.NewItem "hierarchical-prefix"
        let fullKey = PartitionKeyBuilder().Add(testItem.partitionKey).AddNoneType().Build()
        let! _ = container.CreateItemAsync (testItem, fullKey, cancellationToken = this.CancellationToken)

        let prefixKey = PartitionKeyBuilder().Add(testItem.partitionKey).Build()
        let! exists = container.ExistsAsync (testItem.id, prefixKey, this.CancellationToken)
        let! missingExists = container.ExistsAsync ($"{testItem.id}-missing", prefixKey, this.CancellationToken)

        Assert.IsTrue (exists, "ExistsAsync should return true for an existing item beneath a partition key prefix.")
        Assert.IsFalse (missingExists, "ExistsAsync should return false for a missing item beneath a partition key prefix.")
    }

    [<TestMethod>]
    member this.``ExistsAsync with partition key throws instead of returning false when the container does not exist`` () : Task =
        task {
            let! container = this.GetContainer ()
            let missingContainer = container.Database.GetContainer "missing-container"
            let testItem = this.NewItem "missing-container"

            // The service answers 404 here as well, with a sub-status that tells it apart from a missing item
            let! thrown =
                Assert.ThrowsExactlyAsync<CosmosException>(
                    Func<Task>(fun () -> task {
                        let! _ =
                            missingContainer.ExistsAsync (
                                testItem.id,
                                PartitionKey testItem.partitionKey,
                                this.CancellationToken
                            )
                        return ()
                    }),
                    "ExistsAsync should propagate a failure that is not a missing item."
                )

            Assert.AreEqual (HttpStatusCode.NotFound, thrown.StatusCode, "The propagated failure should keep its status code.")
        }

    [<TestMethod>]
    member this.``ExistsAsync and IsNotDeletedAsync match an id present in more than one partition`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "shared-id"
        let secondItem = { firstItem with partitionKey = "integration-2" }
        do! this.SeedItemsAsync (container, [ firstItem; secondItem ])

        let! existsAcrossPartitions =
            container.ExistsAsync (firstItem.id, cancellationToken = this.CancellationToken)

        Assert.IsTrue (
            existsAcrossPartitions,
            "ExistsAsync should match an id present in more than one partition when the query is not partition-scoped."
        )

        let! notDeletedAcrossPartitions = container.IsNotDeletedAsync "deletedAt" firstItem.id

        Assert.IsTrue (
            notDeletedAcrossPartitions,
            "IsNotDeletedAsync should match an id present in more than one partition when the query is not partition-scoped."
        )
    }

    [<TestMethod>]
    [<DataRow("deletedAt", DisplayName = "letters only")>]
    [<DataRow("_deletedAt", DisplayName = "starts with underscore")>]
    [<DataRow("deletedAt1", DisplayName = "digit after first character")>]
    [<DataRow("value", DisplayName = "reserved Cosmos SQL keyword")>]
    member this.``IsNotDeletedAsync evaluates valid deleted field name shapes in the query`` (deletedFieldName : string) : Task =
        task {
            let! container = this.GetContainer ()
            let testItem = this.NewItem "valid-field-name"
            do! this.SeedItemsAsync (container, [ testItem ])

            let! notDeletedBefore = container.IsNotDeletedAsync deletedFieldName testItem.id

            Assert.IsTrue (
                notDeletedBefore,
                $"IsNotDeletedAsync should return true before the '{deletedFieldName}' marker is set."
            )

            let! patchResponse =
                container.ExecuteOverwriteAsync (
                    patch {
                        id testItem.id
                        partitionKey testItem.partitionKey
                        operation (PatchOperation.Set ($"/{deletedFieldName}", true))
                    },
                    this.CancellationToken
                )

            CosmosAssert.IsOk (patchResponse.Result, $"Setting the '{deletedFieldName}' marker should succeed.")

            let! notDeletedAfter = container.IsNotDeletedAsync deletedFieldName testItem.id

            Assert.IsFalse (
                notDeletedAfter,
                $"IsNotDeletedAsync should return false once the '{deletedFieldName}' marker is true."
            )
        }

    [<TestMethod>]
    member this.``IsNotDeletedAsync returns true when deleted marker field is undefined`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "marker-undefined"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! notDeleted = container.IsNotDeletedAsync "deletedAt" testItem.id

        Assert.IsTrue (notDeleted, "IsNotDeletedAsync should return true when the deleted marker field is undefined.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync returns true when deleted marker field is null`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "marker-null"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", Unchecked.defaultof<obj>))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> ()
        | result -> Assert.Fail ($"Expected patch success setting null marker, got {result}.")

        let! notDeleted = container.IsNotDeletedAsync "deletedAt" testItem.id

        Assert.IsTrue (notDeleted, "IsNotDeletedAsync should return true when the deleted marker field is null.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync returns true when deleted marker field is false`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "marker-false"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", false))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> ()
        | result -> Assert.Fail ($"Expected patch success setting false marker, got {result}.")

        let! notDeleted = container.IsNotDeletedAsync "deletedAt" testItem.id

        Assert.IsTrue (notDeleted, "IsNotDeletedAsync should return true when the deleted marker field is explicitly false.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync returns false when deleted marker field is true`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "marker-true"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", true))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> ()
        | result -> Assert.Fail ($"Expected patch success setting true marker, got {result}.")

        let! notDeleted = container.IsNotDeletedAsync "deletedAt" testItem.id

        Assert.IsFalse (notDeleted, "IsNotDeletedAsync should return false when the deleted marker field is true.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync returns false when deleted marker field is a timestamp`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "marker-timestamp"
        do! this.SeedItemsAsync (container, [ testItem ])

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", "2026-05-24T00:00:00Z"))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> Assert.AreEqual (HttpStatusCode.OK, patchResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! notDeleted = container.IsNotDeletedAsync "deletedAt" testItem.id

        Assert.IsFalse (notDeleted, "IsNotDeletedAsync should return false when the deleted marker field is a timestamp.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync throws ArgumentNullException for a null deleted field name`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "null-deleted-field-name"

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentNullException>(
                Func<Task>(fun () -> task {
                    let! _ = container.IsNotDeletedAsync Unchecked.defaultof<string> testItem.id
                    return ()
                }),
                "IsNotDeletedAsync should throw ArgumentNullException when deleted field name is null."
            )

        return ()
    }

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow(" ", DisplayName = "whitespace")>]
    [<DataRow("1deletedAt", DisplayName = "starts with digit")>]
    [<DataRow("1deleted", DisplayName = "starts with digit, short name")>]
    [<DataRow("deleted-at", DisplayName = "contains hyphen")>]
    [<DataRow("deleted-field", DisplayName = "contains hyphen, another field name")>]
    [<DataRow("deleted.field", DisplayName = "contains dot")>]
    [<DataRow("deleted field", DisplayName = "contains space")>]
    member this.``IsNotDeletedAsync throws ArgumentException for malformed deleted field names``
        (deletedFieldName : string)
        : Task
        = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "malformed-deleted-field-name"

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task {
                    let! _ = container.IsNotDeletedAsync deletedFieldName testItem.id
                    return ()
                }),
                $"IsNotDeletedAsync should throw ArgumentException for deleted field name '{deletedFieldName}'."
            )

        return ()
    }
