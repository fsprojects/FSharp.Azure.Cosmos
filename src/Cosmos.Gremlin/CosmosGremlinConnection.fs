namespace FSharp.Azure.Cosmos.Gremlin

open System
open System.Text.RegularExpressions

/// <summary>
/// The settings that connect a client to a graph of Azure Cosmos DB for Apache Gremlin: the Gremlin endpoint, the
/// database, the graph and the key of the account.
/// </summary>
/// <remarks>
/// <see cref="T:Microsoft.FSharp.Core.StructuredFormatDisplayAttribute"/> makes <c>ToString</c>, the format <c>%A</c>
/// and an interpolated string of the record give
/// <see cref="P:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnection.Description"/>, which does not show
/// <see cref="P:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnection.Key"/>. A structured logger that reads the public
/// properties of the record writes the key. Thus write
/// <see cref="P:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnection.Description"/> to a log, not the record.
/// </remarks>
[<StructuredFormatDisplay("{Description}")>]
type CosmosGremlinConnection = {
    /// <summary>
    /// The host of the Gremlin endpoint, for example <c>myaccount.gremlin.cosmos.azure.com</c>.
    /// </summary>
    Host : string
    /// The port of the Gremlin endpoint.
    Port : int
    /// <summary>
    /// <see langword="true"/> to connect with TLS (<c>wss</c>), <see langword="false"/> to connect without TLS
    /// (<c>ws</c>).
    /// </summary>
    UseSsl : bool
    /// The id of the database.
    Database : string
    /// The id of the graph, which is a container of the database.
    Graph : string
    /// The primary or the secondary key of the account.
    Key : string
} with

    /// <summary>
    /// The user name of the authentication: <c>/dbs/&lt;database&gt;/colls/&lt;graph&gt;</c>.
    /// </summary>
    member this.UserName = $"/dbs/{this.Database}/colls/{this.Graph}"

    /// <summary>
    /// The settings without <see cref="P:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnection.Key"/>, for example
    /// <c>wss://myaccount.gremlin.cosmos.azure.com:443 /dbs/db1/colls/graph1</c>.
    /// </summary>
    member this.Description =
        let scheme = if this.UseSsl then "wss" else "ws"
        $"{scheme}://{this.Host}:{this.Port} {this.UserName}"

/// <summary>
/// Creates a <see cref="T:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnection"/> for an account in Azure or for the
/// emulator.
/// </summary>
[<RequireQualifiedAccess>]
module CosmosGremlinConnection =

    /// The well-known key of the Azure Cosmos DB Emulator.
    [<Literal>]
    let EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

    /// The port of the Gremlin endpoint of an account in Azure.
    [<Literal>]
    let AzurePort = 443

    /// <summary>
    /// The default port of the Gremlin endpoint of the Windows emulator. The emulator opens it when it starts with
    /// <c>/EnableGremlinEndpoint</c>.
    /// </summary>
    [<Literal>]
    let EmulatorPort = 8901

    // The rule of Azure for the name of a Cosmos DB account: 3 to 44 lowercase letters, numbers and hyphens, starting
    // with a letter or a number. \z instead of $, because $ also matches before a final line feed.
    let private accountName = Regex (@"\A[a-z0-9][a-z0-9-]{2,43}\z", RegexOptions.CultureInvariant)

    // Cosmos DB does not allow these characters in the id of a database or a container. A slash would also change the
    // path of the user name.
    let private forbiddenIdCharacters = [| '/'; '\\'; '?'; '#' |]

    [<Literal>]
    let private MaxIdLength = 255

    let private ensureId (parameterName : string) (id : string) =
        if String.IsNullOrWhiteSpace id then
            invalidArg parameterName "The id must not be empty."

        if id.IndexOfAny forbiddenIdCharacters >= 0 then
            invalidArg parameterName $"The id '{id}' must not contain '/', '\\', '?' or '#'."

        // Data Explorer refuses an id that ends in a space (ValidCosmosDbIdDescription in src/Utils/ValidationUtils.ts of
        // Azure/cosmos-explorer). The id is not trimmed, because then the settings would name another resource.
        if id.EndsWith (" ", StringComparison.Ordinal) then
            invalidArg parameterName $"The id '{id}' must not end with a space."

        if id.Length > MaxIdLength then
            invalidArg parameterName $"The id must have {MaxIdLength} characters or less, but it has {id.Length}."

    /// <summary>
    /// Creates the settings of the graph <paramref name="graph"/> in the database <paramref name="database"/> of the
    /// account <paramref name="account"/> in Azure: the host <c>&lt;account&gt;.gremlin.cosmos.azure.com</c>, the port
    /// <see cref="AzurePort"/> and TLS.
    /// </summary>
    /// <param name="account">The name of the account, without the domain, for example <c>myaccount</c>.</param>
    /// <param name="database">The id of the database.</param>
    /// <param name="graph">The id of the graph.</param>
    /// <param name="key">The primary or the secondary key of the account.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="account"/> is not a name of a Cosmos DB account, <paramref name="database"/> or
    /// <paramref name="graph"/> is not an id of a resource, or <paramref name="key"/> is empty.
    /// </exception>
    let azure (account : string) (database : string) (graph : string) (key : string) : CosmosGremlinConnection =
        // A caller in C# can give null, and Regex.IsMatch throws for null
        if
            String.IsNullOrEmpty account
            || not (accountName.IsMatch account)
        then
            invalidArg
                (nameof account)
                $"'{account}' is not the name of a Cosmos DB account: 3 to 44 lowercase letters, numbers and hyphens, starting with a letter or a number."

        ensureId (nameof database) database
        ensureId (nameof graph) graph

        if String.IsNullOrWhiteSpace key then
            invalidArg (nameof key) "The key must not be empty."

        {
            Host = $"{account}.gremlin.cosmos.azure.com"
            Port = AzurePort
            UseSsl = true
            Database = database
            Graph = graph
            Key = key
        }

    /// <summary>
    /// Creates the settings of the graph <paramref name="graph"/> in the database <paramref name="database"/> of the
    /// local emulator: the host <c>localhost</c>, the port <see cref="EmulatorPort"/> without TLS, and
    /// <see cref="EmulatorKey"/>. The Gremlin endpoint of the emulator does not accept TLS.
    /// </summary>
    /// <param name="database">The id of the database.</param>
    /// <param name="graph">The id of the graph.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="database"/> or <paramref name="graph"/> is not an id of a resource.
    /// </exception>
    let emulator (database : string) (graph : string) : CosmosGremlinConnection =
        ensureId (nameof database) database
        ensureId (nameof graph) graph

        {
            Host = "localhost"
            Port = EmulatorPort
            UseSsl = false
            Database = database
            Graph = graph
            Key = EmulatorKey
        }
