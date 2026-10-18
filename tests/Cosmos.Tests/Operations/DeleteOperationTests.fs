namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; DeleteTestCategory>]
type DeleteOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Delete execute removes item and subsequent read is not found`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "delete"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let! deleteResponse =
            container.ExecuteAsync (
                delete {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (deleteResponse.Result, "Delete should return DeleteResult.Ok.")
        Assert.AreEqual (HttpStatusCode.NoContent, deleteResponse.HttpStatusCode, "Delete should return HTTP 204.")

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (missingResponse.Result, "Read after delete should return ReadResult.NotFound.")
        Assert.AreEqual (HttpStatusCode.NotFound, missingResponse.HttpStatusCode, "Read after delete should return HTTP 404.")
    }

    [<TestMethod>]
    member this.``Delete execute returns NotFound for a missing item`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "delete-missing"

        let! deleteResponse =
            container.ExecuteAsync (
                delete {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (deleteResponse.Result, "Delete of a never-created item should return DeleteResult.NotFound.")
        Assert.AreEqual (
            HttpStatusCode.NotFound,
            deleteResponse.HttpStatusCode,
            "Delete of a missing item should return HTTP 404."
        )
    }

    [<TestMethod>]
    member this.``Delete execute returns ModifiedBefore for a stale ETag`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "delete-stale-etag"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")
        let staleETag = createResponse.ETag

        let! overwriteResponse =
            container.ExecuteOverwriteAsync (
                patch {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    operation (PatchOperation.Replace ("/name", "item-delete-stale-etag-changed"))
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (overwriteResponse.Result, "Overwrite that changes the ETag should succeed.")

        let! staleDeleteResponse =
            container.ExecuteAsync (
                delete {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    eTag staleETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsModifiedBefore (
            staleDeleteResponse.Result,
            "Delete with a stale eTag should return DeleteResult.ModifiedBefore."
        )
        Assert.AreEqual (
            HttpStatusCode.PreconditionFailed,
            staleDeleteResponse.HttpStatusCode,
            "Delete with a stale eTag should return HTTP 412."
        )

        let! currentResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (currentResponse.Result, "Read after the failed delete should succeed.")

        let! deleteResponse =
            container.ExecuteAsync (
                delete {
                    id testItem.id
                    partitionKey testItem.partitionKey
                    eTag currentResponse.ETag
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (deleteResponse.Result, "Delete with a matching eTag should return DeleteResult.Ok.")
        Assert.AreEqual (
            HttpStatusCode.NoContent,
            deleteResponse.HttpStatusCode,
            "Delete with a matching eTag should return HTTP 204."
        )
    }
