namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

// A module of its own: the labels id and partitionKey would otherwise take the record labels of the other test files. The module is public because System.Text.Json writes no property of an internal record.
module TimeSpanTicksConverterIntegrationTestData =

    [<CLIMutable>]
    type DurationItem = { id : string; partitionKey : string; duration : TimeSpan }

/// <summary>
/// Checks on the Cosmos DB Emulator that <see cref="TimeSpanTicksConverter"/> lets Cosmos DB sort and compare a
/// <see cref="TimeSpan"/> in time order, and that the text of the default converter does not.
/// </summary>
[<TestClass; TimeSpanTicksConverterTestCategory>]
type TimeSpanTicksConverterIntegrationTests () =
    inherit OperationTestBase ()

    static let partition = "integration"

    // The durations differ in the length of the text and in the sign, so no accident puts the text in time order
    static let durations = [|
        TimeSpan.FromDays 2.0
        TimeSpan.FromDays 10.0
        TimeSpan.FromHours 23.0
        TimeSpan.FromHours 1.0
        TimeSpan.FromDays 1.0
        TimeSpan.FromMinutes (-5.0)
    |]

    static let ticksOptions =
        let options = JsonSerializerOptions ()
        options.Converters.Add (TimeSpanTicksConverter ())
        options

    static let textOptions = JsonSerializerOptions ()

    // The container of this test as seen by another client: the test fixture creates the container, one client per
    // serializer reads and writes it
    member private this.ContainerOf (client : CosmosClient) : Task<Container> = task {
        let! container = this.GetContainer ()
        return client.GetContainer (container.Database.Id, container.Id)
    }

    member private this.CreateAsync (container : Container, name : string, duration : TimeSpan) : Task = task {
        let document : TimeSpanTicksConverterIntegrationTestData.DurationItem = {
            id = $"{this.TestContext.TestName}-{name}"
            partitionKey = partition
            duration = duration
        }

        let! response =
            container.ExecuteAsync (
                create {
                    item document
                    partitionKey partition
                },
                this.CancellationToken
            )

        CosmosAssert.IsOk (response.Result, $"Create should succeed for the duration {duration}.")
    }

    member private this.SeedAsync (container : Container) : Task = task {
        for index in 0 .. durations.Length - 1 do
            do! this.CreateAsync (container, string index, durations[index])
    }

    member private this.QueryAsync (container : Container, query : QueryDefinition) : Task<TimeSpan[]> = task {
        let options = QueryRequestOptions (PartitionKey = Nullable (PartitionKey partition))
        use iterator = container.GetItemQueryIterator<TimeSpan>(query, requestOptions = options)
        let results = ResizeArray<TimeSpan>()

        while iterator.HasMoreResults do
            let! page = iterator.ReadNextAsync this.CancellationToken
            results.AddRange page

        return results.ToArray ()
    }

    static member private OrderedByDuration =
        QueryDefinition("SELECT VALUE c.duration FROM c WHERE c.partitionKey = @partitionKey ORDER BY c.duration")
            .WithParameter("@partitionKey", partition)

    [<TestMethod>]
    member this.``ORDER BY returns the durations in time order when the converter stores ticks`` () : Task = task {
        use client = Emulator.createClientWithJson ticksOptions
        let! container = this.ContainerOf client
        do! this.SeedAsync container

        let! results = this.QueryAsync (container, TimeSpanTicksConverterIntegrationTests.OrderedByDuration)

        CollectionAssert.AreEqual (Array.sort durations, results, "ORDER BY should return the durations in time order.")
    }

    [<TestMethod>]
    member this.``A TimeSpan parameter filters the durations by time when the converter stores ticks`` () : Task = task {
        use client = Emulator.createClientWithJson ticksOptions
        let! container = this.ContainerOf client
        do! this.SeedAsync container

        let minimum = TimeSpan.FromHours 23.0

        let query =
            QueryDefinition(
                "SELECT VALUE c.duration FROM c WHERE c.partitionKey = @partitionKey AND c.duration >= @minimum ORDER BY c.duration"
            )
                .WithParameter("@partitionKey", partition)
                .WithParameter("@minimum", minimum)

        let! results = this.QueryAsync (container, query)

        let expected =
            durations
            |> Array.filter (fun duration -> duration >= minimum)
            |> Array.sort
        Assert.HasCount (4, expected, "The test data should have four durations of 23 hours or more.")
        CollectionAssert.AreEqual (expected, results, "The filter should keep the durations of 23 hours or more.")
    }

    [<TestMethod>]
    member this.``A document that holds the text of the default converter is read with the converter`` () : Task = task {
        use textClient = Emulator.createClientWithJson textOptions
        let! textContainer = this.ContainerOf textClient
        do! this.CreateAsync (textContainer, "text", TimeSpan.FromHours 26.0)

        use ticksClient = Emulator.createClientWithJson ticksOptions
        let! ticksContainer = this.ContainerOf ticksClient

        let! response =
            ticksContainer.ReadItemAsync<TimeSpanTicksConverterIntegrationTestData.DurationItem>(
                $"{this.TestContext.TestName}-text",
                PartitionKey partition,
                cancellationToken = this.CancellationToken
            )

        Assert.AreEqual (
            TimeSpan.FromHours 26.0,
            response.Resource.duration,
            "The converter should read the text form of an old document."
        )
    }

    [<TestMethod>]
    member this.``ORDER BY returns the durations in the order of their text when the default converter stores text`` () : Task =
        task {
            use client = Emulator.createClientWithJson textOptions
            let! container = this.ContainerOf client
            do! this.SeedAsync container

            let! results = this.QueryAsync (container, TimeSpanTicksConverterIntegrationTests.OrderedByDuration)

            let text (duration : TimeSpan) = JsonSerializer.Serialize (duration, textOptions)

            let expected =
                durations
                |> Array.sortWith (fun first second -> String.CompareOrdinal (text first, text second))

            CollectionAssert.AreEqual (expected, results, "ORDER BY should return the durations in the order of their text.")

            CollectionAssert.AreNotEqual (
                Array.sort durations,
                results,
                "The order of the text should differ from the time order, which is why the converter exists."
            )
        }
