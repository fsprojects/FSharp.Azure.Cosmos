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

    [<TestMethod>]
    member this.``Patch concurrently retries and applies update`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "patch-concurrent"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let mutable conflictInjected = false

        let operation = patchConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun current -> task {
                if not conflictInjected then
                    conflictInjected <- true

                    let competingUpdate = { current with name = "competing-patch" }

                    let! competingResponse =
                        container.ExecuteOverwriteAsync (
                            patch {
                                id competingUpdate.id
                                partitionKey competingUpdate.partitionKey
                                operation (PatchOperation.Replace ("/name", competingUpdate.name))
                            },
                            this.CancellationToken
                        )

                    CosmosAssert.IsOk (
                        competingResponse.Result,
                        "Competing patch should succeed so the retried patch observes a stale ETag."
                    )

                return
                    Result.Ok [
                        PatchOperation.Replace ("/name", "patch-concurrent-updated")
                        PatchOperation.Replace ("/quantity", current.quantity + 10)
                    ]
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        // Two attempts ran with two different eTags; neither may leak into the caller-owned options.
        Assert.IsNull (
            operation.RequestOptions.IfMatchEtag,
            "Patch concurrently must not write the per-attempt eTag back into the caller's request options."
        )

        match concurrentResponse.Result with
        | PatchConcurrentResult.Ok _ ->
            Assert.IsTrue (conflictInjected, "Patch concurrently test should inject a conflicting update at least once.")

            let! readResponse =
                container.ExecuteAsync (
                    read {
                        id original.id
                        partitionKey original.partitionKey
                    },
                    this.CancellationToken
                )

            let persisted = CosmosAssert.WantOk (readResponse.Result, "Patched item should be readable.")
            Assert.AreEqual ("patch-concurrent-updated", persisted.name, "Patch concurrently should persist updated name.")
            Assert.AreEqual (original.quantity + 10, persisted.quantity, "Patch concurrently should persist updated quantity.")
        | result -> Assert.Fail ($"Expected patch concurrently success after retry, got {result}.")
    }

    [<TestMethod>]
    member this.``PatchAndRead concurrently returns patched item`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "patch-concurrent-and-read"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = patchConcurrenlyAndRead<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun current -> task {
                return
                    Result.Ok [
                        PatchOperation.Replace ("/name", "patch-concurrent-and-read-updated")
                        PatchOperation.Replace ("/quantity", current.quantity + 5)
                    ]
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        match concurrentResponse.Result with
        | PatchConcurrentResult.Ok updated ->
            Assert.AreEqual (
                "patch-concurrent-and-read-updated",
                updated.name,
                "PatchAndRead concurrently should return updated name."
            )
            Assert.AreEqual (original.quantity + 5, updated.quantity, "PatchAndRead concurrently should return updated quantity.")
            Assert.AreEqual (
                HttpStatusCode.OK,
                concurrentResponse.HttpStatusCode,
                "PatchAndRead concurrently should return HTTP 200."
            )
        | result -> Assert.Fail ($"Expected patchAndRead concurrently success, got {result}.")
    }

    [<TestMethod>]
    member this.``Patch concurrently returns CustomError when update reports an error`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "patch-concurrent-custom-error"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = patchConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun _ -> task { return Result.Error "update rejected" })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        let customError =
            CosmosAssert.WantCustomError (
                concurrentResponse.Result,
                "Patch concurrently should return PatchConcurrentResult.CustomError when update reports an error."
            )

        Assert.AreEqual ("update rejected", customError, "Patch concurrently CustomError should carry the reported error.")
    }

    [<TestMethod>]
    member this.``Patch concurrently returns ModifiedBefore when retries are exhausted`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "patch-concurrent-exhausted"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = patchConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun current -> task {
                // Always inject a competing write first, so the single allowed attempt
                // (maxRetryCount = 1) always observes a stale ETag and exhausts immediately.
                let competingUpdate = { current with name = "competing-exhaustion-patch" }

                let! competingResponse =
                    container.ExecuteOverwriteAsync (
                        patch {
                            id competingUpdate.id
                            partitionKey competingUpdate.partitionKey
                            operation (PatchOperation.Replace ("/name", competingUpdate.name))
                        },
                        this.CancellationToken
                    )

                CosmosAssert.IsOk (
                    competingResponse.Result,
                    "Competing patch should succeed so every attempt observes a stale ETag."
                )

                return Result.Ok [ PatchOperation.Replace ("/name", "should-not-be-persisted") ]
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 1, this.CancellationToken)

        CosmosAssert.IsModifiedBefore (
            concurrentResponse.Result,
            "Patch concurrently should return PatchConcurrentResult.ModifiedBefore once retries are exhausted."
        )
    }

    [<TestMethod>]
    member this.``Patch concurrently returns NotFound for a missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-concurrent-missing"

        let operation = patchConcurrenly<TestItem, string> {
            id testItem.id
            partitionKey testItem.partitionKey
            update (fun _ -> task { return Result.Error "should not be called" })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        CosmosAssert.IsNotFound (
            concurrentResponse.Result,
            "Patch concurrently of a never-created item should return PatchConcurrentResult.NotFound."
        )
        Assert.AreEqual (
            HttpStatusCode.NotFound,
            concurrentResponse.HttpStatusCode,
            "Patch concurrently of a missing item should return HTTP 404."
        )
    }

    [<TestMethod>]
    member this.``Patch concurrently rejects a non-positive retry count`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-concurrent-invalid-retry-count"

        let operation = patchConcurrenly<TestItem, string> {
            id testItem.id
            partitionKey testItem.partitionKey
            update (fun _ -> task { return Result.Error "should not be called" })
        }

        for maxRetryCount in [| 0; -1 |] do
            let invoke () =
                Func<Task>(fun () -> task {
                    let! _ = container.ExecuteConcurrentlyAsync (operation, maxRetryCount, this.CancellationToken)
                    return ()
                })

            let! _ =
                Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                    invoke (),
                    $"Patch concurrently should throw ArgumentOutOfRangeException for maxRetryCount = %i{maxRetryCount}."
                )

            ()
    }
