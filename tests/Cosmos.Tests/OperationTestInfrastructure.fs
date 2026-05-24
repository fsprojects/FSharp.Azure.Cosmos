namespace Tests.Integration

open System
open System.Threading.Tasks
open Microsoft.Azure.Cosmos

type internal TestItem = { id : string; partitionKey : string; name : string; quantity : int }

[<AbstractClass>]
type OperationTestBase () =
    inherit IntegrationTestBase ()

    [<Literal>]
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
