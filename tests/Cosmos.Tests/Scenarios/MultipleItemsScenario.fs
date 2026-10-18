namespace FSharp.Azure.Cosmos.Tests.Integration.MultipleItemsScenario

open System.Threading
open System.Threading.Tasks

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration

/// The values that the tests of this scenario read.
[<Struct>]
type TestData = {
    /// The first item that the scenario writes.
    FirstSeededItem : TestItem
    /// The second item that the scenario writes, in the same partition as the first item.
    SecondSeededItem : TestItem
}

/// <summary>
/// Writes two items into one partition of the container of <see cref="OperationTestBase{T}"/>.
/// </summary>
type TestApplicationFactory (testContext : TestContext) =
    inherit DatabaseTestApplicationFactory (testContext)

    // The container of OperationTestBase
    let containerId = "operation-tests"

    let firstSeededItem : TestItem = {
        id = $"{testContext.TestName}-readmany-1"
        partitionKey = "integration"
        name = "item-readmany-1"
        quantity = 1
    }

    let secondSeededItem : TestItem = {
        id = $"{testContext.TestName}-readmany-2"
        partitionKey = "integration"
        name = "item-readmany-2"
        quantity = 2
    }

    /// The values that the tests read.
    member val TestData : TestData = { FirstSeededItem = firstSeededItem; SecondSeededItem = secondSeededItem }

    override this.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        let! container = this.GetOrCreateContainerAsync (containerId, "/partitionKey", cancellationToken)

        for seededItem in [| firstSeededItem; secondSeededItem |] do
            let! createResponse =
                container.ExecuteAsync (
                    create {
                        item seededItem
                        partitionKey seededItem.partitionKey
                    },
                    cancellationToken
                )

            CosmosAssert.IsOk (createResponse.Result, $"ReadMany scenario seed create should succeed for '{seededItem.id}'.")
    }
