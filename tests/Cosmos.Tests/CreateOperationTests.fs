namespace Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type CreateOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.Create_execute_persists_item () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "create"

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Create should return CreateResult.Ok.")
        Assert.IsTrue (createResponse.HttpStatusCode = HttpStatusCode.Created, "Create should return HTTP 201.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let created = CosmosAssert.WantOk (readResponse.Result, "Created item should be readable.")
        Assert.IsTrue (testItem.id = created.id, "Create should persist item id.")
        Assert.IsTrue (testItem.partitionKey = created.partitionKey, "Create should persist partition key.")
    }

    [<TestMethod>]
    member this.CreateAndRead_execute_returns_created_resource () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "create-and-read"

        let! response =
            container.ExecuteAsync (
                createAndRead {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let created = CosmosAssert.WantOk (response.Result, "CreateAndRead should return CreateResult.Ok.")
        Assert.IsTrue (testItem.id = created.id, "CreateAndRead should return created item id.")
        Assert.IsTrue (testItem.partitionKey = created.partitionKey, "CreateAndRead should return created partition key.")
    }
