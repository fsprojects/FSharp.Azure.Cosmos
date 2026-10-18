namespace FSharp.Azure.Cosmos.Sql

open System

/// The group a built-in function belongs to, following the function index of the query language on Microsoft Learn;
/// the functions that Learn does not document form a group of their own.
[<RequireQualifiedAccess>]
type FunctionCategory =
    /// <summary>
    /// Aggregation functions such as <c>COUNT</c>.
    /// </summary>
    | Aggregate
    /// <summary>
    /// Array functions such as <c>ARRAY_CONTAINS</c>.
    /// </summary>
    | Array
    /// <summary>
    /// Conditional functions: <c>IIF</c>.
    /// </summary>
    | Conditional
    /// <summary>
    /// Date and time functions such as <c>DateTimeAdd</c>.
    /// </summary>
    | DateTime
    /// <summary>
    /// Full text search functions such as <c>FullTextContains</c> and <c>RRF</c>.
    /// </summary>
    | FullTextSearch
    /// <summary>
    /// Item functions: <c>DOCUMENTID</c>.
    /// </summary>
    | Item
    /// <summary>
    /// Mathematical functions such as <c>ABS</c> and <c>INTBITAND</c>.
    /// </summary>
    | Mathematical
    /// <summary>
    /// Spatial functions such as <c>ST_DISTANCE</c>, and <c>VectorDistance</c>, which Learn lists among them.
    /// </summary>
    | Spatial
    /// <summary>
    /// String functions such as <c>CONTAINS</c>.
    /// </summary>
    | String
    /// <summary>
    /// Type checking functions such as <c>IS_DEFINED</c>, and the <c>StringTo…</c> conversions Learn lists among them.
    /// </summary>
    | TypeChecking
    /// Names the SDK knows that Microsoft Learn does not document.
    | Undocumented

/// How many arguments a built-in function takes.
[<RequireQualifiedAccess>]
type Arity =
    /// Exactly the given number of arguments.
    | Exactly of count : int
    /// <summary>
    /// From <paramref name="min"/> to <paramref name="max"/> arguments, both inclusive.
    /// </summary>
    | Between of min : int * max : int
    /// <summary>
    /// <paramref name="min"/> arguments or more.
    /// </summary>
    | AtLeast of min : int
    /// <summary>
    /// Not specified yet; every count is accepted. Only entries whose
    /// <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.Status"/> is unsupported use it.
    /// </summary>
    | Unspecified

    /// <summary>
    /// Whether a call with <paramref name="count"/> arguments satisfies this arity.
    /// </summary>
    /// <param name="count">The number of arguments of the call.</param>
    member this.Accepts (count : int) =
        match this with
        | Exactly expected -> count = expected
        | Between (min, max) -> count >= min && count <= max
        | AtLeast min -> count >= min
        | Unspecified -> true

    /// <summary>
    /// The arity as English text for diagnostics, such as <c>2 to 3</c>.
    /// </summary>
    override this.ToString () =
        match this with
        | Exactly count -> $"%d{count}"
        | Between (min, max) -> $"%d{min} to %d{max}"
        | AtLeast min -> $"%d{min} or more"
        | Unspecified -> "unspecified"

/// The JSON type a built-in function returns when it returns a defined value.
[<RequireQualifiedAccess>]
type ReturnKind =
    /// A boolean.
    | Boolean
    /// A number.
    | Number
    /// A string, including the ISO 8601 date and time strings of the date and time functions.
    | String
    /// An array.
    | Array
    /// An object.
    | Object
    /// Any JSON value, depending on the arguments.
    | Any
    /// Not specified yet.
    | Unspecified

/// How a built-in function treats arguments that are undefined or of the wrong type.
[<RequireQualifiedAccess>]
type UndefinedRule =
    /// Returns a defined value for every argument, such as the type checking functions.
    | NeverUndefined
    /// <summary>
    /// Returns <c>undefined</c> when an argument is undefined or of a type the function does not accept.
    /// </summary>
    | UndefinedOnInvalidArgument
    /// Not specified yet.
    | Unspecified

/// How a built-in function uses the index when it appears in a filter, as the remarks of its Microsoft Learn page state.
[<RequireQualifiedAccess>]
type IndexUsage =
    /// The function performs an index seek.
    | IndexSeek
    /// The function performs a precise index scan.
    | PreciseIndexScan
    /// The function benefits from a range index.
    | RangeIndex
    /// The function performs a full scan.
    | FullScan
    /// The function does not use the index.
    | NoIndex
    /// Not specified yet, or not stated on the function's page.
    | Unspecified

