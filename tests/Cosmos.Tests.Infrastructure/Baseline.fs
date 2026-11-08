/// <summary>
/// Golden baselines: the text and the parameters a test produces, compared with files checked into the test project,
/// <c>Baselines/&lt;suite&gt;/&lt;case&gt;.&lt;extension&gt;</c> and <c>Baselines/&lt;suite&gt;/&lt;case&gt;.params.json</c>.
/// The extension tells the language of the text: <see cref="SqlExtension"/> for a query of Cosmos DB, or another
/// extension, for example <c>groovy</c> for a Gremlin script.
/// </summary>
/// <remarks>
/// <para>
/// A query of Cosmos DB is compared after <see cref="normalizeSql"/> has collapsed its whitespace, so that its baseline
/// may be laid out for reading. Another text is compared after <see cref="normalizeText"/>, which keeps its line breaks
/// and its spaces. In another language, they can change the meaning: in Groovy, a line break ends a comment. The
/// parameters are compared as JSON, so that their layout does not matter. A text without parameters has no parameter
/// file.
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

/// The extension of a baseline that holds a query of Cosmos DB.
[<Literal>]
let SqlExtension = "sql"

// The extension of the parameter file. A text file with this extension would be the parameter file.
[<Literal>]
let private ParametersExtension = "params.json"

/// The result of comparing what a test produced with its baseline.
[<RequireQualifiedAccess>]
type Comparison =
    /// The produced text and parameters match the baseline.
    | Matches
    /// The baseline does not exist yet; the path is that of its text file.
    | Missing of textPath : string
    /// The produced text or parameters differ from the baseline, both given in the form they were compared in.
    | Differs of expected : string * actual : string
    /// The baseline was written from what the test produced; the path is that of its text file.
    | Rewritten of textPath : string

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

    // Windows removes a dot or a space at the end of a file or directory name. Thus the extension 'params.json.' would
    // give the parameter file, and the suite 'Suite.' would give the directory 'Suite'.
    if
        name.EndsWith (".", StringComparison.Ordinal)
        || name.EndsWith (" ", StringComparison.Ordinal)
    then
        invalidArg parameterName $"'{name}' ends with a dot or a space, which Windows removes from a file or directory name."

let private ensureValidExtension (extension : string) =
    ensureValidName (nameof extension) extension

    if extension.StartsWith (".", StringComparison.Ordinal) then
        invalidArg (nameof extension) $"Write the extension '{extension}' without the leading dot."

    // Windows and macOS do not compare the case of file names. Thus on these systems, a text file with this extension
    // is the parameter file.
    if String.Equals (extension, ParametersExtension, StringComparison.OrdinalIgnoreCase) then
        invalidArg (nameof extension) $"'{extension}' is the extension of the parameter file of a baseline."

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
/// Changes every line break in <paramref name="text"/> to a line feed and removes the whitespace at both ends. The
/// other whitespace stays: in a language other than SQL, a line break or a space can change the meaning of a text.
/// </summary>
let normalizeText (text : string) : string = text.ReplaceLineEndings("\n").Trim()

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

// Compares a text and its parameters with their baseline, or writes them as the baseline. The two texts are compared in
// the form that normalize gives them.
let private compareWith
    (normalize : string -> string)
    (extension : string)
    (rewrite : bool)
    (directory : string)
    (suite : string)
    (caseName : string)
    (text : string)
    (parameters : JsonObject)
    : Comparison =
    ensureValidExtension extension
    ensureValidName (nameof suite) suite
    ensureValidName (nameof caseName) caseName
    let suiteDirectory = Path.Combine (directory, suite)
    let textPath = Path.Combine (suiteDirectory, $"{caseName}.{extension}")
    let parametersPath = Path.Combine (suiteDirectory, $"{caseName}.{ParametersExtension}")

    if rewrite then
        Directory.CreateDirectory suiteDirectory |> ignore
        File.WriteAllText (textPath, text.Trim () + "\n")

        if parameters.Count = 0 then
            // A text that lost its parameters must not keep a parameter file from before
            File.Delete parametersPath
        else
            File.WriteAllText (parametersPath, parameters.ToJsonString indented + "\n")

        Comparison.Rewritten textPath
    elif not (File.Exists textPath) then
        Comparison.Missing textPath
    else
        let expectedText = normalize (File.ReadAllText textPath)

        let expectedParameters =
            if File.Exists parametersPath then
                nonNull (JsonNode.Parse (File.ReadAllText parametersPath))
            else
                JsonObject ()

        let actualText = normalize text

        if
            String.Equals (expectedText, actualText, StringComparison.Ordinal)
            && JsonNode.DeepEquals (expectedParameters, parameters)
        then
            Comparison.Matches
        else
            Comparison.Differs (
                $"{expectedText}\n{expectedParameters.ToJsonString ()}",
                $"{actualText}\n{parameters.ToJsonString ()}"
            )

