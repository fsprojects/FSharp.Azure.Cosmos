namespace FSharp.Azure.Cosmos.Tests.Integration

open System.Net
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type ReadManyOperationIntegrationTests () =
    inherit OperationTestBase ()

    [<TestMethod>]
    member this.``ReadMany execute returns matching items`` () : Task = task {
        let! container = this.GetContainer ()
        let firstItem = this.NewItem "readmany-1"
        let secondItem = this.NewItem "readmany-2"

        let! firstCreateResponse =
            container.ExecuteAsync (
                create {
                    item firstItem
                    partitionKey firstItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (firstCreateResponse.Result, "First seed create should succeed.")

        let! secondCreateResponse =
            container.ExecuteAsync (
                create {
                    item secondItem
                    partitionKey secondItem.partitionKey
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (secondCreateResponse.Result, "Second seed create should succeed.")

        let! readManyResponse =
            container.ExecuteAsync (
                readMany {
                    item firstItem.id firstItem.partitionKey
                    item secondItem.id secondItem.partitionKey
                },
                this.CancellationToken
            )

        match readManyResponse.Result with
        | ReadManyResult.Ok (feed : FeedResponse<TestItem>) ->
            let returnedIds = feed |> Seq.map _.id |> Set.ofSeq
            Assert.IsTrue (feed.Count = 2, "ReadMany should return requested number of items.")
            Assert.IsTrue (returnedIds.Contains firstItem.id, "ReadMany should include first item.")
            Assert.IsTrue (returnedIds.Contains secondItem.id, "ReadMany should include second item.")
            Assert.IsTrue (readManyResponse.HttpStatusCode = HttpStatusCode.OK, "ReadMany should return HTTP 200.")
        | result -> Assert.Fail ($"Expected read many success, got {result}.")
    }
