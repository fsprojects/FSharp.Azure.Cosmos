namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; PatchTestCategory>]
type PatchOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Patch execute overwrite updates item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let patchedName = "item-patched"
        let patchedQuantity = 9

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", patchedName))
                    operation (PatchOperation.Replace ("/quantity", patchedQuantity))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> Assert.AreEqual (HttpStatusCode.OK, patchResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let persisted = CosmosAssert.WantOk (readResponse.Result, "Patched item should be readable.")
        Assert.AreEqual (patchedName, persisted.name, "Patch should persist patched name.")
        Assert.AreEqual (patchedQuantity, persisted.quantity, "Patch should persist patched quantity.")
    }

    [<TestMethod>]
    member this.``PatchAndRead execute overwrite returns updated item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-and-read"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let patchedName = "item-patched-and-read"
        let patchedQuantity = 11

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patchAndRead {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", patchedName))
                    operation (PatchOperation.Replace ("/quantity", patchedQuantity))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok patched ->
            Assert.AreEqual (patchedName, patched.name, "PatchAndRead should return patched name.")
            Assert.AreEqual (patchedQuantity, patched.quantity, "PatchAndRead should return patched quantity.")
            Assert.AreEqual (HttpStatusCode.OK, patchResponse.HttpStatusCode, "PatchAndRead should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patchAndRead success, got {result}.")
    }

    [<TestMethod>]
    member this.``Patch execute overwrite returns NotFound for a missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-missing"

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", "irrelevant"))
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (patchResponse.Result, "Patch of a never-created item should return PatchResult.NotFound.")
        Assert.AreEqual (HttpStatusCode.NotFound, patchResponse.HttpStatusCode, "Patch of a missing item should return HTTP 404.")
    }

    [<TestMethod>]
    member this.``Patch execute requires an ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-requires-etag"

        let invoke () =
            Func<Task>(fun () -> task {
                let! _ =
                    container.ExecuteAsync (
                        patch {
                            id testItem.id
                            partitionKey testItem.partitionKey
                            operation (PatchOperation.Replace ("/name", "irrelevant"))
                        },
                        this.CancellationToken
                    )

                return ()
            })

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invoke (),
                "Patch safe execute should throw ArgumentException when no eTag is set."
            )

        return ()
    }

    [<TestMethod>]
    member this.``Patch execute succeeds when the ETag matches`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-matching-etag"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let! patchResponse =
            container.ExecuteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", "item-patch-matching-etag"))
                    eTag createResponse.ETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (patchResponse.Result, "Safe patch with a matching eTag should return PatchResult.Ok.")
        Assert.AreEqual (
            HttpStatusCode.OK,
            patchResponse.HttpStatusCode,
            "Safe patch with a matching eTag should return HTTP 200."
        )
    }

    [<TestMethod>]
    member this.``Patch execute returns ModifiedBefore for a stale ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-stale-etag"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")
        let staleETag = createResponse.ETag

        let! overwriteResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", "item-patch-stale-etag-changed"))
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (overwriteResponse.Result, "Overwrite that changes the ETag should succeed.")

        let! stalePatchResponse =
            container.ExecuteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", "item-patch-stale-etag-final"))
                    eTag staleETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsModifiedBefore (
            stalePatchResponse.Result,
            "Safe patch with a stale eTag should return PatchResult.ModifiedBefore."
        )
        Assert.AreEqual (
            HttpStatusCode.PreconditionFailed,
            stalePatchResponse.HttpStatusCode,
            "Safe patch with a stale eTag should return HTTP 412."
        )
    }
