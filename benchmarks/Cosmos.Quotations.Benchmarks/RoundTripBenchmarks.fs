namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open System
open System.Threading.Tasks
open Microsoft.Azure.Cosmos
open BenchmarkDotNet.Attributes

open FSharp.Azure.Cosmos.Tests.Integration

/// The item of the round trip benchmarks, with the lower-case names the default serializer of the SDK writes as they are.
type StoredPerson = { id : string; partitionKey : string; name : string; age : int; city : string }

/// <summary>
/// What the client-side costs are compared with: one request to the Cosmos DB Emulator and back. It needs a running
/// emulator (<see cref="M:FSharp.Azure.Cosmos.Tests.Integration.Emulator.createClient"/>) and creates one database with
/// one container of 100 items, which it deletes afterwards.
/// </summary>
[<MemoryDiagnoser>]
type RoundTripBenchmarks () =

    [<Literal>]
    let PartitionKeyValue = "benchmark"

    // The prefix lets the sweep of the test runs delete the database if a run is aborted before its cleanup
    let databaseId = $"{DatabaseIdentifier.Prefix}benchmarks-{Guid.NewGuid ():N}"

    // Both are created in the setup, which BenchmarkDotNet runs in the process that measures, and not here: the
    // host process constructs this class as well and would never dispose a client
    let mutable client = Unchecked.defaultof<CosmosClient>
    let mutable container = Unchecked.defaultof<Container>

    [<GlobalSetup>]
    member _.SetupAsync () : Task = task {
        client <- Emulator.createClient ()
        let! database = client.CreateDatabaseAsync databaseId
        let! created = database.Database.CreateContainerAsync (ContainerProperties ("people", "/partitionKey"))
        container <- created.Container

        for index in 0..99 do
            let item = {
                id = $"person-{index}"
                partitionKey = PartitionKeyValue
                name = $"Person {index}"
                age = index
                city = if index % 2 = 0 then "Kyiv" else "Lviv"
            }

            let! _ = container.CreateItemAsync (item, PartitionKey PartitionKeyValue)
            ()
    }

    [<GlobalCleanup>]
    member _.CleanupAsync () : Task = task {
        let! _ = client.GetDatabase(databaseId).DeleteAsync()
        client.Dispose ()
    }

    [<Benchmark(Description = "Point read of one item")>]
    member _.ReadItemAsync () : Task<ItemResponse<StoredPerson>> =
        container.ReadItemAsync<StoredPerson>("person-50", PartitionKey PartitionKeyValue)

    [<Benchmark(Description = "Query in one partition, 10 of 100 items")>]
    member _.QueryAsync () : Task<int> = task {
        let definition =
            QueryDefinition("SELECT VALUE p FROM p WHERE ((p[\"age\"] >= @minAge) AND (p[\"city\"] = @city))")
                .WithParameter("@minAge", 80)
                .WithParameter("@city", "Kyiv")

        use iterator =
            container.GetItemQueryIterator<StoredPerson>(
                definition,
                requestOptions = QueryRequestOptions (PartitionKey = PartitionKey PartitionKeyValue)
            )

        let! page = iterator.ReadNextAsync ()
        return page.Count
    }
