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
    member this.``ExistsAsync and IsNotDeletedAsync return expected values`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "exists-1"
        let secondItem = this.NewItem "exists-2"
        do! this.SeedItemsAsync (container, [ firstItem; secondItem ])

        let! existsWithPartition =
            container.ExistsAsync (firstItem.id, PartitionKey firstItem.partitionKey, this.CancellationToken)

        let! existsWithoutPartition =
            container.ExistsAsync (firstItem.id, cancellationToken = this.CancellationToken)

        let! missingExists =
            container.ExistsAsync ($"{firstItem.id}-missing", cancellationToken = this.CancellationToken)

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

        let! notDeletedWithDigit = container.IsNotDeletedAsync "deletedAt1" secondItem.id

        Assert.IsTrue (
            notDeletedWithDigit,
            "IsNotDeletedAsync should support deleted field names with digits after the first character."
        )

        let! digitFieldPatchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id firstItem.id
                    partitionKey firstItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt1", true))
                },
                this.CancellationToken
            )

        match digitFieldPatchResponse.Result with
        | PatchResult.Ok _ ->
            Assert.AreEqual (HttpStatusCode.OK, digitFieldPatchResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success for digit-field marker, got {result}.")

        let! notDeletedWithDigitAfterPatch = container.IsNotDeletedAsync "deletedAt1" firstItem.id

        Assert.IsFalse (
            notDeletedWithDigitAfterPatch,
            "IsNotDeletedAsync should return false when a digit-containing deleted marker field is true."
        )

        let! patchFalseResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id secondItem.id
                    partitionKey secondItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", false))
                },
                this.CancellationToken
            )

        match patchFalseResponse.Result with
        | PatchResult.Ok _ ->
            Assert.AreEqual (HttpStatusCode.OK, patchFalseResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! notDeletedAfterFalsePatch = container.IsNotDeletedAsync "deletedAt" secondItem.id

        Assert.IsTrue (notDeletedAfterFalsePatch, "IsNotDeletedAsync should return true when deleted marker is false.")

        let! patchTrueResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id secondItem.id
                    partitionKey secondItem.partitionKey
                    operation (PatchOperation.Set ("/deletedAt", true))
                },
                this.CancellationToken
            )

        match patchTrueResponse.Result with
        | PatchResult.Ok _ ->
            Assert.AreEqual (HttpStatusCode.OK, patchTrueResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! notDeletedAfterTruePatch = container.IsNotDeletedAsync "deletedAt" secondItem.id

        Assert.IsFalse (notDeletedAfterTruePatch, "IsNotDeletedAsync should return false after deleted marker is true.")
    }

    [<TestMethod>]
    member this.``IsNotDeletedAsync throws for invalid deleted field names`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "invalid-deleted-field-name"
        let invokeIsNotDeleted (deletedFieldName : string) =
            Func<Task>(fun () -> task {
                let! _ = container.IsNotDeletedAsync deletedFieldName testItem.id
                return ()
            })

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
                invokeIsNotDeleted "",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name is empty."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invokeIsNotDeleted " ",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name is whitespace."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invokeIsNotDeleted "1deletedAt",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name starts with a digit."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invokeIsNotDeleted "deleted-at",
                "IsNotDeletedAsync should throw ArgumentException when deleted field name has unsupported characters."
            )

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invokeIsNotDeleted "deleted-field",
                "IsNotDeletedAsync should throw ArgumentException for field names with hyphens."
            )

        return ()
    }
