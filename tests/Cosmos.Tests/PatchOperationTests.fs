namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
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
        | PatchResult.Ok _ -> Assert.IsTrue (patchResponse.HttpStatusCode = HttpStatusCode.OK, "Patch should return HTTP 200.")
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
        let isNamePatched = patchedName = persisted.name
        let isQuantityPatched = patchedQuantity = persisted.quantity
        Assert.IsTrue (isNamePatched, "Patch should persist patched name.")
        Assert.IsTrue (isQuantityPatched, "Patch should persist patched quantity.")
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
            let isNamePatched = patchedName = patched.name
            let isQuantityPatched = patchedQuantity = patched.quantity
            Assert.IsTrue (isNamePatched, "PatchAndRead should return patched name.")
            Assert.IsTrue (isQuantityPatched, "PatchAndRead should return patched quantity.")
            Assert.IsTrue (patchResponse.HttpStatusCode = HttpStatusCode.OK, "PatchAndRead should return HTTP 200.")
        | result -> Assert.Fail ($"Expected patchAndRead success, got {result}.")
    }
