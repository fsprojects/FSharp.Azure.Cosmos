namespace Tests

open System
open System.Reflection
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type TestClass () =

    static let throughputBucketProperty =
        let propertyInfo =
            typeof<RequestOptions>.GetProperty ("ThroughputBucket", BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic)

        match propertyInfo with
        | null -> invalidOp "RequestOptions.ThroughputBucket should exist in the Cosmos SDK package."
        | propertyInfo -> propertyInfo

    static member private GetThroughputBucket (requestOptions : RequestOptions | null) =
        Assert.IsNotNull (requestOptions, "Request options should be created before reading the throughput bucket.")

        match throughputBucketProperty.GetValue requestOptions with
        | null -> Nullable ()
        | value -> Nullable (unbox<int> value)

    [<TestMethod>]
    member _.``Create, replace, and upsert builders set throughput bucket`` () =
        let createOperation =
            create<string> {
                item "item"
                throughputBucket 1
            }

        let replaceOperation =
            replace<string> {
                id "id"
                item "item"
                throughputBucket 2
            }

        let upsertOperation =
            upsert<string> {
                item "item"
                throughputBucket 3
            }

        Assert.AreEqual (
            Nullable 1,
            TestClass.GetThroughputBucket createOperation.RequestOptions,
            "Create CE should set the throughput bucket on ItemRequestOptions."
        )

        Assert.AreEqual (
            Nullable 2,
            TestClass.GetThroughputBucket replaceOperation.RequestOptions,
            "Replace CE should set the throughput bucket on ItemRequestOptions."
        )

        Assert.AreEqual (
            Nullable 3,
            TestClass.GetThroughputBucket upsertOperation.RequestOptions,
            "Upsert CE should set the throughput bucket on ItemRequestOptions."
        )

    [<TestMethod>]
    member _.``Concurrent builders set throughput bucket`` () =
        let replaceOperation =
            replaceConcurrenly<string, string> {
                id "id"
                update (fun item -> async { return Result.Ok item })
                throughputBucket 4
            }

        let upsertOperation =
            upsertConcurrenly<string, string> {
                id "id"
                updateOrCreate (fun item -> async { return Result.Ok (defaultArg item "item") })
                throughputBucket 5
            }

        Assert.AreEqual (
            Nullable 4,
            TestClass.GetThroughputBucket replaceOperation.RequestOptions,
            "Concurrent replace CE should set the throughput bucket on ItemRequestOptions."
        )

        Assert.AreEqual (
            Nullable 5,
            TestClass.GetThroughputBucket upsertOperation.RequestOptions,
            "Concurrent upsert CE should set the throughput bucket on ItemRequestOptions."
        )

    [<TestMethod>]
    member _.``Delete and read builders create request options for throughput bucket`` () =
        let deleteOperation =
            delete {
                id "id"
                throughputBucket 6
            }

        let readOperation =
            read<string> {
                id "id"
                throughputBucket 7
            }

        Assert.IsTrue (
            deleteOperation.RequestOptions.IsSome,
            "Delete CE should create ItemRequestOptions when throughput bucket is set."
        )

        Assert.AreEqual (
            Nullable 6,
            deleteOperation.RequestOptions |> ValueOption.get |> TestClass.GetThroughputBucket,
            "Delete CE should set the throughput bucket on ItemRequestOptions."
        )

        Assert.IsNotNull (readOperation.RequestOptions, "Read CE should create ItemRequestOptions when throughput bucket is set.")

        Assert.AreEqual (
            Nullable 7,
            TestClass.GetThroughputBucket readOperation.RequestOptions,
            "Read CE should set the throughput bucket on ItemRequestOptions."
        )

    [<TestMethod>]
    member _.``Patch and readMany builders set throughput bucket`` () =
        let patchOperation =
            patch<string> {
                id "id"
                throughputBucket 8
            }

        let readManyOperation =
            readMany<string> {
                item "id" "partition"
                throughputBucket 9
            }

        Assert.AreEqual (
            Nullable 8,
            TestClass.GetThroughputBucket patchOperation.RequestOptions,
            "Patch CE should set the throughput bucket on PatchItemRequestOptions."
        )

        Assert.IsNotNull (
            readManyOperation.RequestOptions,
            "ReadMany CE should create ReadManyRequestOptions when throughput bucket is set."
        )

        Assert.AreEqual (
            Nullable 9,
            TestClass.GetThroughputBucket readManyOperation.RequestOptions,
            "ReadMany CE should set the throughput bucket on ReadManyRequestOptions."
        )
