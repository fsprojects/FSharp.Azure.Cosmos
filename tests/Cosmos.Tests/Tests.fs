namespace Tests.Integration

open System
open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

type private TestItem = { id : string; partitionKey : string; name : string; quantity : int }

[<TestClass>]
type OperationIntegrationTests () =
    inherit IntegrationTestBase ()

    [<Literal>]
    let containerId = "operation-tests"

    member private this.GetContainerAsync () : Task<Container> = task {
        let database =
            match this.Application.Database with
            | ValueSome database -> database
            | ValueNone -> invalidOp "Database is not initialized."

        let! containerResponse =
            database.CreateContainerIfNotExistsAsync (
                ContainerProperties (containerId, "/partitionKey"),
                cancellationToken = this.CancellationToken
            )

        return containerResponse.Container
    }

    member private this.NewItem (suffix : string) : TestItem = {
        id = $"{this.TestContext.TestName}-{suffix}"
        partitionKey = "integration"
        name = $"item-{suffix}"
        quantity = 1
    }

    [<TestMethod>]
    member this.Create_execute_returns_created_resource () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "create"

        let! response =
            container.ExecuteAsync (
                createAndRead {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match response.Result with
        | CreateResult.Ok created ->
            Assert.IsTrue (testItem.id = created.id, "Create should persist the item id.")
            Assert.IsTrue (testItem.partitionKey = created.partitionKey, "Create should persist the item partition key.")
            Assert.IsFalse (String.IsNullOrWhiteSpace response.ActivityId, "Create should return a valid activity id.")
            Assert.IsTrue (response.RequestCharge > 0.0, "Create should report positive request charge.")
        | result -> Assert.Fail ($"Expected create success but received {result}.")
    }

    [<TestMethod>]
    member this.Read_execute_returns_existing_and_not_found_states () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "read"

        let! _ =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let! foundResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match foundResponse.Result with
        | ReadResult.Ok found ->
            Assert.IsTrue (testItem.id = found.id, "Read should return the item that was created.")
            Assert.IsTrue (foundResponse.HttpStatusCode = HttpStatusCode.OK, "Read success should return HTTP 200.")
        | result -> Assert.Fail ($"Expected successful read but received {result}.")

        let missingId = $"{testItem.id}-missing"

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id missingId
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match missingResponse.Result with
        | ReadResult.NotFound _ ->
            Assert.IsTrue (
                missingResponse.HttpStatusCode = HttpStatusCode.NotFound,
                "Read of missing item should return HTTP 404."
            )
        | result -> Assert.Fail ($"Expected read not found for missing item but received {result}.")
    }

    [<TestMethod>]
    member this.Upsert_execute_overwrite_creates_then_updates_item () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "upsert"

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
            Assert.IsTrue (testItem.name = created.name, "Upsert create should store initial payload.")
            Assert.IsTrue (createdResponse.HttpStatusCode = HttpStatusCode.Created, "Initial upsert should create item.")
        | result -> Assert.Fail ($"Expected upsert create success but received {result}.")

        let updated = { testItem with name = "item-upsert-updated"; quantity = 5 }

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
            Assert.IsTrue (updated.name = upserted.name, "Upsert update should persist new payload.")
            Assert.IsTrue (updated.quantity = upserted.quantity, "Upsert update should persist new quantity.")
            Assert.IsTrue (updatedResponse.HttpStatusCode = HttpStatusCode.OK, "Second upsert should replace existing item.")
        | result -> Assert.Fail ($"Expected upsert update success but received {result}.")
    }

    [<TestMethod>]
    member this.Replace_execute_overwrite_replaces_existing_item () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "replace"

        let! _ =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let replacement = { testItem with name = "item-replaced"; quantity = 3 }

        let! replaceResponse =
            container.ExecuteOverwriteAsync (
                replaceAndRead {
                    id replacement.id
                    item replacement
                    partitionKey replacement.partitionKey
                },
                this.CancellationToken
            )

        match replaceResponse.Result with
        | ReplaceResult.Ok replaced ->
            Assert.IsTrue (replacement.name = replaced.name, "Replace should persist replacement name.")
            Assert.IsTrue (replacement.quantity = replaced.quantity, "Replace should persist replacement quantity.")
            Assert.IsTrue (replaceResponse.HttpStatusCode = HttpStatusCode.OK, "Replace should return HTTP 200.")
        | result -> Assert.Fail ($"Expected replace success but received {result}.")
    }

    [<TestMethod>]
    member this.Patch_execute_overwrite_updates_targeted_field () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "patch"

        let! _ =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patchAndRead {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (Microsoft.Azure.Cosmos.PatchOperation.Replace ("/name", "item-patched"))
                    operation (Microsoft.Azure.Cosmos.PatchOperation.Replace ("/quantity", 9))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok patched ->
            Assert.IsTrue ("item-patched" = patched.name, "Patch should update the name field.")
            Assert.IsTrue (9 = patched.quantity, "Patch should update the quantity field.")
            Assert.IsTrue (patchResponse.HttpStatusCode = HttpStatusCode.OK, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success but received {result}.")
    }

    [<TestMethod>]
    member this.Delete_execute_removes_item_and_subsequent_read_is_not_found () : Task = task {
        let! container = this.GetContainerAsync ()
        let testItem = this.NewItem "delete"

        let! _ =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let! deleteResponse =
            container.ExecuteAsync (
                delete {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match deleteResponse.Result with
        | DeleteResult.Ok _ ->
            Assert.IsTrue (deleteResponse.HttpStatusCode = HttpStatusCode.NoContent, "Delete should return HTTP 204.")
        | result -> Assert.Fail ($"Expected delete success but received {result}.")

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        match missingResponse.Result with
        | ReadResult.NotFound _ ->
            Assert.IsTrue (missingResponse.HttpStatusCode = HttpStatusCode.NotFound, "Read after delete should return HTTP 404.")
        | result -> Assert.Fail ($"Expected read not found after delete but received {result}.")
    }

    [<TestMethod>]
    member this.ReadMany_execute_returns_matching_items () : Task = task {
        let! container = this.GetContainerAsync ()
        let firstItem = this.NewItem "readmany-1"
        let secondItem = this.NewItem "readmany-2"

        let! _ =
            container.ExecuteAsync (
                create {
                    item firstItem
                    partitionKey firstItem.partitionKey
                },
                this.CancellationToken
            )

        let! _ =
            container.ExecuteAsync (
                create {
                    item secondItem
                    partitionKey secondItem.partitionKey
                },
                this.CancellationToken
            )

        let! readManyResponse =
            container.ExecuteAsync (
                readMany {
                    item firstItem.id firstItem.partitionKey
                    item secondItem.id secondItem.partitionKey
                },
                this.CancellationToken
            )

        match readManyResponse.Result with
        | ReadManyResult.Ok (feed : FeedResponse<TestItem>) ->
            let returnedIds = feed |> Seq.map _.id |> Set.ofSeq
            Assert.IsTrue (feed.Count = 2, "ReadMany should return the requested number of existing items.")
            Assert.IsTrue (returnedIds.Contains firstItem.id, "ReadMany should include first requested item.")
            Assert.IsTrue (returnedIds.Contains secondItem.id, "ReadMany should include second requested item.")
            Assert.IsTrue (readManyResponse.HttpStatusCode = HttpStatusCode.OK, "ReadMany should return HTTP 200.")
        | result -> Assert.Fail ($"Expected read many success but received {result}.")
    }
