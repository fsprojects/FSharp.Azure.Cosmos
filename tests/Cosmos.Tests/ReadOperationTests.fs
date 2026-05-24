namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type ReadOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.Read_execute_returns_existing_and_not_found_states () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "read"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Seed create should succeed.")

        let! foundResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let found =
            CosmosAssert.WantOk (foundResponse.Result, "Read should return ReadResult.Ok for existing item.")
        Assert.IsTrue (testItem.id = found.id, "Read should return created item.")
        Assert.IsTrue (foundResponse.HttpStatusCode = HttpStatusCode.OK, "Read should return HTTP 200 for existing item.")

        let! missingResponse =
            container.ExecuteAsync (
                read {
                    id $"{testItem.id}-missing"
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsNotFound (missingResponse.Result, "Read should return ReadResult.NotFound for missing item.")
        Assert.IsTrue (missingResponse.HttpStatusCode = HttpStatusCode.NotFound, "Read missing should return HTTP 404.")
    }
