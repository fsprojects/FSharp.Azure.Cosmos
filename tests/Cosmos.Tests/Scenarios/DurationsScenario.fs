namespace FSharp.Azure.Cosmos.Tests.Integration.DurationsScenario

open System
open System.Collections.Immutable
open System.Text.Json
open System.Threading
open System.Threading.Tasks

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration

// The record is public because System.Text.Json does not write the properties of an internal record. Only the files
// that open this namespace see its labels. Thus, the record type inference of the other test files does not use them.
/// A document that holds one duration.
[<CLIMutable>]
type DurationItem = { Id : string; PartitionKey : string; Duration : TimeSpan }

/// The serializer options of the clients that write and read the durations.
[<RequireQualifiedAccess>]
module SerializerOptions =

    /// <summary>
    /// The options with the default converter of <see cref="TimeSpan"/>, which writes text. The queries use camel case
    /// names, and Cosmos DB requires the name <c>id</c>.
    /// </summary>
    let text = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// <summary>
    /// The text options with <see cref="TimeSpanTicksConverter"/>, which writes a number of ticks.
    /// </summary>
    let ticks =
        let options = JsonSerializerOptions text
        options.Converters.Add (TimeSpanTicksConverter ())
        options

/// The values that the tests of this scenario read.
[<Struct>]
type TestData = {
    /// The durations of each partition, in the order of their creation. They differ in the length of their text and in
    /// their sign. Thus, no accident puts the text in time order.
    Durations : ImmutableArray<TimeSpan>
    /// <summary>
    /// The partition key of the documents that <see cref="TimeSpanTicksConverter"/> wrote.
    /// </summary>
    TicksPartition : string
    /// The partition key of the documents that the default converter wrote, as old documents hold them.
    TextPartition : string
}

/// <summary>
/// Writes the same durations two times into the container of <see cref="OperationTestBase{T}"/>: through
/// <see cref="TimeSpanTicksConverter"/>, which writes ticks, into <see cref="TestData.TicksPartition"/>, and through the
/// default converter, which writes text, into <see cref="TestData.TextPartition"/>.
/// </summary>
type TestApplicationFactory (testContext : TestContext) =
    inherit OperationTestApplicationFactory (testContext)

    let durations =
        seq {
            TimeSpan.FromDays 2.0
            TimeSpan.FromDays 10.0
            TimeSpan.FromHours 23.0
            TimeSpan.FromHours 1.0
            TimeSpan.FromDays 1.0
            TimeSpan.FromMinutes (-5.0)
        }
        |> ImmutableArray.CreateRange

    let ticksPartition = "ticks"
    let textPartition = "text"

    let documentsIn (partition : string) =
        durations
        |> Seq.mapi (fun index duration -> { Id = $"{partition}-{index}"; PartitionKey = partition; Duration = duration })
        |> ImmutableArray.CreateRange

    let ticksDocuments = documentsIn ticksPartition
    let textDocuments = documentsIn textPartition

    /// The values that the tests read.
    member val TestData : TestData =
        {
            Durations = durations
            TicksPartition = ticksPartition
            TextPartition = textPartition
        }

    // A client of its own writes the documents, because its serializer decides how Cosmos DB stores a TimeSpan
    member private this.CreateAsync (options, documents : ImmutableArray<DurationItem>, cancellationToken) : Task = task {
        use client = Emulator.createClientWithJson options
        let container = this.GetContainerOf client

        for document in documents do
            let! response =
                container.ExecuteAsync (
                    create {
                        item document
                        partitionKey document.PartitionKey
                    },
                    cancellationToken
                )

            CosmosAssert.IsOk (response.Result, $"Seed create should succeed for the document '{document.Id}'.")
    }

    override this.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        do! this.CreateAsync (SerializerOptions.ticks, ticksDocuments, cancellationToken)
        do! this.CreateAsync (SerializerOptions.text, textDocuments, cancellationToken)
    }
