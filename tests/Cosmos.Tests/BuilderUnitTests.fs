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
            consistencyLevel (Nullable ConsistencyLevel.Session)
            indexingDirective (Nullable IndexingDirective.Include)
            preTrigger "pre-1"
            preTriggers [ "pre-2"; "pre-3" ]
            postTrigger "post-1"
            postTriggers [ "post-2"; "post-3" ]
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
        Assert.AreEqual (
            Nullable ConsistencyLevel.Session,
            createOperation.RequestOptions.ConsistencyLevel,
            "Create builder should set consistency level."
        )
        Assert.AreEqual (
            Nullable IndexingDirective.Include,
            createOperation.RequestOptions.IndexingDirective,
            "Create builder should set indexing directive."
        )
        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2"; "pre-3" |],
            Array.ofSeq createOperation.RequestOptions.PreTriggers,
            "Create builder should accumulate pre-triggers in call order."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2"; "post-3" |],
            Array.ofSeq createOperation.RequestOptions.PostTriggers,
            "Create builder should accumulate post-triggers in call order."
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
    member _.``Create requestOptions override replaces options but re-applies content response mode`` () =
        let customOptions =
            ItemRequestOptions (SessionToken = "custom-session", EnableContentResponseOnWrite = true)

        let operation = create {
            item { id = "create-id"; partitionKey = "pk"; value = 1 }
            requestOptions customOptions
        }

        Assert.AreSame (customOptions, operation.RequestOptions, "Create requestOptions should replace the operation's options.")
        Assert.AreEqual (
            "custom-session",
            operation.RequestOptions.SessionToken,
            "Create requestOptions should preserve unrelated properties of the supplied options."
        )
        Assert.IsFalse (
            operation.RequestOptions.EnableContentResponseOnWrite,
            "Create requestOptions should force content response mode back to the builder's own mode."
        )

    [<TestMethod>]
    member _.``Read builder configures id and partition key and request options`` () =
        let operation = read {
            id "read-id"
            partitionKey "pk"
            eTag "etag-value"
            sessionToken "read-session"
            consistencyLevel (Nullable ConsistencyLevel.Eventual)
            indexingDirective (Nullable IndexingDirective.Exclude)
        }

        Assert.AreEqual ("read-id", operation.Id, "Read builder should set id.")
        Assert.IsNotNull (operation.RequestOptions, "Read builder should initialize request options when needed.")
        Assert.AreEqual ("etag-value", operation.RequestOptions.IfNoneMatchEtag, "Read builder should set eTag option.")
        Assert.AreEqual ("read-session", operation.RequestOptions.SessionToken, "Read builder should set session token.")
        Assert.AreEqual (
            Nullable ConsistencyLevel.Eventual,
            operation.RequestOptions.ConsistencyLevel,
            "Read builder should set consistency level."
        )
        Assert.AreEqual (
            Nullable IndexingDirective.Exclude,
            operation.RequestOptions.IndexingDirective,
            "Read builder should set indexing directive."
        )

    [<TestMethod>]
    member _.``Read builder initializes request options fresh for consistencyLevel as the first option`` () =
        let operation = read {
            id "read-id-2"
            partitionKey "pk"
            consistencyLevel (Nullable ConsistencyLevel.Strong)
        }

        Assert.IsNotNull (
            operation.RequestOptions,
            "Read builder should initialize request options when consistencyLevel is the first option set."
        )
        Assert.AreEqual (
            Nullable ConsistencyLevel.Strong,
            operation.RequestOptions.ConsistencyLevel,
            "Read builder should set consistency level when initializing fresh options."
        )

    [<TestMethod>]
    member _.``ReadMany builder collects item tuples and request options`` () =
        let operation = readMany {
            item "item-1" "pk"
            item "item-2" (PartitionKey "pk")
            items [ struct ("item-3", PartitionKey "pk") ]
            items [ struct ("item-4", "pk") ]
            sessionToken "readmany-session"
            consistencyLevel (Nullable ConsistencyLevel.Session)
        }

        Assert.HasCount (4, operation.Items, "ReadMany builder should collect items from both item and items calls.")
        Assert.IsNotNull (operation.RequestOptions, "ReadMany builder should create request options when needed.")
        Assert.AreEqual ("readmany-session", operation.RequestOptions.SessionToken, "ReadMany builder should set session token.")
        Assert.AreEqual (
            Nullable ConsistencyLevel.Session,
            operation.RequestOptions.ConsistencyLevel,
            "ReadMany builder should set consistency level."
        )

    [<TestMethod>]
    member _.``Replace builders configure operation and content response mode`` () =
        let replaceItem = { id = "replace-id"; partitionKey = "pk"; value = 1 }

        let replaceOperation = replace {
            id replaceItem.id
            item replaceItem
            partitionKey replaceItem.partitionKey
            eTag "replace-etag"
            consistencyLevel (Nullable ConsistencyLevel.BoundedStaleness)
            indexingDirective (Nullable IndexingDirective.Include)
            preTrigger "pre-1"
            preTriggers [ "pre-2" ]
            postTrigger "post-1"
            postTriggers [ "post-2" ]
        }

        let replaceAndReadOperation = replaceAndRead {
            id replaceItem.id
            item replaceItem
            partitionKey replaceItem.partitionKey
        }

        Assert.AreEqual (replaceItem.id, replaceOperation.Id, "Replace builder should set id.")
        Assert.AreEqual ("replace-etag", replaceOperation.RequestOptions.IfMatchEtag, "Replace builder should set eTag.")
        Assert.AreEqual (
            Nullable ConsistencyLevel.BoundedStaleness,
            replaceOperation.RequestOptions.ConsistencyLevel,
            "Replace builder should set consistency level."
        )
        Assert.AreEqual (
            Nullable IndexingDirective.Include,
            replaceOperation.RequestOptions.IndexingDirective,
            "Replace builder should set indexing directive."
        )
        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2" |],
            Array.ofSeq replaceOperation.RequestOptions.PreTriggers,
            "Replace builder should accumulate pre-triggers in call order."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2" |],
            Array.ofSeq replaceOperation.RequestOptions.PostTriggers,
            "Replace builder should accumulate post-triggers in call order."
        )
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
            consistencyLevel (Nullable ConsistencyLevel.ConsistentPrefix)
            indexingDirective (Nullable IndexingDirective.Include)
            preTrigger "pre-1"
            preTriggers [ "pre-2" ]
            postTrigger "post-1"
            postTriggers [ "post-2" ]
        }

        let upsertAndReadOperation = upsertAndRead {
            item upsertItem
            partitionKey upsertItem.partitionKey
        }

        Assert.IsValueSome (upsertOperation.PartitionKey, "Upsert builder should set partition key.")
        Assert.AreEqual ("upsert-etag", upsertOperation.RequestOptions.IfMatchEtag, "Upsert builder should set eTag.")
        Assert.AreEqual (
            Nullable ConsistencyLevel.ConsistentPrefix,
            upsertOperation.RequestOptions.ConsistencyLevel,
            "Upsert builder should set consistency level."
        )
        Assert.AreEqual (
            Nullable IndexingDirective.Include,
            upsertOperation.RequestOptions.IndexingDirective,
            "Upsert builder should set indexing directive."
        )
        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2" |],
            Array.ofSeq upsertOperation.RequestOptions.PreTriggers,
            "Upsert builder should accumulate pre-triggers in call order."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2" |],
            Array.ofSeq upsertOperation.RequestOptions.PostTriggers,
            "Upsert builder should accumulate post-triggers in call order."
        )
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
            operations [ PatchOperation.Set ("/name", "patched"); PatchOperation.Remove "/unused" ]
            filterPredicate "FROM c WHERE c.partitionKey = 'pk'"
            eTag "patch-etag"
            consistencyLevel (Nullable ConsistencyLevel.Eventual)
            preTrigger "pre-1"
            preTriggers [ "pre-2" ]
            postTrigger "post-1"
            postTriggers [ "post-2" ]
        }

        let patchAndReadOperation = patchAndRead<BuilderTestItem> {
            id "patch-and-read-id"
            partitionKey "pk"
            operation (PatchOperation.Replace ("/value", 5))
        }

        Assert.AreEqual ("patch-id", patchOperation.Id, "Patch builder should set id.")
        Assert.HasCount (
            3,
            patchOperation.Operations,
            "Patch builder should collect operations from both operation and operations calls."
        )
        Assert.AreEqual (
            "FROM c WHERE c.partitionKey = 'pk'",
            patchOperation.RequestOptions.FilterPredicate,
            "Patch builder should set filter predicate."
        )
        Assert.AreEqual ("patch-etag", patchOperation.RequestOptions.IfMatchEtag, "Patch builder should set eTag.")
        Assert.AreEqual (
            Nullable ConsistencyLevel.Eventual,
            patchOperation.RequestOptions.ConsistencyLevel,
            "Patch builder should set consistency level."
        )
        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2" |],
            Array.ofSeq patchOperation.RequestOptions.PreTriggers,
            "Patch builder should accumulate pre-triggers in call order."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2" |],
            Array.ofSeq patchOperation.RequestOptions.PostTriggers,
            "Patch builder should accumulate post-triggers in call order."
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
    member _.``Patch requestOptions override re-applies the operation's own content response mode`` () =
        let customOptions =
            PatchItemRequestOptions (FilterPredicate = "FROM c", EnableContentResponseOnWrite = false)

        let operation = patchAndRead<BuilderTestItem> {
            id "patch-id"
            partitionKey "pk"
            operation (PatchOperation.Replace ("/value", 2))
            requestOptions customOptions
        }

        Assert.AreSame (customOptions, operation.RequestOptions, "Patch requestOptions should replace the operation's options.")
        Assert.IsTrue (
            operation.RequestOptions.EnableContentResponseOnWrite,
            "Patch requestOptions should re-apply the current state's content response mode (true for patchAndRead), overriding the supplied options' own value."
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
    member _.``Delete builder initializes request options fresh for consistencyLevel as the first option`` () =
        let operation = delete {
            id "delete-id-2"
            partitionKey "pk"
            consistencyLevel (Nullable ConsistencyLevel.Strong)
            enableContentResponseOnWrite true
            indexingDirective (Nullable IndexingDirective.Include)
            preTrigger "pre-1"
            preTriggers [ "pre-2" ]
            postTrigger "post-1"
            postTriggers [ "post-2" ]
        }

        let options =
            Assert.WantValueSome (
                operation.RequestOptions,
                "Delete builder should initialize request options when consistencyLevel is the first option set."
            )
        Assert.AreEqual (
            Nullable ConsistencyLevel.Strong,
            options.ConsistencyLevel,
            "Delete builder should set consistency level when initializing fresh options."
        )
        Assert.IsTrue (options.EnableContentResponseOnWrite, "Delete builder should set content response mode.")
        Assert.AreEqual (
            Nullable IndexingDirective.Include,
            options.IndexingDirective,
            "Delete builder should set indexing directive."
        )
        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2" |],
            Array.ofSeq options.PreTriggers,
            "Delete builder should accumulate pre-triggers in call order."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2" |],
            Array.ofSeq options.PostTriggers,
            "Delete builder should accumulate post-triggers in call order."
        )

    [<TestMethod>]
    member _.``AddPreTrigger and AddPostTrigger accumulate across calls on fresh options`` () =
        let options = ItemRequestOptions ()

        options.AddPreTrigger "pre-1"
        options.AddPreTrigger "pre-2"
        options.AddPostTrigger "post-1"
        options.AddPostTrigger "post-2"

        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2" |],
            Array.ofSeq options.PreTriggers,
            "AddPreTrigger should accumulate triggers across calls, including on freshly created options."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2" |],
            Array.ofSeq options.PostTriggers,
            "AddPostTrigger should accumulate triggers across calls, including on freshly created options."
        )

    [<TestMethod>]
    member _.``AddPreTriggers and AddPostTriggers accumulate across calls on fresh options`` () =
        let options = ItemRequestOptions ()

        options.AddPreTriggers [ "pre-1"; "pre-2" ]
        options.AddPreTriggers [ "pre-3" ]
        options.AddPostTriggers [ "post-1"; "post-2" ]
        options.AddPostTriggers [ "post-3" ]

        CollectionAssert.AreEqual (
            [| "pre-1"; "pre-2"; "pre-3" |],
            Array.ofSeq options.PreTriggers,
            "AddPreTriggers should accumulate triggers across calls, including on freshly created options."
        )
        CollectionAssert.AreEqual (
            [| "post-1"; "post-2"; "post-3" |],
            Array.ofSeq options.PostTriggers,
            "AddPostTriggers should accumulate triggers across calls, including on freshly created options (regression test for the missing null-guard)."
        )

    [<TestMethod>]
    member _.``AddPreTriggers and AddPostTriggers throw for null trigger sequence`` () =
        let options = ItemRequestOptions ()

        Assert.ThrowsExactly<ArgumentNullException>(
            (fun () -> options.AddPreTriggers Unchecked.defaultof<string seq>),
            "AddPreTriggers should throw ArgumentNullException for a null sequence."
        )
        |> ignore

        Assert.ThrowsExactly<ArgumentNullException>(
            (fun () -> options.AddPostTriggers Unchecked.defaultof<string seq>),
            "AddPostTriggers should throw ArgumentNullException for a null sequence."
        )
        |> ignore

    [<TestMethod>]
    member _.``Unique key builders configure key and policy paths`` () =
        let uniqueKeyDefinition = uniqueKey { paths [ "/tenantId"; "/email" ] }
        let policy = uniqueKeyPolicy { key uniqueKeyDefinition }

        Assert.HasCount (2, uniqueKeyDefinition.Paths, "UniqueKey builder should add all paths.")
        Assert.HasCount (1, policy.UniqueKeys, "UniqueKeyPolicy builder should add unique key.")

    [<TestMethod>]
    member _.``Unique key builders support direct Yield seeding of a single path or key`` () =
        // Note: the bare form `uniqueKey { "/direct-path" }` (no `yield`) does NOT reach
        // `UniqueKeyBuilder.Yield(path : string)` — the builder has no `Combine`/`Delay`, so
        // there is no implicit-last-expression-as-yield desugaring, and the bare string
        // statement is silently ignored (F# warns FS0020) while `Yield` is invoked with `unit`,
        // producing an empty key. An explicit `yield` is required to reach the string overload.
        let uniqueKeyDefinition = uniqueKey { yield "/direct-path" }
        let policy = uniqueKeyPolicy { yield uniqueKeyDefinition }

        Assert.HasCount (1, uniqueKeyDefinition.Paths, "UniqueKey builder should seed a single path via direct Yield.")
        Assert.Contains ("/direct-path", uniqueKeyDefinition.Paths, "UniqueKey builder should seed the given path.")
        Assert.HasCount (1, policy.UniqueKeys, "UniqueKeyPolicy builder should seed a single key via direct Yield.")
