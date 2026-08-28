namespace FSharp.Azure.Cosmos.Tests

open System
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

type private BuilderTestItem = { id : string; partitionKey : string; value : int }

[<TestClass; BuildersTestCategory>]
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

        Assert.IsValueSome (createOperation.PartitionKey, "Create builder should set partition key.")
        Assert.AreEqual (
            "create-session",
            createOperation.RequestOptions.SessionToken,
            "Create builder should set session token."
        )
        Assert.IsFalse (
            createOperation.RequestOptions.EnableContentResponseOnWrite,
            "Create builder should disable content response."
        )
        Assert.IsTrue (
            createAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "CreateAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Read builder configures id and partition key and request options`` () =
        let operation = read {
            id "read-id"
            partitionKey "pk"
            eTag "etag-value"
            sessionToken "read-session"
        }

        Assert.AreEqual ("read-id", operation.Id, "Read builder should set id.")
        Assert.IsNotNull (operation.RequestOptions, "Read builder should initialize request options when needed.")
        Assert.AreEqual ("etag-value", operation.RequestOptions.IfNoneMatchEtag, "Read builder should set eTag option.")
        Assert.AreEqual ("read-session", operation.RequestOptions.SessionToken, "Read builder should set session token.")

    [<TestMethod>]
    member _.``ReadMany builder collects item tuples and request options`` () =
        let operation = readMany {
            item "item-1" "pk"
            item "item-2" (PartitionKey "pk")
            sessionToken "readmany-session"
        }

        Assert.AreEqual (2, operation.Items.Length, "ReadMany builder should collect all item tuples.")
        Assert.IsNotNull (operation.RequestOptions, "ReadMany builder should create request options when needed.")
        Assert.AreEqual ("readmany-session", operation.RequestOptions.SessionToken, "ReadMany builder should set session token.")

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

        Assert.AreEqual (replaceItem.id, replaceOperation.Id, "Replace builder should set id.")
        Assert.AreEqual ("replace-etag", replaceOperation.RequestOptions.IfMatchEtag, "Replace builder should set eTag.")
        Assert.IsFalse (
            replaceOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace builder should disable content response."
        )
        Assert.IsTrue (
            replaceAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "ReplaceAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Replace concurrently builders configure update function and response mode`` () : Task = task {
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

        let! updateResult =
            replaceConcurrentlyOperation.Update { id = "id"; partitionKey = "pk"; value = 2 }
            |> Async.StartAsTask

        Assert.AreEqual ("replace-concurrent-id", replaceConcurrentlyOperation.Id, "Replace concurrently builder should set id.")
        Assert.IsOk (updateResult, "Replace concurrently builder should set update function.")
        Assert.IsFalse (
            replaceConcurrentlyOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace concurrently builder should disable content response."
        )
        Assert.IsTrue (
            replaceConcurrentlyAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "Replace concurrently and read builder should enable content response."
        )
    }

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

        Assert.IsValueSome (upsertOperation.PartitionKey, "Upsert builder should set partition key.")
        Assert.AreEqual ("upsert-etag", upsertOperation.RequestOptions.IfMatchEtag, "Upsert builder should set eTag.")
        Assert.IsFalse (
            upsertOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert builder should disable content response."
        )
        Assert.IsTrue (
            upsertAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "UpsertAndRead builder should enable content response."
        )

    [<TestMethod>]
    member _.``Upsert concurrently builders configure updateOrCreate and response mode`` () : Task = task {
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

        let! updateResult =
            upsertConcurrentlyOperation.UpdateOrCreate None
            |> Async.StartAsTask

        Assert.AreEqual ("upsert-concurrent-id", upsertConcurrentlyOperation.Id, "Upsert concurrently builder should set id.")
        Assert.IsOk (updateResult, "Upsert concurrently builder should set updateOrCreate function.")
        Assert.IsFalse (
            upsertConcurrentlyOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert concurrently builder should disable content response."
        )
        Assert.IsTrue (
            upsertConcurrentlyAndReadOperation.RequestOptions.EnableContentResponseOnWrite,
            "Upsert concurrently and read builder should enable content response."
        )
    }

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

        Assert.AreEqual ("patch-id", patchOperation.Id, "Patch builder should set id.")
        Assert.AreEqual (1, patchOperation.Operations.Length, "Patch builder should collect operations.")
        Assert.AreEqual (
            "FROM c WHERE c.partitionKey = 'pk'",
            patchOperation.RequestOptions.FilterPredicate,
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

        Assert.AreEqual ("delete-id", operation.Id, "Delete builder should set id.")
        let options =
            Assert.WantValueSome (operation.RequestOptions, "Delete builder should initialize request options.")
        Assert.AreEqual ("delete-etag", options.IfNoneMatchEtag, "Delete builder should set eTag.")
        Assert.AreEqual ("delete-session", options.SessionToken, "Delete builder should set session token.")

    [<TestMethod>]
    member _.``Unique key builders configure key and policy paths`` () =
        let uniqueKeyDefinition = uniqueKey { paths [ "/tenantId"; "/email" ] }
        let policy = uniqueKeyPolicy { key uniqueKeyDefinition }

        Assert.AreEqual (2, uniqueKeyDefinition.Paths.Count, "UniqueKey builder should add all paths.")
        Assert.AreEqual (1, policy.UniqueKeys.Count, "UniqueKeyPolicy builder should add unique key.")
