namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; TestCategory(TestCategories.ReadExtensions)>]
type CosmosReadExtensionsIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``CountAsync and LongCountAsync return seeded item counts`` () : Task = task {
        let! container = this.GetContainer ()
        let seededItems = [ this.NewItem "count-1"; this.NewItem "count-2"; this.NewItem "count-3" ]
        do! this.SeedItemsAsync (container, seededItems)

        let! countByPartition = container.CountAsync ("integration", cancellationToken = this.CancellationToken)
        let! countByQuery = container.CountAsync (QueryRequestOptions (), cancellationToken = this.CancellationToken)
        let! longCountByPartition =
            container.LongCountAsync (PartitionKey "integration", cancellationToken = this.CancellationToken)

        Assert.IsTrue ((countByPartition = 3), "CountAsync by partition should return seeded item count.")
        Assert.IsTrue ((countByQuery = 3), "CountAsync by query options should return seeded item count.")
        Assert.IsTrue ((longCountByPartition = 3L), "LongCountAsync should return seeded item count.")
    }

    [<TestMethod>]
    member this.``ExistsAsync and IsNotDeletedAsync return expected values`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "exists-1"
        let secondItem = this.NewItem "exists-2"
        do! this.SeedItemsAsync (container, [ firstItem; secondItem ])

        let! existsWithPartition =
            container.ExistsAsync (firstItem.id, PartitionKey firstItem.partitionKey, this.CancellationToken)

        let! existsWithoutPartition = container.ExistsAsync (firstItem.id, cancellationToken = this.CancellationToken)

        let! missingExists = container.ExistsAsync ($"{firstItem.id}-missing", cancellationToken = this.CancellationToken)

        Assert.IsTrue (existsWithPartition, "ExistsAsync with partition key should return true for existing item.")
        Assert.IsTrue (existsWithoutPartition, "ExistsAsync without partition key should return true for existing item.")
        Assert.IsFalse (missingExists, "ExistsAsync should return false for missing item.")

        let! notDeletedBeforePatch = container.IsNotDeletedAsync "deletedAt" secondItem.id

        Assert.IsTrue (notDeletedBeforePatch, "IsNotDeletedAsync should return true before deleted marker is set.")

        let! notDeletedWithUnderscoreFieldName = container.IsNotDeletedAsync "_deletedAt" secondItem.id

        Assert.IsTrue (
            notDeletedWithUnderscoreFieldName,
            "IsNotDeletedAsync should support deleted field names starting with underscore."
        )

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id secondItem.id
                    partitionKey secondItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", "2026-05-24T00:00:00Z"))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> Assert.IsTrue (patchResponse.HttpStatusCode = HttpStatusCode.OK, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! notDeletedAfterPatch = container.IsNotDeletedAsync "deletedAt" secondItem.id

        Assert.IsFalse (notDeletedAfterPatch, "IsNotDeletedAsync should return false after deleted marker is set.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync throws for invalid deleted field names`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "invalid-deleted-field-name"
        let invokeIsNotDeleted (deletedFieldName : string | null) =
            Func<Task> (fun () ->
                task {
                    let! _ = container.IsNotDeletedAsync deletedFieldName testItem.id
                    return ()
                }
                :> Task
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentNullException> (
                invokeIsNotDeleted null,
                "IsNotDeletedAsync should throw ArgumentNullException when deleted field name is null."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException> (
                invokeIsNotDeleted " ",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name is whitespace."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException> (
                invokeIsNotDeleted "1deletedAt",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name starts with a digit."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException> (
                invokeIsNotDeleted "deleted-at",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name has unsupported characters."
            )

        return ()
    }
