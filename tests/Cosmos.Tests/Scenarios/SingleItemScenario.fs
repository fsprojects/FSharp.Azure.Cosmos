namespace FSharp.Azure.Cosmos.Tests.Integration.SingleItemScenario

open System.Threading
open System.Threading.Tasks

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration

/// The values that the tests of this scenario read.
[<Struct>]
type TestData = {
    /// The item that the scenario writes.
    SeededItem : TestItem
}

/// <summary>
/// Writes one item into the container of <see cref="OperationTestBase{T}"/>.
/// </summary>
type TestApplicationFactory (testContext : TestContext) =
    inherit DatabaseTestApplicationFactory (testContext)

    // The container of OperationTestBase
    let containerId = "operation-tests"

    let seededItem : TestItem = {
        id = $"{testContext.TestName}-read"
        partitionKey = "integration"
        name = "item-read"
        quantity = 1
    }

    /// The values that the tests read.
    member val TestData : TestData = { SeededItem = seededItem }

    override this.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
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
