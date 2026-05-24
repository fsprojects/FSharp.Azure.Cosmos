namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.VisualStudio.TestTools.UnitTesting

type SingleItemScenario (testContext : TestContext) as this =
    inherit DatabaseTestApplicationFactory (testContext)

    let containerId = "operation-tests"

    let seededItem : TestItem = {
        id = $"{testContext.TestName}-read"
        partitionKey = "integration"
        name = "item-read"
        quantity = 1
    }

    member _.SeededItem = seededItem

    override _.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        let! container = this.GetOrCreateContainerAsync (containerId, "/partitionKey", cancellationToken)

        let! createResponse =
            container.ExecuteAsync (
                create {
                    item seededItem
                    partitionKey seededItem.partitionKey
                },
                cancellationToken
            )

        CosmosAssert.IsOk (createResponse.Result, "Read scenario seed create should succeed.")
    }

[<TestClass; TestCategory(TestCategories.Read)>]
type ReadOperationIntegrationTests () =
    inherit OperationTestBase<SingleItemScenario> ()

    override _.CreateApplication context = SingleItemScenario (context)

    [<TestMethod>]
    member this.``Read execute returns existing and not found states`` () : Task = task {
        let! container = this.GetContainer ()
        let testItem = this.Application.SeededItem

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
