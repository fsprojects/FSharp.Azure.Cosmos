namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System
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
    member this.``IsNotDeletedAsync throws for null or malformed deleted field names`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "invalid-deleted-field-name"

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentNullException>(
                Func<Task>(fun () -> task {
                    let! _ = container.IsNotDeletedAsync Unchecked.defaultof<string> testItem.id
                    return ()
                }),
                "IsNotDeletedAsync should throw ArgumentNullException when deleted field name is null."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task {
                    let! _ = container.IsNotDeletedAsync "1invalid" testItem.id
                    return ()
                }),
                "IsNotDeletedAsync should throw ArgumentException for a malformed deleted field name."
            )

        return ()
    }