/// Where a built-in function is known to work. A flag is set only once it is confirmed: the service flag by the
/// function's Microsoft Learn page, the emulator flags by the emulator probes of the roadmap.
[<Flags>]
type Availability =
    /// Not confirmed anywhere.
    | None = 0
    /// The Azure Cosmos DB service.
    | Service = 1
    /// The Windows Azure Cosmos DB Emulator.
    | WindowsEmulator = 2
    /// The Linux vNext Azure Cosmos DB Emulator.
    | VNextEmulator = 4

/// <summary>
/// Whether the catalog specifies a built-in function well enough for the translator and
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Catalog.call(System.String,System.Collections.Immutable.ImmutableArray{FSharp.Azure.Cosmos.Sql.ScalarExpression})"/>
/// to use it.
/// </summary>
[<RequireQualifiedAccess>]
type FunctionStatus =
    /// The entry is specified: arity, return kind and placement rules are filled in.
    | Supported
    /// <summary>
    /// The entry is not specified yet, or the name cannot be called; <paramref name="reason"/> says which.
    /// </summary>
    | Unsupported of reason : string

/// The specification of one built-in function of the query language: the data the translator, the validator, the
/// interpreter, the emulator capability gating and the generated function coverage page share.
type FunctionSpec = {
    /// The name, spelled the way the SDK spells it, or the way Microsoft Learn spells it for names the SDK does not
    /// know. The query language compares function names case-insensitively.
    Name : string
    /// The group the function belongs to.
    Category : FunctionCategory
    /// How many arguments the function takes.
    Arity : Arity
    /// The JSON type of a defined result.
    ReturnKind : ReturnKind
    /// How the function treats undefined arguments and arguments of the wrong type.
    UndefinedRule : UndefinedRule
    /// How the function uses the index in a filter.
    IndexUsage : IndexUsage
    /// Where the function is confirmed to work.
    Availability : Availability
    /// <summary>
    /// Whether the function returns the same result for the same arguments; <c>RAND</c> and the functions that read
    /// the current time do not.
    /// </summary>
    Deterministic : bool
    /// <summary>
    /// Whether a call may be an item of a plain <c>ORDER BY</c> clause, which otherwise accepts property paths only.
    /// </summary>
    AllowedInOrderBy : bool
    /// <summary>
    /// Whether a call may be an item of an <c>ORDER BY RANK</c> clause: the scoring functions.
    /// </summary>
    AllowedInOrderByRank : bool
    /// <summary>
    /// Whether the function may appear only in an <c>ORDER BY RANK</c> item, directly or as an argument of another
    /// scoring function, and nowhere else in a query.
    /// </summary>
    OnlyInOrderByRank : bool
    /// The function's page on Microsoft Learn, if it has one.
    DocUrl : string voption
    /// Whether the entry is specified.
    Status : FunctionStatus
}

/// <summary>
/// Why <see cref="M:FSharp.Azure.Cosmos.Sql.Catalog.call(System.String,System.Collections.Immutable.ImmutableArray{FSharp.Azure.Cosmos.Sql.ScalarExpression})"/>
/// refused to build a call.
/// </summary>
[<RequireQualifiedAccess>]
type CatalogError =
    /// <summary>
    /// The catalog has no function named <paramref name="name"/>.
    /// </summary>
    | UnknownFunction of name : string
    /// <summary>
    /// The catalog does not specify the function named <paramref name="name"/> yet, or the name cannot be called, as
    /// its <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.Status"/> says, for <paramref name="reason"/>.
    /// </summary>
    | Unsupported of name : string * reason : string
    /// <summary>
    /// The call has <paramref name="actual"/> arguments, which <paramref name="expected"/> does not accept.
    /// </summary>
    | ArityMismatch of name : string * expected : Arity * actual : int

    /// The error as an English sentence.
    override this.ToString () =
        match this with
        | UnknownFunction name -> $"The query language has no built-in function '%s{name}'."
        | Unsupported (name, reason) -> $"The built-in function '%s{name}' is not supported: %s{reason}"
        | ArityMismatch (name, expected, actual) ->
            $"The number of arguments of the built-in function '%s{name}' must be %O{expected}, but the call has %d{actual}."
