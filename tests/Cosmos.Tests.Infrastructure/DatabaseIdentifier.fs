namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Buffers
open System.Buffers.Binary
open System.Collections
open System.Globalization
open System.Security.Cryptography
open System.Text

open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// The identity of a test, from which <see cref="DatabaseIdentifier.create"/> builds the identifier of its database.
/// </summary>
[<Struct>]
type TestIdentity = {
    /// <summary>
    /// The fully qualified name of the test class.
    /// </summary>
    ClassName : string

    /// <summary>
    /// The test method name; <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> in a class-level
    /// context such as a <see cref="ClassInitializeAttribute"/> method, where the whole class shares one database named
    /// after the class.
    /// </summary>
    TestName : string voption

    /// <summary>
    /// The data row of a data-driven test; <see langword="null"/> otherwise.
    /// </summary>
    TestData : objnull array | null

    /// <summary>
    /// The display name of the data row; <see langword="null"/> when it has none.
    /// </summary>
    DisplayName : string | null
}

/// <summary>
/// Builds the identifier of the database an emulator-backed test creates.
/// <para>
/// An identifier reads <c>fsac-test-&lt;test hash&gt;_&lt;test name&gt;</c>: <see cref="Prefix"/>, a short stable hash
/// of <see cref="TestIdentity.ClassName"/> together with <see cref="TestIdentity.TestName"/>, and
/// <see cref="TestIdentity.TestName"/> with every invalid character replaced by <c>_</c>. A data-driven test gets
/// <c>_&lt;row hash&gt;</c> appended, a stable hash of <see cref="TestIdentity.TestData"/> and
/// <see cref="TestIdentity.DisplayName"/>. A name that does not fit into <see cref="MaxLength"/> is cut.
/// </para>
/// <para>
/// The readable test name alone does not tell two tests apart: equally named methods of two classes share it, and so do
/// two methods of one class whose names differ only in characters that are replaced, such as <c>a/b</c>, <c>a b</c> and
/// <c>a_b</c>, or only beyond the cut. The test hash covers the unchanged class and test names, so none of them share
/// a database.
/// </para>
/// <para>
/// The row hash covers an encoding of the row in which every value is written as the name of its runtime type followed
/// by its text, each with its length in front. The text of a string is the string itself, that of an
/// <see cref="IFormattable"/> value its invariant text, that of an <see cref="IEnumerable"/> its items encoded the same
/// way, and that of any other value its <see cref="M:System.Object.ToString"/>. The display name of the row follows its
/// values. Values of two types that read alike, such as <c>1</c> as an <see cref="Int32"/> and as an
/// <see cref="Int64"/>, therefore never share a row hash, and no text reads like several values or like a nested
/// array.
/// </para>
/// <para>
/// What remains ambiguous are values whose type names and texts are both equal: two values of one type whose text is
/// equal, such as two objects that do not override <see cref="M:System.Object.ToString"/> or two <see cref="Type"/>
/// values naming types of one full name from different assemblies, and values of two such types. That is acceptable:
/// the arguments of a <see cref="DataRowAttribute"/> are constants of primitive types, <see cref="String"/>,
/// enumerations, <see cref="Type"/> and arrays of them, whose invariant text differs for any two values of one type
/// unless they name types of one full name, and rows that a <see cref="DynamicDataAttribute"/> provides with such
/// values still get different identifiers when their display names differ, as MSTest needs them to differ to report
/// the rows apart.
/// </para>
/// <para>
/// Every hash is SHA-256 based rather than <see cref="M:System.String.GetHashCode"/>, which is randomised per process:
/// a rerun of an aborted test meets the database that run left behind and recreates it instead of adding another one,
/// and the leftover sweep, <see cref="Emulator.deleteLeftoverDatabasesAsync"/>, finds it by <see cref="Prefix"/>.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module DatabaseIdentifier =

    /// <summary>
    /// The prefix of every test database identifier, by which <see cref="Emulator.deleteLeftoverDatabasesAsync"/> finds
    /// leftover test databases.
    /// </summary>
    [<Literal>]
    let Prefix = "fsac-test-"

    /// <summary>
    /// The maximum length of an identifier.
    /// </summary>
    [<Literal>]
    let MaxLength = 80

    // Eight hex digits (32 bits) keep a collision among a few thousand tests or rows of one method negligible
    [<Literal>]
    let private HashLength = 8

    // An explicit set, because Path.GetInvalidFileNameChars differs per platform and contains no '#'.
    // '/', '\', '?' and '#' are the characters Cosmos DB forbids in resource identifiers; '"', '<', '>', '|', ':' and '*'
    // are the other characters Windows forbids in file names; '%' would be read as an escape in REST resource links.
    let private invalidCharacters = SearchValues.Create "/\\?#\"<>|:*%"

    let private isInvalid (character : char) =
        // Whitespace as well: Cosmos DB forbids a trailing space, which cutting a long name could leave behind
        Char.IsWhiteSpace character
        || Char.IsControl character
        || invalidCharacters.Contains character

    let private sanitize (name : string) =
        name
        |> String.map (fun character -> if isInvalid character then '_' else character)

    let private stableHash (text : string) =
        // The UTF-16 code units of the text rather than its UTF-8 bytes: UTF-8 replaces every lone surrogate by U+FFFD,
        // so two texts that differ only in lone surrogates would hash alike. Little-endian on every machine, so that the
        // hash does not depend on where the test runs.
        let bytes = Array.zeroCreate<byte>(text.Length * sizeof<char>)

        for index in 0 .. text.Length - 1 do
            BinaryPrimitives.WriteUInt16LittleEndian (bytes.AsSpan (index * sizeof<char>), uint16 text[index])

        let hash = SHA256.HashData bytes
        Convert.ToHexStringLower (hash, 0, HashLength / 2)

    // Writes the length of the text in front of it, so that the text ends where its length says, whatever it contains
    let private appendFramed (builder : StringBuilder) (text : string) =
        builder.Append(text.Length.ToString (CultureInfo.InvariantCulture)).Append(':').Append(text)
        |> ignore

    // Writes the runtime type of the value, then its text, both framed. The type tells apart values that read alike, such
    // as 1 and 1L; the type decides how the text is written, so equal type names always come with texts written alike.
    let rec private appendValue (builder : StringBuilder) (value : objnull) =
        match value with
        // An empty type name, which no runtime type has
        | null -> appendFramed builder ""
        | value ->
            // Type.ToString rather than Type.FullName: the full name of a generic type qualifies its type arguments with
            // their assemblies and versions, so the identifier would change with the runtime version
            appendFramed builder (value.GetType().ToString())

            match value with
            | :? string as text -> appendFramed builder text
            | :? IFormattable as formattable -> appendFramed builder (formattable.ToString (null, CultureInfo.InvariantCulture))
            | :? IEnumerable as items ->
                // The items framed as a whole, so that the items of a nested sequence never read like those of the outer one
                let itemsBuilder = StringBuilder ()

                for item in items do
                    appendValue itemsBuilder item

                appendFramed builder (itemsBuilder.ToString ())
            | value ->
                match value.ToString () with
                | null -> appendFramed builder ""
                | text -> appendFramed builder text

    let private dataRowHash (testData : objnull array) (displayName : string | null) =
        let encoding = StringBuilder ()
        appendValue encoding testData
        // The display name as well, so that rows whose values encode alike still get different hashes when their display
        // names differ, such as rows of objects that do not override ToString
        appendValue encoding displayName
        stableHash (encoding.ToString ())

    /// <summary>
    /// Builds the database identifier of the test that <paramref name="test"/> identifies.
    /// </summary>
    /// <param name="test">The identity of the test.</param>
    /// <returns>An identifier of at most <see cref="MaxLength"/> characters that Cosmos DB accepts.</returns>
    let create (test : TestIdentity) : string =
        let struct (name, separator, hashedName) =
            match test.TestName with
            // A line break never occurs in a class or method name, so no other pair of names hashes the same text
            | ValueSome testName -> struct (testName, '_', $"{test.ClassName}\n{testName}")
            // A different separator keeps the class database apart from the one of a method named like its class
            | ValueNone -> struct (test.ClassName.Substring (test.ClassName.LastIndexOf '.' + 1), '-', test.ClassName)

        let rowSuffix =
            match test.TestData with
            | null -> ""
            | testData -> $"_{dataRowHash testData test.DisplayName}"

        let head = $"{Prefix}{stableHash hashedName}{separator}"
        let room = MaxLength - head.Length - rowSuffix.Length
        let readableName = sanitize name

        let readableName =
            if readableName.Length <= room then
                readableName
            else
                // No further hash needed: the one in the head already tells apart names that differ only beyond the cut
                readableName.Substring (0, room)

        $"{head}{readableName}{rowSuffix}"

    /// <summary>
    /// Builds the database identifier of the test that <paramref name="testContext"/> describes.
    /// </summary>
    /// <remarks>
    /// The test name is read from <see cref="TestContext.Properties"/>, because <see cref="TestContext.TestName"/>
    /// throws in class-level contexts; there the identifier falls back to
    /// <see cref="TestContext.FullyQualifiedTestClassName"/>.
    /// </remarks>
    let ofTestContext (testContext : TestContext) : string =
        let testName =
            match testContext.Properties.TryGetValue "TestName" with
            | true, (:? string as testName) -> ValueSome testName
            | _ -> ValueNone

        create {
            ClassName = testContext.FullyQualifiedTestClassName
            TestName = testName
            TestData = testContext.TestData
            DisplayName = testContext.TestDisplayName
        }
