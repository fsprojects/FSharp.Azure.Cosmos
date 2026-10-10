/// <summary>
/// Golden baselines: the SQL text and the parameters a test produces, compared with files checked into the test project,
/// <c>Baselines/&lt;suite&gt;/&lt;case&gt;.sql</c> and <c>Baselines/&lt;suite&gt;/&lt;case&gt;.params.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The SQL is compared after <see cref="normalizeSql"/> has collapsed its whitespace, so that a baseline may be laid out
/// for reading, and the parameters are compared as JSON, so that their layout does not matter either. A query without
/// parameters has no parameter file.
/// </para>
/// <para>
/// With the environment variable <see cref="RewriteVariable"/> set to <c>1</c> or <c>true</c>, a test writes what it
/// produced into its baseline files instead of comparing them, and reports itself inconclusive, so that a rewriting run
/// is never mistaken for a passing one; the rewritten files are then reviewed in the diff. A run on CI, where the
/// environment variable <c>CI</c> is <c>true</c>, refuses to rewrite.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Baseline

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

open Microsoft.VisualStudio.TestTools.UnitTesting

/// The environment variable that makes the tests rewrite their baselines instead of comparing them.
[<Literal>]
let RewriteVariable = "COSMOS_REWRITE_BASELINES"

/// The result of comparing what a test produced with its baseline.
[<RequireQualifiedAccess>]
type Comparison =
    /// The produced SQL and parameters match the baseline.
    | Matches
    /// The baseline does not exist yet; the path is that of its SQL file.
    | Missing of sqlPath : string
    /// The produced SQL or parameters differ from the baseline, both given in the form they were compared in.
    | Differs of expected : string * actual : string
    /// The baseline was written from what the test produced; the path is that of its SQL file.
    | Rewritten of sqlPath : string

// Characters that make a suite or case name unusable as part of a file path on any platform
let private invalidNameCharacters =
    seq {
        yield! Path.GetInvalidFileNameChars ()
        yield! [| '/'; '\\'; ':'; '*'; '?'; '"'; '<'; '>'; '|' |]
    }
    |> Seq.distinct
    |> Seq.toArray

let private ensureValidName (parameterName : string) (name : string) =
    if
        String.IsNullOrWhiteSpace name
        || name.IndexOfAny invalidNameCharacters >= 0
    then
        invalidArg parameterName $"'{name}' cannot be part of the name of a baseline file."

// LF on every platform, so that a baseline does not change with the system that rewrote it
let private indented = JsonSerializerOptions (WriteIndented = true, NewLine = "\n")

/// <summary>
/// Collapses every run of whitespace in <paramref name="sql"/> into one space and removes the whitespace at both ends,
/// so that line breaks and indentation do not count when SQL is compared with its baseline.
/// </summary>
let normalizeSql (sql : string) : string =
    let builder = StringBuilder sql.Length
    let mutable pendingSpace = false
    let trimmed = sql.AsSpan().Trim()

    for index in 0 .. trimmed.Length - 1 do
        let character = trimmed[index]

        if Char.IsWhiteSpace character then
            pendingSpace <- true
        else
            if pendingSpace then
                builder.Append ' ' |> ignore
                pendingSpace <- false

            builder.Append character |> ignore

    builder.ToString ()

/// <summary>
/// Finds the <c>Baselines</c> directory of the test project that built <paramref name="assembly"/>: the directory next
/// to the project file, found by walking up from the directory the assembly runs from.
/// </summary>
/// <exception cref="InvalidOperationException">No directory above the assembly holds its project file.</exception>
let directoryOf (assembly : Assembly) : string =
    let projectFileName = $"{assembly.GetName().Name}.fsproj"

    let rec find (directory : DirectoryInfo | null) =
        match directory with
        | null ->
            invalidOp
                $"No directory above '{assembly.Location}' holds '{projectFileName}', so the baselines of the test project cannot be found."
        | directory when File.Exists (Path.Combine (directory.FullName, projectFileName)) ->
            Path.Combine (directory.FullName, "Baselines")
        | directory -> find directory.Parent

    match Path.GetDirectoryName assembly.Location with
    | null -> find null
    | assemblyDirectory -> find (DirectoryInfo assemblyDirectory)

