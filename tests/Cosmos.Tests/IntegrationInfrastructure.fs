namespace Tests.Integration

open System
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<AbstractClass; TestClass>]
type TestBase () =

    member val TestContext = Unchecked.defaultof<TestContext> with get, set

    member this.CancellationToken = this.TestContext.CancellationTokenSource.Token

type DatabaseTestApplicationFactory(testContext : TestContext) =
    let endpoint = "https://127.0.0.1:8081"

    let primaryKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

    let buildDatabaseId () =
        let className =
            match testContext.FullyQualifiedTestClassName with
            | null -> "CosmosTests"
            | fullyQualifiedTestClassName when String.IsNullOrWhiteSpace fullyQualifiedTestClassName -> "CosmosTests"
            | fullyQualifiedTestClassName -> fullyQualifiedTestClassName

        let sanitizedClassName = Regex.Replace(className, "[^a-zA-Z0-9-_]", "-")
        let shortClassName = sanitizedClassName[..(min 40 (sanitizedClassName.Length - 1))]
        $"{shortClassName}-{Guid.NewGuid():N}"

    let databaseId = buildDatabaseId ()
    let client = new CosmosClient(endpoint, primaryKey, CosmosClientOptions(ConnectionMode = ConnectionMode.Gateway))
    let mutable database = ValueNone

    member _.Client = client
    member _.DatabaseId = databaseId
    member _.Database = database

    member _.InitializeAsync(cancellationToken : CancellationToken) : Task =
        task {
            let! createdDatabase = client.CreateDatabaseIfNotExistsAsync(databaseId, cancellationToken = cancellationToken)
            database <- ValueSome createdDatabase.Database
        }

    member _.CleanupAsync(cancellationToken : CancellationToken) : Task =
        task {
            match database with
            | ValueNone -> ()
            | ValueSome existingDatabase ->
                let! _ = existingDatabase.DeleteAsync(cancellationToken = cancellationToken)
                database <- ValueNone
        }

    abstract SeedDataAsync : CancellationToken -> Task
    default _.SeedDataAsync(_ : CancellationToken) = Task.CompletedTask

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            task {
                do! this.CleanupAsync(CancellationToken.None)
                client.Dispose()
            }
            |> ValueTask

[<AbstractClass; TestClass; TestCategory "Cosmos DB Emulator">]
type IntegrationTestBase () =
    inherit TestBase()

    [<DefaultValue false>]
    val mutable private application : DatabaseTestApplicationFactory voption

    member this.Application =
        match this.application with
        | ValueNone -> invalidOp "Integration test application is not initialized."
        | ValueSome application -> application

    abstract CreateApplication : TestContext -> DatabaseTestApplicationFactory
    default _.CreateApplication context = DatabaseTestApplicationFactory(context)

    [<TestInitialize>]
    member this.Initialize() : Task =
        task {
            let application = this.CreateApplication(this.TestContext)
            this.application <- ValueSome application
            do! application.InitializeAsync(this.CancellationToken)
            do! application.SeedDataAsync(this.CancellationToken)
        }

    [<TestCleanup>]
    member this.Cleanup() : Task =
        task {
            match this.application with
            | ValueNone -> ()
            | ValueSome application ->
                do! (application :> IAsyncDisposable).DisposeAsync().AsTask()
                this.application <- ValueNone
        }
