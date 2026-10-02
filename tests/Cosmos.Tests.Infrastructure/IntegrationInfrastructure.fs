namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Net.Http
open System.Net.Security
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests

/// Extensions of the MSTest test context used by the integration test fixtures.
[<AutoOpen>]
module TestContextExtensions =

    type TestContext with

        /// <summary>
        /// Builds the identifier of the database a test creates from the test name and its data row.
        /// </summary>
        member ctx.GetTestDatabaseIdentifier () =
            match ctx.TestData with
            | null -> ctx.TestName
            | testData ->
                let dataHash =
                    testData
                    |> Array.fold
                        (fun acc item ->
                            let itemHash =
                                match item with
                                | null -> 0
                                | item -> item.GetHashCode ()

                            HashCode.Combine (acc, itemHash)
                        )
                        0
                    |> int64
                    |> abs

                $"{ctx.TestName}_{dataHash}"

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
    [<Literal>]
    let endpoint = "https://127.0.0.1:8081"

    [<Literal>]
    let primaryKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

    let buildDatabaseId () = testContext.GetTestDatabaseIdentifier ()

    let databaseId = buildDatabaseId ()

    let isLocalEmulatorHost (uri : Uri) =
        uri.Host.Equals ("localhost", StringComparison.OrdinalIgnoreCase)
        || uri.Host.Equals ("127.0.0.1", StringComparison.OrdinalIgnoreCase)

    let createHttpClient () =
        let handler =
            new HttpClientHandler (
                ServerCertificateCustomValidationCallback =
                    (fun request _ _ errors ->
                        match request.RequestUri with
                        | null -> errors = SslPolicyErrors.None
                        | requestUri when errors = SslPolicyErrors.None -> true
                        | requestUri -> isLocalEmulatorHost requestUri
                    )
            )

        new HttpClient (handler, true)

    let client =
        new CosmosClient (
            endpoint,
            primaryKey,
            CosmosClientOptions (ConnectionMode = ConnectionMode.Gateway, HttpClientFactory = Func<HttpClient> createHttpClient)
        )
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
    /// Creates the database of this fixture.
    /// </summary>
    member _.InitializeAsync (cancellationToken : CancellationToken) : Task = task {
        let! createdDatabase =
            client.CreateDatabaseIfNotExistsAsync (databaseId, cancellationToken = cancellationToken)
        database <- ValueSome createdDatabase.Database
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
