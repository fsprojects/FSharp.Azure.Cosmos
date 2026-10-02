namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

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
type DatabaseTestApplicationFactory (testContext : TestContext) =
    let databaseId = DatabaseIdentifier.ofTestContext testContext
    let client = Emulator.createClient ()
    let mutable database = ValueNone

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
    /// <see cref="InitializeAsync"/> and after <see cref="CleanupAsync"/>.
    /// </summary>
    member _.Database = database

    /// <summary>
    /// Creates the database of this fixture, empty: a database of the same identifier that an earlier run left behind
    /// is deleted and created anew.
    /// </summary>
    member _.InitializeAsync (cancellationToken : CancellationToken) : Task = task {
        let! response = client.CreateDatabaseIfNotExistsAsync (databaseId, cancellationToken = cancellationToken)

        if response.StatusCode = HttpStatusCode.Created then
            database <- ValueSome response.Database
        else
            // The identifier is stable, so this is the database of this very test from an earlier run that
            // was aborted, or whose cleanup failed, too recently for the leftover sweep to delete it. Its
            // containers and items would make the test fail, such as a seed that conflicts with an item
            // the earlier run created, so the test starts over with an empty database.
            testContext.WriteLine
                $"Recreating the test database '{databaseId}' that an earlier run of this test left behind."

            let! _ = response.Database.DeleteAsync (cancellationToken = cancellationToken)
            let! recreated = client.CreateDatabaseAsync (databaseId, cancellationToken = cancellationToken)
            database <- ValueSome recreated.Database
    }

    /// <summary>
    /// Deletes the database of this fixture.
    /// </summary>
    member _.CleanupAsync (cancellationToken : CancellationToken) : Task = task {
        match database with
        | ValueNone -> ()
        | ValueSome existingDatabase ->
            let! _ = existingDatabase.DeleteAsync (cancellationToken = cancellationToken)
            database <- ValueNone
    }

    /// <summary>
    /// Creates a container in the database of this fixture unless it exists, and returns it.
    /// </summary>
    member _.GetOrCreateContainerAsync
        (containerId : string, partitionKeyPath : string, cancellationToken : CancellationToken)
        : Task<Container>
        = task {
        let database =
            match database with
            | ValueSome existingDatabase -> existingDatabase
            | ValueNone -> invalidOp "Database is not initialized."

        let! containerResponse =
            database.CreateContainerIfNotExistsAsync (
                ContainerProperties (containerId, partitionKeyPath),
                cancellationToken = cancellationToken
            )

        return containerResponse.Container
    }

    /// <summary>
    /// Seeds the data of a scenario after the database is created; does nothing unless overridden.
    /// </summary>
    abstract SeedDataAsync : cancellationToken : CancellationToken -> Task
    default _.SeedDataAsync (cancellationToken : CancellationToken) = Task.CompletedTask

    interface IAsyncDisposable with
        /// <inheritdoc />
        member this.DisposeAsync () =
            task {
                try
                    do! this.CleanupAsync (CancellationToken.None)
                finally
                    client.Dispose ()
            }
            |> ValueTask

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
            do! (application :> IAsyncDisposable).DisposeAsync()
            this.application <- ValueNone
    }

/// <summary>
/// The base class of emulator-backed test classes that need no scenario: an empty database per test.
/// </summary>
type IntegrationTestBase () =
    inherit IntegrationTestBase<DatabaseTestApplicationFactory> ()

    /// <inheritdoc />
    override _.CreateApplication context = DatabaseTestApplicationFactory (context)