/// <summary>
/// Compares <paramref name="text"/> and <paramref name="parameters"/>, the parameter names mapped to their values, with
/// the baseline <paramref name="caseName"/> of <paramref name="suite"/> under <paramref name="directory"/>, whose text
/// file has the extension <paramref name="extension"/>, or, when <paramref name="rewrite"/> is
/// <see langword="true"/>, writes them as that baseline. The texts are compared after <see cref="normalizeText"/>.
/// </summary>
/// <exception cref="ArgumentException">
/// The suite or the case name cannot be part of a file name, or the extension cannot be the extension of a text file.
/// </exception>
let compareText
    (extension : string)
    (rewrite : bool)
    (directory : string)
    (suite : string)
    (caseName : string)
    (text : string)
    (parameters : JsonObject)
    : Comparison =
    compareWith normalizeText extension rewrite directory suite caseName text parameters

/// <summary>
/// Compares <paramref name="sql"/> and <paramref name="parameters"/> with a baseline of a query of Cosmos DB, whose text
/// file has the extension <see cref="SqlExtension"/>, or, when <paramref name="rewrite"/> is <see langword="true"/>,
/// writes them as that baseline. The queries are compared after <see cref="normalizeSql"/>, so that a baseline may be
/// laid out for reading.
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
    compareWith normalizeSql SqlExtension rewrite directory suite caseName sql parameters

let private isSet (variable : string) =
    match Environment.GetEnvironmentVariable variable with
    | null -> false
    | value ->
        let value = value.AsSpan().Trim()
        value.Equals ("1", StringComparison.Ordinal)
        || value.Equals ("true", StringComparison.OrdinalIgnoreCase)

/// <summary>
/// Whether <see cref="RewriteVariable"/> is set to <c>1</c> or <c>true</c>, so that the assertions write the baselines
/// instead of comparing them. A test of the comparison itself is inconclusive then.
/// </summary>
let isRewriting () : bool = isSet RewriteVariable

// Whether the assertions write the baselines instead of comparing them. A run on CI never does.
let private isRewriteRequested () =
    let rewrite = isRewriting ()

    if rewrite && isSet "CI" then
        Assert.Fail $"{RewriteVariable} is set on CI, where baselines are never rewritten."

    rewrite

// Makes the test fail, pass or be inconclusive by the result of the comparison with its baseline
let private report (extension : string) (suite : string) (caseName : string) (comparison : Comparison) =
    match comparison with
    | Comparison.Matches -> ()
    | Comparison.Missing textPath ->
        Assert.Fail $"There is no baseline at {textPath}. Run the test with {RewriteVariable}=1 to write it, then review it."
    | Comparison.Differs (expected, actual) ->
        Assert.AreEqual (
            expected,
            actual,
            $"The text or the parameters of {suite}/{caseName}.{extension} differ from the baseline. Run the test with {RewriteVariable}=1 to accept them, then review the diff."
        )
    | Comparison.Rewritten textPath ->
        Assert.Inconclusive $"Rewrote the baseline at {textPath}. Review it and run the test again without {RewriteVariable}."

/// <summary>
/// Fails the test unless <paramref name="text"/> and <paramref name="parameters"/> match the baseline
/// <paramref name="caseName"/> of <paramref name="suite"/> under <paramref name="directory"/>, usually
/// <see cref="directoryOf"/> the test assembly, whose text file has the extension <paramref name="extension"/>. The
/// texts are compared as <see cref="compareText"/> compares them. With <see cref="RewriteVariable"/> set, writes the
/// baseline instead and reports the test inconclusive.
/// </summary>
let assertTextMatches
    (extension : string)
    (directory : string)
    (suite : string)
    (caseName : string)
    (text : string)
    (parameters : JsonObject)
    =
    compareText extension (isRewriteRequested ()) directory suite caseName text parameters
    |> report extension suite caseName

/// <summary>
/// Fails the test unless <paramref name="sql"/> and <paramref name="parameters"/> match a baseline of a query of
/// Cosmos DB, compared as <see cref="compare"/> compares them. With <see cref="RewriteVariable"/> set, writes the
/// baseline instead and reports the test inconclusive.
/// </summary>
let assertMatches (directory : string) (suite : string) (caseName : string) (sql : string) (parameters : JsonObject) =
    compare (isRewriteRequested ()) directory suite caseName sql parameters
    |> report SqlExtension suite caseName
