namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; TestCategory(TestCategories.Upsert)>]
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
            Assert.IsTrue (createResult.HttpStatusCode = HttpStatusCode.Created, "First upsert should create item (HTTP 201).")
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
            Assert.IsTrue (updateResult.HttpStatusCode = HttpStatusCode.OK, "Second upsert should update item (HTTP 200).")
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
        Assert.IsTrue (updated.name = persisted.name, "Upsert should persist updated name.")
        Assert.IsTrue (updated.quantity = persisted.quantity, "Upsert should persist updated quantity.")
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
            Assert.IsTrue (testItem.name = created.name, "UpsertAndRead create should return created resource.")
            Assert.IsTrue (
                createdResponse.HttpStatusCode = HttpStatusCode.Created,
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
            Assert.IsTrue (updated.name = upserted.name, "UpsertAndRead update should return updated name.")
            Assert.IsTrue (updated.quantity = upserted.quantity, "UpsertAndRead update should return updated quantity.")
            Assert.IsTrue (updatedResponse.HttpStatusCode = HttpStatusCode.OK, "UpsertAndRead update should return HTTP 200.")
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
            Assert.IsTrue (updated.name = "upsert-concurrent-updated", "Upsert concurrently should persist updated name.")
            Assert.IsTrue (updated.quantity = original.quantity + 7, "Upsert concurrently should persist updated quantity.")
        | result -> Assert.Fail ($"Expected upsert concurrently success after retry, got {result}.")
    }
