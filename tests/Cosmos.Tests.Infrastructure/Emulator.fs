/// <summary>
/// The Cosmos DB Emulator the tests run against: its connection settings, <see cref="createClient"/>, which creates the
/// client of every <see cref="DatabaseTestApplicationFactory"/>, and <see cref="deleteLeftoverDatabasesAsync"/>, which
/// sweeps the test databases left behind.
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Integration.Emulator

open System
open System.Globalization
open System.Net.Http
open System.Net.Security
open System.Threading
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// The environment variable that overrides <see cref="DefaultEndpoint"/>.
/// </summary>
[<Literal>]
let EndpointVariable = "COSMOS_EMULATOR_ENDPOINT"

/// <summary>
/// The environment variable that overrides <see cref="DefaultKey"/>.
/// </summary>
[<Literal>]
let KeyVariable = "COSMOS_EMULATOR_KEY"

/// <summary>
/// The documented endpoint of a local emulator.
/// </summary>
[<Literal>]
let DefaultEndpoint = "https://127.0.0.1:8081"

/// <summary>
/// The documented, publicly known primary key of the emulator.
/// </summary>
[<Literal>]
let DefaultKey =
    "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

let private readVariable (name : string) =
    match Environment.GetEnvironmentVariable name with
    | null -> ValueNone
    | value when String.IsNullOrWhiteSpace value -> ValueNone
    | value -> ValueSome (value.Trim ())

/// <summary>
/// The endpoint the tests connect to: the value of <see cref="EndpointVariable"/> when it is set, so that the same
/// tests run against another emulator, otherwise <see cref="DefaultEndpoint"/>.
/// </summary>
let endpoint =
    readVariable EndpointVariable
    |> ValueOption.defaultValue DefaultEndpoint

/// <summary>
/// The key the tests authenticate with: the value of <see cref="KeyVariable"/> when it is set, otherwise
/// <see cref="DefaultKey"/>.
/// </summary>
let key =
    readVariable KeyVariable
    |> ValueOption.defaultValue DefaultKey

/// <summary>
/// The environment variable that overrides <see cref="DefaultLeftoverAgeMinutes"/>.
/// </summary>
[<Literal>]
let LeftoverAgeVariable = "COSMOS_TEST_LEFTOVER_AGE_MINUTES"

/// <summary>
/// How many minutes a test database must stay unmodified before <see cref="deleteLeftoverDatabasesAsync"/> deletes it
/// by default: far longer than any test keeps its database, so that the sweep keeps the live databases of other test
/// processes.
/// </summary>
[<Literal>]
let DefaultLeftoverAgeMinutes = 60

