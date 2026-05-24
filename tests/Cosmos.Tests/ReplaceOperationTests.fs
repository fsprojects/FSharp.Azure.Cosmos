namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type ReplaceOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.Replace_execute_overwrite_replaces_existing_item () : Task = task {
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
    member this.ReplaceAndRead_execute_overwrite_returns_replaced_item () : Task = task {
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
