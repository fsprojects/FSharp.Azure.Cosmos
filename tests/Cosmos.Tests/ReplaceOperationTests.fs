namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; TestCategory(TestCategories.Replace)>]
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
        Assert.IsTrue (replaceResponse.HttpStatusCode = HttpStatusCode.OK, "Replace should return HTTP 200.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id replacement.id
                    partitionKey replacement.partitionKey
                },
                this.CancellationToken
            )

        let persisted = CosmosAssert.WantOk (readResponse.Result, "Replaced item should be readable.")
        Assert.IsTrue (replacement.name = persisted.name, "Replace should persist replacement name.")
        Assert.IsTrue (replacement.quantity = persisted.quantity, "Replace should persist replacement quantity.")
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
        Assert.IsTrue (replacement.name = replaced.name, "ReplaceAndRead should return replacement name.")
        Assert.IsTrue (replacement.quantity = replaced.quantity, "ReplaceAndRead should return replacement quantity.")
        Assert.IsTrue (replaceResponse.HttpStatusCode = HttpStatusCode.OK, "ReplaceAndRead should return HTTP 200.")
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

                    ()

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
            Assert.IsTrue (updated.name = "replace-concurrent-updated", "Replace concurrently should persist updated name.")
            Assert.IsTrue (updated.quantity = original.quantity + 10, "Replace concurrently should persist updated quantity.")
        | result -> Assert.Fail ($"Expected replace concurrently success after retry, got {result}.")
    }
