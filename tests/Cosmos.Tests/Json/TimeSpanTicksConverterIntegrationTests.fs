namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Text.Json
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration.DurationsScenario

/// <summary>
/// Checks on the Cosmos DB Emulator that Cosmos DB sorts and compares a <see cref="TimeSpan"/> in time order when
/// <see cref="TimeSpanTicksConverter"/> writes it. Cosmos DB does not do this for the text of the default converter.
/// </summary>
[<TestClass; JsonConverterTestCategory; TimeSpanTestCategory>]
type TimeSpanTicksConverterIntegrationTests () =
    inherit OperationTestBase<DurationsScenario.TestApplicationFactory> ()

    override _.CreateApplication context = DurationsScenario.TestApplicationFactory (context)

    member private this.QueryAsync<'Item> (container : Container, partition : string, query : QueryDefinition) = task {
        let options = QueryRequestOptions (PartitionKey = Nullable (PartitionKey partition))
        use iterator = container.GetItemQueryIterator<'Item>(query, requestOptions = options)
        let results = ResizeArray<'Item>()

        while iterator.HasMoreResults do
            let! page = iterator.ReadNextAsync this.CancellationToken
            results.AddRange page

        return results.ToArray ()
    }

    static member private OrderedByDuration (partition : string) =
        QueryDefinition ("SELECT VALUE c.duration FROM c WHERE c.partitionKey = @partitionKey ORDER BY c.duration")
        |> _.WithParameter("@partitionKey", partition)

    [<TestMethod>]
    member this.``ORDER BY returns the durations in time order when the converter stores ticks`` () : Task = task {
        let { Durations = durations; TicksPartition = partition } = this.Application.TestData
        use client = Emulator.createClientWithJson SerializerOptions.ticks
        let container = this.Application.GetContainerOf client

        let! results =
            this.QueryAsync<TimeSpan>(container, partition, TimeSpanTicksConverterIntegrationTests.OrderedByDuration partition)

        CollectionAssert.AreEqual (
            durations |> Seq.sort |> Seq.toArray,
            results,
            "ORDER BY should return the durations in time order."
        )
    }

    [<TestMethod>]
    member this.``A TimeSpan parameter filters the durations by time when the converter stores ticks`` () : Task = task {
        let { Durations = durations; TicksPartition = partition } = this.Application.TestData
        use client = Emulator.createClientWithJson SerializerOptions.ticks
        let container = this.Application.GetContainerOf client

        let minimum = TimeSpan.FromHours 23.0

        let query =
            QueryDefinition (
                "SELECT VALUE c.duration FROM c WHERE c.partitionKey = @partitionKey AND c.duration >= @minimum ORDER BY c.duration"
            )
            |> _.WithParameter("@partitionKey", partition)
            |> _.WithParameter("@minimum", minimum)

        let! results = this.QueryAsync<TimeSpan>(container, partition, query)

        let expected =
            durations
            |> Seq.filter ((<=) minimum)
            |> Seq.sort
            |> Seq.toArray
        Assert.HasCount (4, expected, "The test data should have four durations of 23 hours or more.")
        CollectionAssert.AreEqual (expected, results, "The filter should keep the durations of 23 hours or more.")
    }

    [<TestMethod>]
    member this.``Documents that hold the text of the default converter are read with the converter`` () : Task = task {
        let { Durations = durations; TextPartition = partition } = this.Application.TestData
        use client = Emulator.createClientWithJson SerializerOptions.ticks
        let container = this.Application.GetContainerOf client

        let query =
            QueryDefinition ("SELECT * FROM c WHERE c.partitionKey = @partitionKey")
            |> _.WithParameter("@partitionKey", partition)

        let! documents = this.QueryAsync<DurationItem>(container, partition, query)

        CollectionAssert.AreEquivalent (
            durations |> Seq.toArray,
            documents |> Array.map _.Duration,
            "The converter should read the text form of the old documents."
        )
    }

    [<TestMethod>]
    member this.``ORDER BY returns the durations in the order of their text when the default converter stores text`` () : Task =
        task {
            let { Durations = durations; TextPartition = partition } = this.Application.TestData
            use client = Emulator.createClientWithJson SerializerOptions.text
            let container = this.Application.GetContainerOf client

            let! results =
                this.QueryAsync<TimeSpan>(
                    container,
                    partition,
                    TimeSpanTicksConverterIntegrationTests.OrderedByDuration partition
                )

            let text (duration : TimeSpan) = JsonSerializer.Serialize (duration, SerializerOptions.text)

            let expected =
                durations
                |> Seq.sortWith (fun first second -> String.CompareOrdinal (text first, text second))
                |> Seq.toArray

            CollectionAssert.AreEqual (expected, results, "ORDER BY should return the durations in the order of their text.")

            CollectionAssert.AreNotEqual (
                durations |> Seq.sort |> Seq.toArray,
                results,
                "The order of the text should differ from the time order, which is why the converter exists."
            )
        }