/// <summary>
/// Compares <paramref name="sql"/> and <paramref name="parameters"/>, the parameter names mapped to their values, with
/// the baseline <paramref name="caseName"/> of <paramref name="suite"/> under <paramref name="directory"/>, or, when
/// <paramref name="rewrite"/> is <see langword="true"/>, writes them as that baseline.
/// </summary>
/// <exception cref="ArgumentException">The suite or the case name cannot be part of a file name.</exception>
let compare
    (rewrite : bool)
    (directory : string)
    (suite : string)
    (caseName : string)
    (sql : string)
    (parameters : JsonObject)
    : Comparison =
    ensureValidName (nameof suite) suite
    ensureValidName (nameof caseName) caseName
    let suiteDirectory = Path.Combine (directory, suite)
    let sqlPath = Path.Combine (suiteDirectory, $"{caseName}.sql")
    let parametersPath = Path.Combine (suiteDirectory, $"{caseName}.params.json")

    if rewrite then
        Directory.CreateDirectory suiteDirectory |> ignore
        File.WriteAllText (sqlPath, sql.Trim () + "\n")

        if parameters.Count = 0 then
            // A query that lost its parameters must not keep a parameter file from before
            File.Delete parametersPath
        else
            File.WriteAllText (parametersPath, parameters.ToJsonString indented + "\n")

        Comparison.Rewritten sqlPath
    elif not (File.Exists sqlPath) then
        Comparison.Missing sqlPath
    else
        let expectedSql = normalizeSql (File.ReadAllText sqlPath)

        let expectedParameters =
            if File.Exists parametersPath then
                nonNull (JsonNode.Parse (File.ReadAllText parametersPath))
            else
                JsonObject ()

        let actualSql = normalizeSql sql

        if
            String.Equals (expectedSql, actualSql, StringComparison.Ordinal)
            && JsonNode.DeepEquals (expectedParameters, parameters)
        then
            Comparison.Matches
        else
            Comparison.Differs (
                $"{expectedSql}\n{expectedParameters.ToJsonString ()}",
                $"{actualSql}\n{parameters.ToJsonString ()}"
            )

let private isSet (variable : string) =
    match Environment.GetEnvironmentVariable variable with
    | null -> false
    | value ->
        let value = value.AsSpan().Trim()
        value.Equals ("1", StringComparison.Ordinal)
        || value.Equals ("true", StringComparison.OrdinalIgnoreCase)

/// <summary>
/// Fails the test unless <paramref name="sql"/> and <paramref name="parameters"/> match the baseline
/// <paramref name="caseName"/> of <paramref name="suite"/> under <paramref name="directory"/>, usually
/// <see cref="directoryOf"/> the test assembly. With <see cref="RewriteVariable"/> set, writes the baseline instead and
/// reports the test inconclusive.
/// </summary>
let assertMatches (directory : string) (suite : string) (caseName : string) (sql : string) (parameters : JsonObject) =
    let rewrite = isSet RewriteVariable

    if rewrite && isSet "CI" then
        Assert.Fail $"{RewriteVariable} is set on CI, where baselines are never rewritten."

    match compare rewrite directory suite caseName sql parameters with
    | Comparison.Matches -> ()
    | Comparison.Missing sqlPath ->
        Assert.Fail $"There is no baseline at {sqlPath}. Run the test with {RewriteVariable}=1 to write it, then review it."
    | Comparison.Differs (expected, actual) ->
        Assert.AreEqual (
            expected,
            actual,
            $"The SQL or the parameters of {suite}/{caseName} differ from the baseline. Run the test with {RewriteVariable}=1 to accept them, then review the diff."
        )
    | Comparison.Rewritten sqlPath ->
        Assert.Inconclusive $"Rewrote the baseline at {sqlPath}. Review it and run the test again without {RewriteVariable}."
