namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
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
        Assert.IsTrue (deleteResponse.HttpStatusCode = HttpStatusCode.NoContent, "Delete should return HTTP 204.")

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (missingResponse.Result, "Read after delete should return ReadResult.NotFound.")
        Assert.IsTrue (missingResponse.HttpStatusCode = HttpStatusCode.NotFound, "Read after delete should return HTTP 404.")
    }
