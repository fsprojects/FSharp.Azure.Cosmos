namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos

[<CLIMutable>]
type TestItem = { id : string; partitionKey : string; name : string; quantity : int }

/// <summary>
/// The base class of the scenarios of the operation tests. Its database has the container that
/// <see cref="OperationTestBase{T}"/> reads and writes. Thus a scenario writes its data into <see cref="Container"/>.
/// </summary>
type OperationTestApplicationFactory (testContext : TestContext) =
    inherit DatabaseTestApplicationFactory (testContext)

    /// The identifier of the container of the operation tests.
    static member ContainerId = "operation-tests"

    /// The path of the partition key of the container of the operation tests.
    static member PartitionKeyPath = "/partitionKey"

    /// <summary>
    /// The container of the operation tests, as <see cref="DatabaseTestApplicationFactory.Client"/> sees it.
    /// </summary>
    member this.Container = this.GetContainerOf this.Client

    /// <summary>
    /// The container of the operation tests, as <paramref name="client"/> sees it. Use a client with other serializer
    /// options to write documents in another form.
    /// </summary>
    member this.GetContainerOf (client : CosmosClient) : Container =
        client.GetContainer (this.DatabaseId, OperationTestApplicationFactory.ContainerId)

    /// <inheritdoc />
    override _.Containers =
        ContainerSet.Create (
            ContainerProperties (OperationTestApplicationFactory.ContainerId, OperationTestApplicationFactory.PartitionKeyPath)
        )

[<AbstractClass>]
type OperationTestBase<'DatabaseTestApplicationFactory when 'DatabaseTestApplicationFactory :> DatabaseTestApplicationFactory> ()
    =
    inherit IntegrationTestBase<'DatabaseTestApplicationFactory> ()

    member this.GetContainer () : Task<Container> = task {
        return!
            this.Application.GetOrCreateContainerAsync (
                OperationTestApplicationFactory.ContainerId,
                OperationTestApplicationFactory.PartitionKeyPath,
                this.CancellationToken
            )
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
