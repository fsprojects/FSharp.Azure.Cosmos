namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; UpsertTestCategory>]
type UpsertOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Upsert execute overwrite creates then updates item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert"

        let! createResult =
            container.ExecuteOverwriteAsync (
                upsert {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match createResult.Result with
        | UpsertResult.Ok _ ->
            Assert.AreEqual (HttpStatusCode.Created, createResult.HttpStatusCode, "First upsert should create item (HTTP 201).")
        | result -> Assert.Fail ($"Expected first upsert success, got {result}.")

        let updated = { testItem with name = "item-upsert-updated"; quantity = 5 }

        let! updateResult =
            container.ExecuteOverwriteAsync (
                upsert {
                    item updated
                    partitionKey updated.partitionKey
                },
                this.CancellationToken
            )

        match updateResult.Result with
        | UpsertResult.Ok _ ->
            Assert.AreEqual (HttpStatusCode.OK, updateResult.HttpStatusCode, "Second upsert should update item (HTTP 200).")
        | result -> Assert.Fail ($"Expected second upsert success, got {result}.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id updated.id
                    partitionKey updated.partitionKey
                },
                this.CancellationToken
            )

        let persisted = CosmosAssert.WantOk (readResponse.Result, "Updated upsert item should be readable.")
        Assert.AreEqual (updated.name, persisted.name, "Upsert should persist updated name.")
        Assert.AreEqual (updated.quantity, persisted.quantity, "Upsert should persist updated quantity.")
    }

    [<TestMethod>]
    member this.``UpsertAndRead execute overwrite returns updated item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert-and-read"

        let! createdResponse =
            container.ExecuteOverwriteAsync (
                upsertAndRead {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match createdResponse.Result with
        | UpsertResult.Ok created ->
            Assert.AreEqual (testItem.name, created.name, "UpsertAndRead create should return created resource.")
            Assert.AreEqual (
                HttpStatusCode.Created,
                createdResponse.HttpStatusCode,
                "UpsertAndRead create should return HTTP 201."
            )
        | result -> Assert.Fail ($"Expected upsertAndRead create success, got {result}.")

        let updated = { testItem with name = "item-upsert-and-read-updated"; quantity = 9 }

        let! updatedResponse =
            container.ExecuteOverwriteAsync (
                upsertAndRead {
                    item updated
                    partitionKey updated.partitionKey
                },
                this.CancellationToken
            )

        match updatedResponse.Result with
        | UpsertResult.Ok upserted ->
            Assert.AreEqual (updated.name, upserted.name, "UpsertAndRead update should return updated name.")
            Assert.AreEqual (updated.quantity, upserted.quantity, "UpsertAndRead update should return updated quantity.")
            Assert.AreEqual (HttpStatusCode.OK, updatedResponse.HttpStatusCode, "UpsertAndRead update should return HTTP 200.")
        | result -> Assert.Fail ($"Expected upsertAndRead update success, got {result}.")
    }

    [<TestMethod>]
    member this.``Upsert concurrently retries and applies update`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "upsert-concurrent"

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

        let operation = upsertConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            updateOrCreate (fun maybeCurrent -> async {
                match maybeCurrent with
                | Some current ->
                    if not conflictInjected then
                        conflictInjected <- true

                        let competingUpdate = { current with name = "competing-upsert-update" }

                        let! _ =
                            container.ExecuteOverwriteAsync (
                                upsert {
                                    item competingUpdate
                                    partitionKey competingUpdate.partitionKey
                                },
                                this.CancellationToken
                            )
                            |> Async.AwaitTask

                        ()

                    return
                        Result.Ok {
                            current with
                                name = "upsert-concurrent-updated"
                                quantity = current.quantity + 7
                        }
                | None -> return Result.Error "Expected existing item for concurrent upsert test."
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        match concurrentResponse.Result with
        | UpsertConcurrentResult.Ok updated ->
            Assert.IsTrue (conflictInjected, "Upsert concurrently test should inject a conflicting update at least once.")
            Assert.AreEqual ("upsert-concurrent-updated", updated.name, "Upsert concurrently should persist updated name.")
            Assert.AreEqual (original.quantity + 7, updated.quantity, "Upsert concurrently should persist updated quantity.")
        | result -> Assert.Fail ($"Expected upsert concurrently success after retry, got {result}.")
    }

    [<TestMethod>]
    member this.``Upsert execute requires an ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert-requires-etag"

        let invoke () =
            Func<Task> (fun () -> task {
                let! _ =
                    container.ExecuteAsync (
                        upsert {
                            item testItem
                            partitionKey testItem.partitionKey
                        },
                        this.CancellationToken
                    )

                return ()
            })

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentException> (
                invoke (),
                "Upsert safe execute should throw ArgumentException when no eTag is set."
            )

        return ()
    }

    [<TestMethod>]
    member this.``Upsert execute succeeds when the ETag matches`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert-matching-etag"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let updated = { testItem with name = "item-upsert-matching-etag"; quantity = 8 }

        let! upsertResponse =
            container.ExecuteAsync (
                upsert {
                    item updated
                    partitionKey updated.partitionKey
                    eTag createResponse.ETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (upsertResponse.Result, "Safe upsert with a matching eTag should return UpsertResult.Ok.")
        Assert.AreEqual (
            HttpStatusCode.OK,
            upsertResponse.HttpStatusCode,
            "Safe upsert with a matching eTag should return HTTP 200."
        )
    }

    [<TestMethod>]
    member this.``Upsert execute returns ModifiedBefore for a stale ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert-stale-etag"

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
                upsert {
                    item { testItem with name = "item-upsert-stale-etag-changed"; quantity = 2 }
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (overwriteResponse.Result, "Overwrite that changes the ETag should succeed.")

        let! staleUpsertResponse =
            container.ExecuteAsync (
                upsert {
                    item { testItem with name = "item-upsert-stale-etag-final"; quantity = 3 }
                    partitionKey testItem.partitionKey
                    eTag staleETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsModifiedBefore (
            staleUpsertResponse.Result,
            "Safe upsert with a stale eTag should return UpsertResult.ModifiedBefore."
        )
        Assert.AreEqual (
            HttpStatusCode.PreconditionFailed,
            staleUpsertResponse.HttpStatusCode,
            "Safe upsert with a stale eTag should return HTTP 412."
        )
    }

    [<TestMethod>]
    member this.``Upsert concurrently returns CustomError when update reports an error`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "upsert-concurrent-custom-error"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = upsertConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            updateOrCreate (fun _ -> async { return Result.Error "update rejected" })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        let customError =
            CosmosAssert.WantCustomError (
                concurrentResponse.Result,
                "Upsert concurrently should return UpsertConcurrentResult.CustomError when update reports an error."
            )
        Assert.AreEqual ("update rejected", customError, "Upsert concurrently CustomError should carry the reported error.")
    }

    [<TestMethod>]
    member this.``Upsert concurrently returns ModifiedBefore when retries are exhausted`` () : Task = task {
        let! container = this.GetContainer ()
        let original = this.NewItem "upsert-concurrent-exhausted"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item original
                    partitionKey original.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let operation = upsertConcurrenly<TestItem, string> {
            id original.id
            partitionKey original.partitionKey
            updateOrCreate (fun maybeCurrent -> async {
                match maybeCurrent with
                | Some current ->
                    // Always inject a competing write first, so the single allowed attempt
                    // (maxRetryCount = 1) always observes a stale ETag and exhausts immediately.
                    let competingUpdate = { current with name = "competing-exhaustion-update" }

                    let! _ =
                        container.ExecuteOverwriteAsync (
                            upsert {
                                item competingUpdate
                                partitionKey competingUpdate.partitionKey
                            },
                            this.CancellationToken
                        )
                        |> Async.AwaitTask

                    return Result.Ok { current with name = "should-not-be-persisted" }
                | None -> return Result.Error "Expected existing item for exhaustion test."
            })
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 1, this.CancellationToken)

        CosmosAssert.IsModifiedBefore (
            concurrentResponse.Result,
            "Upsert concurrently should return UpsertConcurrentResult.ModifiedBefore once retries are exhausted."
        )
    }

    [<TestMethod>]
    member this.``Upsert concurrently creates item when it does not exist`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "upsert-concurrent-create"

        let operation = upsertConcurrenly<TestItem, string> {
            id testItem.id
            partitionKey testItem.partitionKey
            updateOrCreate (
                function
                | None -> async { return Result.Ok testItem }
                | Some _ -> async { return Result.Error "Expected no existing item for create test." }
            )
        }

        let! concurrentResponse = container.ExecuteConcurrentlyAsync (operation, 3, this.CancellationToken)

        match concurrentResponse.Result with
        | UpsertConcurrentResult.Ok created ->
            Assert.AreEqual (testItem.id, created.id, "Upsert concurrently create branch should persist the new item's id.")
            Assert.AreEqual (testItem.name, created.name, "Upsert concurrently create branch should persist the new item's name.")
        | result -> Assert.Fail ($"Expected upsert concurrently create success, got {result}.")
    }
