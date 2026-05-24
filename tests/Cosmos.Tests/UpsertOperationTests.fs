namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type UpsertOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.Upsert_execute_overwrite_creates_then_updates_item () : Task = task {
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
    member this.UpsertAndRead_execute_overwrite_returns_updated_item () : Task = task {
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
