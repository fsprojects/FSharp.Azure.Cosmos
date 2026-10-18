namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; CreateTestCategory>]
type CreateOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``Create execute persists item`` () : Task = task {
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
        Assert.AreEqual (HttpStatusCode.Created, createResponse.HttpStatusCode, "Create should return HTTP 201.")

        let! readResponse =
            container.ExecuteAsync (
                read {
                    id testItem.id
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        let created = CosmosAssert.WantOk (readResponse.Result, "Created item should be readable.")
        Assert.AreEqual (testItem.id, created.id, "Create should persist item id.")
        Assert.AreEqual (testItem.partitionKey, created.partitionKey, "Create should persist partition key.")
    }

    [<TestMethod>]
    member this.``CreateAndRead execute returns created resource`` () : Task = task {
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
        Assert.AreEqual (testItem.id, created.id, "CreateAndRead should return created item id.")
        Assert.AreEqual (testItem.partitionKey, created.partitionKey, "CreateAndRead should return created partition key.")
    }

    [<TestMethod>]
    member this.``Create execute returns IdAlreadyExists for a duplicate id`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.NewItem "create-duplicate"

        let! firstResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (firstResponse.Result, "First create should succeed.")

        let! secondResponse =
            container.ExecuteAsync (
                create {
                    item testItem
                    partitionKey testItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsConflict (secondResponse.Result, "Create with a duplicate id should return CreateResult.IdAlreadyExists.")
        Assert.AreEqual (HttpStatusCode.Conflict, secondResponse.HttpStatusCode, "Duplicate create should return HTTP 409.")
    }