/// <summary>
/// Reads how long a test database must stay unmodified before <see cref="deleteLeftoverDatabasesAsync"/> deletes it:
/// the value of <see cref="LeftoverAgeVariable"/> in whole minutes when it is set, otherwise
/// <see cref="DefaultLeftoverAgeMinutes"/>. <c>0</c> makes the sweep delete every test database, which is safe only
/// while no other test process uses the emulator.
/// </summary>
/// <exception cref="InvalidOperationException">
/// <see cref="LeftoverAgeVariable"/> is set to something other than a non-negative integer.
/// </exception>
let readLeftoverAge () : TimeSpan =
    match readVariable LeftoverAgeVariable with
    | ValueNone -> TimeSpan.FromMinutes (float DefaultLeftoverAgeMinutes)
    | ValueSome text ->
        match Int32.TryParse (text, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, minutes -> TimeSpan.FromMinutes (float minutes)
        | _ -> invalidOp $"{LeftoverAgeVariable} must be a non-negative whole number of minutes, but it is '{text}'."

/// <summary>
/// Reads when <paramref name="database"/> was modified last from <see cref="DatabaseProperties.LastModified"/>, its
/// <c>_ts</c> system property in seconds since the Unix epoch;
/// <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> when the emulator does not report it.
/// </summary>
let lastModifiedOf (database : DatabaseProperties) : DateTimeOffset voption =
    database.LastModified
    |> ValueOption.ofNullable
    // _ts is UTC by definition and the SDK reads it into a DateTime of kind Utc. Setting the kind anyway keeps the
    // DateTimeOffset constructor from taking an Unspecified one for local time, which would shift it by the offset.
    |> ValueOption.map (fun lastModified -> DateTimeOffset (DateTime.SpecifyKind (lastModified, DateTimeKind.Utc)))

/// <summary>
/// Decides whether a test database is old enough to be a leftover that <see cref="deleteLeftoverDatabasesAsync"/>
/// deletes: whether it stayed unmodified for at least <paramref name="minimumAge"/> before <paramref name="now"/>.
/// </summary>
/// <remarks>
/// A database modified after <paramref name="now"/>, such as one created while the sweep lists the databases, is never
/// a leftover, not even with a zero <paramref name="minimumAge"/>. The decision trusts the emulator's clock, which
/// stamps <paramref name="lastModified"/>, to agree with this machine's within <paramref name="minimumAge"/>.
/// </remarks>
/// <param name="minimumAge">How long the database must stay unmodified; see <see cref="readLeftoverAge"/>.</param>
/// <param name="now">The moment of the sweep.</param>
/// <param name="lastModified">When the database was modified last; see <see cref="lastModifiedOf"/>.</param>
let isLeftover (minimumAge : TimeSpan) (now : DateTimeOffset) (lastModified : DateTimeOffset) : bool =
    let age = now - lastModified
    age >= TimeSpan.Zero && age >= minimumAge

let private isLocalEmulatorHost (uri : Uri) =
    uri.Host.Equals ("localhost", StringComparison.OrdinalIgnoreCase)
    || uri.Host.Equals ("127.0.0.1", StringComparison.OrdinalIgnoreCase)

// The emulator serves a self-signed certificate; it is accepted from local hosts only
let private createHttpMessageHandler () =
    new HttpClientHandler (
        ServerCertificateCustomValidationCallback =
            (fun request _ _ errors ->
                match request.RequestUri with
                | null -> errors = SslPolicyErrors.None
                | requestUri when errors = SslPolicyErrors.None -> true
                | requestUri -> isLocalEmulatorHost requestUri
            )
    )

/// <summary>
/// Creates a <see cref="CosmosClient"/> for the emulator in <see cref="ConnectionMode.Gateway"/> mode, the only mode
/// the Linux vNext emulator serves, whose <see cref="HttpClient"/> accepts the emulator's self-signed certificate from
/// local hosts only.
/// </summary>
let createClient () : CosmosClient =
    new CosmosClient (
        endpoint,
        key,
        CosmosClientOptions (
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = Func<HttpClient>(fun () -> new HttpClient (createHttpMessageHandler (), true))
        )
    )

// How long the sweep waits for an answer before it concludes that no emulator runs. Without this check, a run of
// the unit tests alone on a machine without an emulator would wait for the SDK to give up (seconds per request).
let private reachabilityTimeout = TimeSpan.FromSeconds 5.0

let private isReachableAsync (cancellationToken : CancellationToken) : Task<bool> = task {
    use httpClient = new HttpClient (createHttpMessageHandler (), true, Timeout = reachabilityTimeout)

    try
        // Any HTTP answer will do: an unauthenticated request to the root returns 401 once the emulator is up
        use! response = httpClient.GetAsync (endpoint, cancellationToken)
        return true
    with
    | :? HttpRequestException -> return false
    | :? TaskCanceledException when not cancellationToken.IsCancellationRequested -> return false
}

/// <summary>
/// Deletes the databases whose identifier starts with <see cref="DatabaseIdentifier.Prefix"/> and that stayed
/// unmodified for at least the leftover age (<see cref="readLeftoverAge"/>): databases of tests whose cleanup failed
/// and of runs that were aborted, which would otherwise keep their containers' partitions.
/// </summary>
/// <remarks>
/// <para>
/// The prefix does not tell test processes apart, the age does: a younger test database may be the live database of
/// another test process that uses the same emulator, such as another clone, a CI agent or a parallel branch, so it
/// is kept, and so is one whose age the emulator does not report. A test does not leave its own database to the
/// sweep: it deletes it in <see cref="DatabaseTestApplicationFactory.CleanupAsync"/>.
/// </para>
/// <para>
/// Best-effort: every failure is written to <paramref name="testContext"/> and none fails the run. A malformed
/// <see cref="LeftoverAgeVariable"/> skips the sweep rather than guessing an age. When the emulator does not answer,
/// the sweep is skipped as well, so that tests that need no emulator still run without one.
/// </para>
/// </remarks>
let deleteLeftoverDatabasesAsync (testContext : TestContext) : Task = task {
    let cancellationToken = testContext.CancellationToken

    let minimumAge =
        try
            ValueSome (readLeftoverAge ())
        with :? InvalidOperationException as ex ->
            testContext.WriteLine $"Leftover test databases are not swept: {ex.Message}"
            ValueNone

    match minimumAge with
    | ValueNone -> ()
    | ValueSome minimumAge ->
        let! reachable = isReachableAsync cancellationToken

        if not reachable then
            testContext.WriteLine
                $"The Cosmos DB Emulator at {endpoint} does not answer, so leftover test databases are not swept."
        else
            use client = createClient ()
            let leftovers = ResizeArray<string>()
            let mutable keptCount = 0
            // Taken before the listing, so that a database created while the listing runs counts as modified after
            // the sweep began and is kept
            let now = DateTimeOffset.UtcNow

            try
                use iterator = client.GetDatabaseQueryIterator<DatabaseProperties>()

                while iterator.HasMoreResults do
                    let! page = iterator.ReadNextAsync cancellationToken

                    for database in page do
                        if database.Id.StartsWith (DatabaseIdentifier.Prefix, StringComparison.Ordinal) then
                            match lastModifiedOf database with
                            | ValueSome lastModified when isLeftover minimumAge now lastModified -> leftovers.Add database.Id
                            // Too young or of unknown age: possibly the live database of another test process
                            | _ -> keptCount <- keptCount + 1
            with ex ->
                testContext.WriteLine $"Could not list the databases to sweep leftover test databases: {ex.Message}"

            if keptCount > 0 then
                testContext.WriteLine
                    $"Kept {keptCount} test database(s) younger than {minimumAge.TotalMinutes} minute(s) or of unknown age: they may belong to a test process that is still running."

            for databaseId in leftovers do
                try
                    let! _ = client.GetDatabase(databaseId).DeleteAsync(cancellationToken = cancellationToken)
                    testContext.WriteLine $"Deleted the leftover test database '{databaseId}'."
                with ex ->
                    testContext.WriteLine $"Could not delete the leftover test database '{databaseId}': {ex.Message}"
}
