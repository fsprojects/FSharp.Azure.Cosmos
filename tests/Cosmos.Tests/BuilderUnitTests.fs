namespace Tests.Unit

open System
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

type private BuilderTestItem = { id : string; partitionKey : string; value : int }

[<TestClass>]
type BuilderUnitTests () =

    [<TestMethod>]
    member _.``Create builders configure operation and content response mode`` () =
        let createItem = { id = "create-id"; partitionKey = "pk"; value = 1 }

        let createOperation = create {
            item createItem
            partitionKey createItem.partitionKey
            sessionToken "create-session"
        }

        let createAndReadOperation = createAndRead {
            item createItem
            partitionKey createItem.partitionKey
            sessionToken "create-and-read-session"
        }

        Assert.IsTrue (createOperation.PartitionKey |> ValueOption.isSome, "Create builder should set partition key.")
        Assert.IsTrue (createOperation.RequestOptions.SessionToken = "create-session", "Create builder should set session token.")
        Assert.IsFalse (
            createOperation.RequestOptions.EnableContentResponseOnWrite,
            "Create builder should disable content response."
        )
        Assert.IsTrue (
            createAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "CreateAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Read builder configures id partition key and request options`` () =
        let operation = read {
            id "read-id"
            partitionKey "pk"
            eTag "etag-value"
            sessionToken "read-session"
        }

        Assert.IsTrue (operation.Id = "read-id", "Read builder should set id.")
        Assert.IsFalse (isNull operation.RequestOptions, "Read builder should initialize request options when needed.")
        Assert.IsTrue (operation.RequestOptions.IfNoneMatchEtag = "etag-value", "Read builder should set eTag option.")
        Assert.IsTrue (operation.RequestOptions.SessionToken = "read-session", "Read builder should set session token.")

    [<TestMethod>]
    member _.``ReadMany builder collects item tuples and request options`` () =
        let operation = readMany {
            item "item-1" "pk"
            item "item-2" (PartitionKey "pk")
            sessionToken "readmany-session"
        }

        Assert.IsTrue (operation.Items.Length = 2, "ReadMany builder should collect all item tuples.")
        Assert.IsFalse (isNull operation.RequestOptions, "ReadMany builder should create request options when needed.")
        Assert.IsTrue (operation.RequestOptions.SessionToken = "readmany-session", "ReadMany builder should set session token.")

    [<TestMethod>]
    member _.``Replace builders configure operation and content response mode`` () =
        let replaceItem = { id = "replace-id"; partitionKey = "pk"; value = 1 }

        let replaceOperation = replace {
            id replaceItem.id
            item replaceItem
            partitionKey replaceItem.partitionKey
            eTag "replace-etag"
        }

        let replaceAndReadOperation = replaceAndRead {
            id replaceItem.id
            item replaceItem
            partitionKey replaceItem.partitionKey
        }

        Assert.IsTrue (replaceOperation.Id = replaceItem.id, "Replace builder should set id.")
        Assert.IsTrue (replaceOperation.RequestOptions.IfMatchEtag = "replace-etag", "Replace builder should set eTag.")
        Assert.IsFalse (
            replaceOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace builder should disable content response."
        )
        Assert.IsTrue (
            replaceAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "ReplaceAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Replace concurrently builders configure update function and response mode`` () =
        let replaceConcurrentlyOperation = replaceConcurrenly<BuilderTestItem, string> {
            id "replace-concurrent-id"
            partitionKey "pk"
            update (fun item -> async { return Result.Ok { item with value = item.value + 1 } })
        }

        let replaceConcurrentlyAndReadOperation = replaceConcurrenlyAndRead<BuilderTestItem, string> {
            id "replace-concurrent-and-read-id"
            partitionKey "pk"
            update (fun item -> async { return Result.Ok item })
        }

        let updateResult =
            replaceConcurrentlyOperation.Update { id = "id"; partitionKey = "pk"; value = 2 }
            |> Async.RunSynchronously

        Assert.IsTrue (replaceConcurrentlyOperation.Id = "replace-concurrent-id", "Replace concurrently builder should set id.")
        Assert.IsTrue (Result.isOk updateResult, "Replace concurrently builder should set update function.")
        Assert.IsFalse (
            replaceConcurrentlyOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace concurrently builder should disable content response."
        )
        Assert.IsTrue (
            replaceConcurrentlyAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace concurrently and read builder should enable content response."
        )

    [<TestMethod>]
    member _.``Upsert builders configure operation and content response mode`` () =
        let upsertItem = { id = "upsert-id"; partitionKey = "pk"; value = 1 }

        let upsertOperation = upsert {
            item upsertItem
            partitionKey upsertItem.partitionKey
            eTag "upsert-etag"
        }

        let upsertAndReadOperation = upsertAndRead {
            item upsertItem
            partitionKey upsertItem.partitionKey
        }

        Assert.IsTrue (upsertOperation.PartitionKey |> ValueOption.isSome, "Upsert builder should set partition key.")
        Assert.IsTrue (upsertOperation.RequestOptions.IfMatchEtag = "upsert-etag", "Upsert builder should set eTag.")
        Assert.IsFalse (
            upsertOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert builder should disable content response."
        )
        Assert.IsTrue (
            upsertAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "UpsertAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Upsert concurrently builders configure updateOrCreate and response mode`` () =
        let upsertConcurrentlyOperation = upsertConcurrenly<BuilderTestItem, string> {
            id "upsert-concurrent-id"
            partitionKey "pk"
            updateOrCreate (fun maybeItem -> async {
                match maybeItem with
                | Some item -> return Result.Ok { item with value = item.value + 1 }
                | None -> return Result.Ok { id = "new-id"; partitionKey = "pk"; value = 1 }
            })
        }

        let upsertConcurrentlyAndReadOperation = upsertConcurrenlyAndRead<BuilderTestItem, string> {
            id "upsert-concurrent-and-read-id"
            partitionKey "pk"
            updateOrCreate (fun _ -> async { return Error "custom-error" })
        }

        let updateResult =
            upsertConcurrentlyOperation.UpdateOrCreate None
            |> Async.RunSynchronously

        Assert.IsTrue (upsertConcurrentlyOperation.Id = "upsert-concurrent-id", "Upsert concurrently builder should set id.")
        Assert.IsTrue (Result.isOk updateResult, "Upsert concurrently builder should set updateOrCreate function.")
        Assert.IsFalse (
            upsertConcurrentlyOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert concurrently builder should disable content response."
        )
        Assert.IsTrue (
            upsertConcurrentlyAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert concurrently and read builder should enable content response."
        )

    [<TestMethod>]
    member _.``Patch builders configure operations and content response mode`` () =
        let patchOperation = patch<BuilderTestItem> {
            id "patch-id"
            partitionKey "pk"
            operation (PatchOperation.Replace ("/value", 2))
            filterPredicate "FROM c WHERE c.partitionKey = 'pk'"
        }

        let patchAndReadOperation = patchAndRead<BuilderTestItem> {
            id "patch-and-read-id"
            partitionKey "pk"
            operation (PatchOperation.Replace ("/value", 5))
        }

        Assert.IsTrue (patchOperation.Id = "patch-id", "Patch builder should set id.")
        Assert.IsTrue (patchOperation.Operations.Length = 1, "Patch builder should collect operations.")
        Assert.IsTrue (
            patchOperation.RequestOptions.FilterPredicate = "FROM c WHERE c.partitionKey = 'pk'",
            "Patch builder should set filter predicate."
        )
        Assert.IsFalse (
            patchOperation.RequestOptions.EnableContentResponseOnWrite,
            "Patch builder should disable content response."
        )
        Assert.IsTrue (
            patchAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "PatchAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Delete builder configures id partition key and request options`` () =
        let operation = delete {
            id "delete-id"
            partitionKey "pk"
            eTag "delete-etag"
            sessionToken "delete-session"
        }

        Assert.IsTrue (operation.Id = "delete-id", "Delete builder should set id.")
        Assert.IsTrue (operation.RequestOptions |> ValueOption.isSome, "Delete builder should initialize request options.")

        let options = operation.RequestOptions |> ValueOption.get
        Assert.IsTrue (options.IfNoneMatchEtag = "delete-etag", "Delete builder should set eTag.")
        Assert.IsTrue (options.SessionToken = "delete-session", "Delete builder should set session token.")

    [<TestMethod>]
    member _.``Unique key builders configure key and policy paths`` () =
        let uniqueKeyDefinition = uniqueKey { paths [ "/tenantId"; "/email" ] }
        let policy = uniqueKeyPolicy { key uniqueKeyDefinition }

        Assert.IsTrue (uniqueKeyDefinition.Paths.Count = 2, "UniqueKey builder should add all paths.")
        Assert.IsTrue (policy.UniqueKeys.Count = 1, "UniqueKeyPolicy builder should add unique key.")
