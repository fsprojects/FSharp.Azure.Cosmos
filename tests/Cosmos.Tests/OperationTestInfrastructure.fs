namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos

[<CLIMutable>]
type TestItem = { id : string; partitionKey : string; name : string; quantity : int }

[<AbstractClass>]
type OperationTestBase<'DatabaseTestApplicationFactory when 'DatabaseTestApplicationFactory :> DatabaseTestApplicationFactory> ()
    =
    inherit IntegrationTestBase<'DatabaseTestApplicationFactory> ()

    let containerId = "operation-tests"

    member this.GetContainer () : Task<Container> = task {
        return! this.Application.GetOrCreateContainerAsync (containerId, "/partitionKey", this.CancellationToken)
    }

    member internal this.NewItem (suffix : string) : TestItem = {
        id = $"{this.TestContext.TestName}-{suffix}"
        partitionKey = "integration"
        name = $"item-{suffix}"
        quantity = 1
    }

    member internal this.SeedItemsAsync (container : Container, items : TestItem seq) : Task = task {
        for seedItem in items do
            let! createResponse =
                container.ExecuteAsync (
                    create {
                        item seedItem
                        partitionKey seedItem.partitionKey
                    },
                    this.CancellationToken
                )

            CosmosAssert.IsOk (createResponse.Result, $"Seed create should succeed for item '{seedItem.id}'.")
    }

[<AbstractClass>]
type OperationTestBase () =
    inherit OperationTestBase<DatabaseTestApplicationFactory> ()

    override _.CreateApplication context = DatabaseTestApplicationFactory (context)
