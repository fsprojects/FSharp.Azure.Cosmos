namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Collections.Generic
open System.Net
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open IcedTasks

open FSharp.Azure.Cosmos.Tests

/// <summary>
/// The base class of every test class: holds the <see cref="TestContext"/> MSTest injects.
/// </summary>
[<AbstractClass; TestClass>]
type TestBase () =

    /// <summary>
    /// The context MSTest injects into the test class instance.
    /// </summary>
    member val TestContext = Unchecked.defaultof<TestContext> with get, set

    /// <summary>
    /// The token MSTest cancels when the test times out or the run is aborted.
    /// </summary>
    member this.CancellationToken = this.TestContext.CancellationTokenSource.Token

/// <summary>
/// The fixture of one emulator-backed test: a <see cref="CosmosClient"/> and a database of its own, created before the
/// test and deleted after it. Scenarios derive from it and override <see cref="SeedDataAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Before it creates its database in <see cref="InitializeAsync"/>, the fixture takes one permit of the emulator's
/// <see cref="PartitionBudget"/> for each container it may create (<see cref="ContainerCount"/>), all at once, and
/// <see cref="CleanupAsync"/> gives back exactly the permits it took. Database and container creation is serialised
/// across parallel tests. Both keep the emulator within the limits it answers with HTTP 500 and 503 beyond.
/// </para>
/// <para>
/// Cleanup is best-effort: a database that cannot be deleted is written to the <see cref="TestContext"/> and left to
/// the next run of the same test, which recreates it, or to <see cref="Emulator.deleteLeftoverDatabasesAsync"/> once
/// it is old enough, so cleanup never fails a test, and it tolerates a fixture whose initialization failed half-way.
/// </para>
/// </remarks>
type DatabaseTestApplicationFactory (testContext : TestContext) =

    // Process-wide: every fixture of the test run shares the one emulator
    static let partitionBudget = PartitionBudget (Emulator.readPartitionCount ())

    // Serialises database and container creation across parallel tests: the emulator answers HTTP 500 and 503 when
    // many creations arrive at once
    static let creation = new SemaphoreSlim (1, 1)

    let databaseId = DatabaseIdentifier.ofTestContext testContext
    let client = Emulator.createClient ()
    let createdContainerIds = HashSet<string>(StringComparer.Ordinal)
    let mutable database = ValueNone
    let mutable acquiredPartitions = 0

    let createSerializedAsync (cancellationToken : CancellationToken) (create : unit -> Task<'Result>) : Task<'Result> = task {
        // Outside the try: a cancelled wait must not release a turn it never got
        do! creation.WaitAsync cancellationToken

        try
            return! create ()
        finally
            creation.Release () |> ignore
    }

    /// <summary>
    /// The client connected to the emulator.
    /// </summary>
    member _.Client = client

    /// <summary>
    /// The identifier of the database this fixture creates.
    /// </summary>
    member _.DatabaseId = databaseId

    /// <summary>
    /// The database this fixture created; <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> before
    /// <see cref="InitializeAsync"/> and after <see cref="CleanupAsync"/>. Create containers through
    /// <see cref="GetOrCreateContainerAsync"/>, which counts them against the <see cref="PartitionBudget"/>.
    /// </summary>
    member _.Database = database

    /// <summary>
    /// The number of containers this fixture creates at most, each of which costs one partition of the emulator.
    /// Override it in a scenario that creates more than one container.
    /// </summary>
    abstract ContainerCount : int
    default _.ContainerCount = 1

    /// <summary>
    /// Takes the <see cref="PartitionBudget"/> permits of <see cref="ContainerCount"/> containers and creates the
    /// database of this fixture, empty: a database of the same identifier that an earlier run left behind is deleted
    /// and created anew.
    /// </summary>
    member this.InitializeAsync (cancellationToken : CancellationToken) : Task = task {
        let! acquired = partitionBudget.AcquireAsync (this.ContainerCount, cancellationToken)
        acquiredPartitions <- acquired

        let! createdDatabase =
            createSerializedAsync
                cancellationToken
                (fun () -> task {
                    let! response = client.CreateDatabaseIfNotExistsAsync (databaseId, cancellationToken = cancellationToken)

                    if response.StatusCode = HttpStatusCode.Created then
                        return response.Database
                    else
                        // The identifier is stable, so this is the database of this very test from an earlier run that
                        // was aborted, or whose cleanup failed, too recently for the leftover sweep to delete it. Its
                        // containers and items would make the test fail, such as a seed that conflicts with an item
                        // the earlier run created, so the test starts over with an empty database.
                        testContext.WriteLine
                            $"Recreating the test database '{databaseId}' that an earlier run of this test left behind."

                        let! _ = response.Database.DeleteAsync (cancellationToken = cancellationToken)
                        let! recreated = client.CreateDatabaseAsync (databaseId, cancellationToken = cancellationToken)
                        return recreated.Database
                })

        database <- ValueSome createdDatabase
    }

    /// <summary>
    /// Deletes the database of this fixture and gives its permits back to the <see cref="PartitionBudget"/>.
    /// Best-effort: a failed deletion is written to the <see cref="TestContext"/> instead of failing the test.
    /// </summary>
    member _.CleanupAsync (cancellationToken : CancellationToken) : Task = task {
        try
            match database with
            | ValueNone -> ()
            | ValueSome existingDatabase ->
                database <- ValueNone

                try
                    let! _ = existingDatabase.DeleteAsync (cancellationToken = cancellationToken)
                    ()
                with ex ->
                    testContext.WriteLine
                        $"Best-effort cleanup could not delete the test database '{databaseId}', so the next run of this test recreates it or the sweep of leftover databases deletes it once it is old enough. {ex.GetType().Name}: {ex.Message}"
        finally
            partitionBudget.Release acquiredPartitions
            acquiredPartitions <- 0
    }

    /// <summary>
    /// Creates a container in the database of this fixture unless it exists, and returns it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The database is not initialized, or the container would exceed the <see cref="ContainerCount"/> this fixture
    /// took partition permits for.
    /// </exception>
    member _.GetOrCreateContainerAsync
        (containerProperties : ContainerProperties, cancellationToken : CancellationToken)
        : Task<Container>
        = task {
        let database =
            match database with
            | ValueSome existingDatabase -> existingDatabase
            | ValueNone -> invalidOp "Database is not initialized."

        return!
            createSerializedAsync
                cancellationToken
                (fun () -> task {
                    // Checked under the creation lock, which also guards createdContainerIds
                    if
                        not (createdContainerIds.Contains containerProperties.Id)
                        && createdContainerIds.Count >= acquiredPartitions
                    then
                        invalidOp
                            $"The fixture of '{databaseId}' took partition permits for {acquiredPartitions} container(s) and cannot create '{containerProperties.Id}' as well; override ContainerCount in its scenario."

                    let! containerResponse =
                        database.CreateContainerIfNotExistsAsync (containerProperties, cancellationToken = cancellationToken)

                    createdContainerIds.Add containerProperties.Id |> ignore
                    return containerResponse.Container
                })
    }

    /// <summary>
    /// Creates a container with a single partition key path in the database of this fixture unless it exists, and
    /// returns it.
    /// </summary>
    member this.GetOrCreateContainerAsync
        (containerId : string, partitionKeyPath : string, cancellationToken : CancellationToken)
        : Task<Container>
        =
        this.GetOrCreateContainerAsync (ContainerProperties (containerId, partitionKeyPath), cancellationToken)

    /// <summary>
    /// Seeds the data of a scenario after the database is created; does nothing unless overridden.
    /// </summary>
    abstract SeedDataAsync : cancellationToken : CancellationToken -> Task
    default _.SeedDataAsync (cancellationToken : CancellationToken) = Task.CompletedTask

    interface IAsyncDisposable with
        /// <inheritdoc />
        member this.DisposeAsync () = valueTaskUnit {
            try
                // Read at cleanup time: MSTest gives [<TestCleanup>] a fresh token source, so a test that timed out
                // still deletes its database
                do! this.CleanupAsync testContext.CancellationToken
            finally
                client.Dispose ()
        }

/// <summary>
/// The base class of emulator-backed test classes: creates the fixture of every test, a
/// <see cref="DatabaseTestApplicationFactory"/>, in its <see cref="TestInitializeAttribute"/> method and disposes it in
/// its <see cref="TestCleanupAttribute"/> method.
/// </summary>
[<AbstractClass; TestClass; CosmosDbEmulatorTestCategory>]
type IntegrationTestBase<'DatabaseTestApplicationFactory when 'DatabaseTestApplicationFactory :> DatabaseTestApplicationFactory>
    ()
    =
    inherit TestBase ()

    member val private application : 'DatabaseTestApplicationFactory voption = ValueNone with get, set

    /// <summary>
    /// The fixture of the running test.
    /// </summary>
    member this.Application =
        match this.application with
        | ValueNone -> invalidOp "Application not initialized. Ensure test runs within TestInitialize/TestCleanup lifecycle."
        | ValueSome application -> application

    /// <summary>
    /// Creates the fixture of the running test.
    /// </summary>
    abstract CreateApplication : TestContext -> 'DatabaseTestApplicationFactory

    /// <summary>
    /// Creates the fixture through <see cref="CreateApplication"/>, its database through
    /// <see cref="DatabaseTestApplicationFactory.InitializeAsync"/> and the data of its scenario through
    /// <see cref="DatabaseTestApplicationFactory.SeedDataAsync"/>.
    /// </summary>
    [<TestInitialize>]
    member this.Initialize () : Task = task {
        let application = this.CreateApplication (this.TestContext)
        // Stored before initialization: MSTest runs [<TestCleanup>] after a failed [<TestInitialize>] as well, and
        // the cleanup gives back whatever a half-initialized fixture already holds
        this.application <- ValueSome application
        do! application.InitializeAsync (this.CancellationToken)
        do! application.SeedDataAsync (this.CancellationToken)
    }

    /// <summary>
    /// Disposes <see cref="Application"/>, deleting its database.
    /// </summary>
    [<TestCleanup>]
    member this.Cleanup () : Task = task {
        match this.application with
        | ValueNone -> ()
        | ValueSome application ->
            this.application <- ValueNone
            do! (application :> IAsyncDisposable).DisposeAsync()
    }

/// <summary>
/// The base class of emulator-backed test classes that need no scenario: an empty database per test.
/// </summary>
type IntegrationTestBase () =
    inherit IntegrationTestBase<DatabaseTestApplicationFactory> ()

    /// <inheritdoc />
    override _.CreateApplication context = DatabaseTestApplicationFactory (context)
