namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; ReplaceTestCategory>]
type ReplaceOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Replace execute overwrite replaces existing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let replacement = { testItem with name = "item-replaced"; quantity = 3 }

        let! replaceResponse =
            container.ExecuteOverwriteAsync (
                replace {
                    id replacement.id
                    item replacement
                    partitionKey replacement.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (replaceResponse.Result, "Replace should return ReplaceResult.Ok.")
        Assert.AreEqual (HttpStatusCode.OK, replaceResponse.HttpStatusCode, "Replace should return HTTP 200.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id replacement.id
                    partitionKey replacement.partitionKey
                },
                this.CancellationToken
            )

        let persisted = CosmosAssert.WantOk (readResponse.Result, "Replaced item should be readable.")
        Assert.AreEqual (replacement.name, persisted.name, "Replace should persist replacement name.")
        Assert.AreEqual (replacement.quantity, persisted.quantity, "Replace should persist replacement quantity.")
    }

    [<TestMethod>]
    member this.``ReplaceAndRead execute overwrite returns replaced item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace-and-read"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let replacement = { testItem with name = "item-replaced-and-read"; quantity = 6 }

        let! replaceResponse =
            container.ExecuteOverwriteAsync (
                replaceAndRead {
                    id replacement.id
                    item replacement
                    partitionKey replacement.partitionKey
                },
                this.CancellationToken
            )

        let replaced =
            CosmosAssert.WantOk (replaceResponse.Result, "ReplaceAndRead should return ReplaceResult.Ok.")
        Assert.AreEqual (replacement.name, replaced.name, "ReplaceAndRead should return replacement name.")
        Assert.AreEqual (replacement.quantity, replaced.quantity, "ReplaceAndRead should return replacement quantity.")
        Assert.AreEqual (HttpStatusCode.OK, replaceResponse.HttpStatusCode, "ReplaceAndRead should return HTTP 200.")
    }

    [<TestMethod>]
    member this.``Replace concurrently retries and applies update`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "replace-concurrent"

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

        let operation = replaceConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun current -> async {
                if not conflictInjected then
                    conflictInjected <- true

                    let competingUpdate = { current with name = "competing-update" }

                    let! competingResponse =
                        container.ExecuteOverwriteAsync (
                            replace {
                                id competingUpdate.id
                                item competingUpdate
                                partitionKey competingUpdate.partitionKey
                            },
                            this.CancellationToken
                        )
                        |> Async.AwaitTask

                    CosmosAssert.IsOk (
                        competingResponse.Result,
                        "Competing replace should succeed so the retried replace observes a stale ETag."
                    )

                return
                    Result.Ok {
                        current with
                            name = "replace-concurrent-updated"
                            quantity = current.quantity + 10
                    }
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        match concurrentResponse.Result with
        | ReplaceConcurrentResult.Ok updated ->
            Assert.IsTrue (conflictInjected, "Replace concurrently test should inject a conflicting update at least once.")
            Assert.AreEqual ("replace-concurrent-updated", updated.name, "Replace concurrently should persist updated name.")
            Assert.AreEqual (original.quantity + 10, updated.quantity, "Replace concurrently should persist updated quantity.")
        | result -> Assert.Fail ($"Expected replace concurrently success after retry, got {result}.")
    }

    [<TestMethod>]
    member this.``Replace execute overwrite returns NotFound for a missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace-missing"

        let! replaceResponse =
            container.ExecuteOverwriteAsync (
                replace {
                    id testItem.id
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (replaceResponse.Result, "Replace of a never-created item should return ReplaceResult.NotFound.")
        Assert.AreEqual (
            HttpStatusCode.NotFound,
            replaceResponse.HttpStatusCode,
            "Replace of a missing item should return HTTP 404."
        )
    }

    [<TestMethod>]
    member this.``Replace execute requires an ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace-requires-etag"

        let invoke () =
            Func<Task>(fun () -> task {
                let! _ =
                    container.ExecuteAsync (
                        replace {
                            id testItem.id
                            item testItem
                            partitionKey testItem.partitionKey
                        },
                        this.CancellationToken
                    )

                return ()
            })

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                invoke (),
                "Replace safe execute should throw ArgumentException when no eTag is set."
            )

        return ()
    }

    [<TestMethod>]
    member this.``Replace execute succeeds when the ETag matches`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace-matching-etag"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let replacement = { testItem with name = "item-replace-matching-etag"; quantity = 4 }

        let! replaceResponse =
            container.ExecuteAsync (
                replace {
                    id replacement.id
                    item replacement
                    partitionKey replacement.partitionKey
                    eTag createResponse.ETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (replaceResponse.Result, "Safe replace with a matching eTag should return ReplaceResult.Ok.")
        Assert.AreEqual (
            HttpStatusCode.OK,
            replaceResponse.HttpStatusCode,
            "Safe replace with a matching eTag should return HTTP 200."
        )
    }

    [<TestMethod>]
    member this.``Replace execute returns ModifiedBefore for a stale ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "replace-stale-etag"

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
                replace {
                    id testItem.id
                    item { testItem with name = "item-replace-stale-etag-changed"; quantity = 2 }
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (overwriteResponse.Result, "Overwrite that changes the ETag should succeed.")

        let! staleReplaceResponse =
            container.ExecuteAsync (
                replace {
                    id testItem.id
                    item { testItem with name = "item-replace-stale-etag-final"; quantity = 3 }
                    partitionKey testItem.partitionKey
                    eTag staleETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsModifiedBefore (
            staleReplaceResponse.Result,
            "Safe replace with a stale eTag should return ReplaceResult.ModifiedBefore."
        )
        Assert.AreEqual (
            HttpStatusCode.PreconditionFailed,
            staleReplaceResponse.HttpStatusCode,
            "Safe replace with a stale eTag should return HTTP 412."
        )
    }

    [<TestMethod>]
    member this.``Replace concurrently returns CustomError when update reports an error`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "replace-concurrent-custom-error"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = replaceConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun _ -> async { return Result.Error "update rejected" })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        let customError =
            CosmosAssert.WantCustomError (
                concurrentResponse.Result,
                "Replace concurrently should return ReplaceConcurrentResult.CustomError when update reports an error."
            )
        Assert.AreEqual ("update rejected", customError, "Replace concurrently CustomError should carry the reported error.")
    }

    [<TestMethod>]
    member this.``Replace concurrently returns ModifiedBefore when retries are exhausted`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "replace-concurrent-exhausted"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = replaceConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            update (fun current -> async {
                // Always inject a competing write first, so the single allowed attempt
                // (maxRetryCount = 1) always observes a stale ETag and exhausts immediately.
                let competingUpdate = { current with name = "competing-exhaustion-update" }

                let! _ =
                    container.ExecuteOverwriteAsync (
                        replace {
                            id competingUpdate.id
                            item competingUpdate
                            partitionKey competingUpdate.partitionKey
                        },
                        this.CancellationToken
                    )
                    |> Async.AwaitTask

                return Result.Ok { current with name = "should-not-be-persisted" }
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 1, this.CancellationToken)

        CosmosAssert.IsModifiedBefore (
            concurrentResponse.Result,
            "Replace concurrently should return ReplaceConcurrentResult.ModifiedBefore once retries are exhausted."
        )
    }
