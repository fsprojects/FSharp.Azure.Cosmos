namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; PatchTestCategory>]
type PatchOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Patch execute overwrite updates item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let patchedName = "item-patched"
        let patchedQuantity = 9

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", patchedName))
                    operation (PatchOperation.Replace ("/quantity", patchedQuantity))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok _ -> Assert.AreEqual (HttpStatusCode.OK, patchResponse.HttpStatusCode, "Patch should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patch success, got {result}.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let persisted = CosmosAssert.WantOk (readResponse.Result, "Patched item should be readable.")
        Assert.AreEqual (patchedName, persisted.name, "Patch should persist patched name.")
        Assert.AreEqual (patchedQuantity, persisted.quantity, "Patch should persist patched quantity.")
    }

    [<TestMethod>]
    member this.``PatchAndRead execute overwrite returns updated item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "patch-and-read"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let patchedName = "item-patched-and-read"
        let patchedQuantity = 11

        let! patchResponse =
            container.ExecuteOverwriteAsync (
                patchAndRead {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", patchedName))
                    operation (PatchOperation.Replace ("/quantity", patchedQuantity))
                },
                this.CancellationToken
            )

        match patchResponse.Result with
        | PatchResult.Ok patched ->
            Assert.AreEqual (patchedName, patched.name, "PatchAndRead should return patched name.")
            Assert.AreEqual (patchedQuantity, patched.quantity, "PatchAndRead should return patched quantity.")
            Assert.AreEqual (HttpStatusCode.OK, patchResponse.HttpStatusCode, "PatchAndRead should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patchAndRead success, got {result}.")
    }
