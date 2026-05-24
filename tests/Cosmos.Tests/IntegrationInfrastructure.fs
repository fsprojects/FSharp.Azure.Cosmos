namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Net
open System.Net.Http
open System.Net.Security
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<AutoOpen>]
module TestContextExtensions =

    type TestContext with

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

[<AbstractClass; TestClass>]
type TestBase () =

    member val TestContext = Unchecked.defaultof<TestContext> with get, set

    member this.CancellationToken = this.TestContext.CancellationTokenSource.Token

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
        let handler = new HttpClientHandler ()

        handler.ServerCertificateCustomValidationCallback <-
            (fun request _ _ errors ->
                match request.RequestUri with
                | null -> errors = SslPolicyErrors.None
                | requestUri when errors = SslPolicyErrors.None -> true
                | requestUri -> isLocalEmulatorHost requestUri
            )

        new HttpClient (handler, true)

    let client =
        new CosmosClient (
            endpoint,
            primaryKey,
            CosmosClientOptions (ConnectionMode = ConnectionMode.Gateway, HttpClientFactory = Func<HttpClient> createHttpClient)
        )
    let mutable database = ValueNone

    member _.Client = client
    member _.DatabaseId = databaseId
    member _.Database = database

    member _.InitializeAsync (cancellationToken : CancellationToken) : Task = task {
        let! createdDatabase = client.CreateDatabaseIfNotExistsAsync (databaseId, cancellationToken = cancellationToken)
        database <- ValueSome createdDatabase.Database
    }

    member _.CleanupAsync (cancellationToken : CancellationToken) : Task = task {
        match database with
        | ValueNone -> ()
        | ValueSome existingDatabase ->
            let! _ = existingDatabase.DeleteAsync (cancellationToken = cancellationToken)
            database <- ValueNone
    }

    member _.GetOrCreateContainerAsync
        (containerId : string, partitionKeyPath : string, cancellationToken : CancellationToken)
        : Task<Container>
        =
        task {
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

    abstract SeedDataAsync : cancellationToken : CancellationToken -> Task
    default _.SeedDataAsync (cancellationToken : CancellationToken) = Task.CompletedTask

    interface IAsyncDisposable with
        member this.DisposeAsync () =
            task {
                do! this.CleanupAsync (CancellationToken.None)
                client.Dispose ()
            }
            |> ValueTask

[<AbstractClass; TestClass; TestCategory "Cosmos DB Emulator">]
type IntegrationTestBase<'DatabaseTestApplicationFactory when 'DatabaseTestApplicationFactory :> DatabaseTestApplicationFactory>
    ()
    =
    inherit TestBase ()

    member val private application : 'DatabaseTestApplicationFactory voption = ValueNone with get, set

    member this.Application =
        match this.application with
        | ValueNone -> invalidOp "Application not initialized. Ensure test runs within TestInitialize/TestCleanup lifecycle."
        | ValueSome application -> application

    abstract CreateApplication : TestContext -> 'DatabaseTestApplicationFactory

    [<TestInitialize>]
    member this.Initialize () : Task = task {
        let application = this.CreateApplication (this.TestContext)
        this.application <- ValueSome application
        do! application.InitializeAsync (this.CancellationToken)
        do! application.SeedDataAsync (this.CancellationToken)
    }

    [<TestCleanup>]
    member this.Cleanup () : Task = task {
        match this.application with
        | ValueNone -> ()
        | ValueSome application ->
            do! (application :> IAsyncDisposable).DisposeAsync().AsTask ()
            this.application <- ValueNone
    }

type IntegrationTestBase () =
    inherit IntegrationTestBase<DatabaseTestApplicationFactory> ()

    override _.CreateApplication context = DatabaseTestApplicationFactory (context)
