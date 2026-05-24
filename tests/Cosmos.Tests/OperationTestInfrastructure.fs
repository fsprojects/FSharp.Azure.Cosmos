namespace Tests.Integration

open System
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos

type internal TestItem = { id : string; partitionKey : string; name : string; quantity : int }

[<AbstractClass>]
type OperationTestBase () =
    inherit IntegrationTestBase ()

    let containerId = "operation-tests"

    member private this.GetDatabase () =
        match this.Application.Database with
        | ValueSome database -> database
        | ValueNone -> invalidOp "Database is not initialized."

    member this.GetContainer () : Task<Container> = task {
        let database = this.GetDatabase ()

        let! containerResponse =
            database.CreateContainerIfNotExistsAsync (
                ContainerProperties (containerId, "/partitionKey"),
                cancellationToken = this.CancellationToken
            )

        return containerResponse.Container
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
