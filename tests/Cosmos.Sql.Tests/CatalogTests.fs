namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Tests.Ast

/// <summary>
/// The function catalog: its seed against the SDK's function names, lookups and
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Catalog.call(System.String,System.Collections.Immutable.ImmutableArray{FSharp.Azure.Cosmos.Sql.ScalarExpression})"/>.
/// </summary>
[<TestClass; CatalogUnitTestCategory>]
type CatalogTests () =

    /// <summary>
    /// The names Microsoft Learn documents that the SDK's
    /// <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlFunctionCallScalarExpression.cs">SqlFunctionCallScalarExpression.Names</a>
    /// does not list.
    /// </summary>
    static let learnOnlyNames = [|
        "AGO"
        "GETCURRENTDATETIMESTATIC"
        "GETCURRENTTICKSSTATIC"
        "GETCURRENTTIMESTAMPSTATIC"
        "INTBITAND"
        "INTBITLEFTSHIFT"
        "INTBITNOT"
        "INTBITOR"
        "INTBITRIGHTSHIFT"
        "INTBITXOR"
        "NOW"
        "NUMBERBIN"
        "REGEXREPLACE"
        "REGEXREPLACEALL"
        "ST_AREA"
    |]

    /// <summary>
    /// The function names of the SDK version this repository uses, read by reflection from the constants of its
    /// internal class
    /// <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlFunctionCallScalarExpression.cs">SqlFunctionCallScalarExpression.Names</a>.
    /// Reading SDK internals is acceptable in a test, never in the library; the test fails when an SDK update adds or
    /// removes names, so the catalog follows the SDK.
    /// </summary>
    static let sdkNames =
        let names =
            typeof<CosmosClient>
                .Assembly.GetType("Microsoft.Azure.Cosmos.SqlObjects.SqlFunctionCallScalarExpression+Names", throwOnError = true)
            |> nonNull

        names.GetFields (BindingFlags.Public ||| BindingFlags.Static)
        |> Seq.filter _.IsLiteral
        |> Seq.map (fun field -> field.GetRawConstantValue () |> nonNull |> unbox<string>)
        |> Seq.toArray

    static let ignoreCase (names : string seq) = HashSet<string>(names, StringComparer.OrdinalIgnoreCase)

    static let arguments (items : ScalarExpression list) = ImmutableArray.CreateRange items

    [<TestMethod>]
    member _.``The catalog holds every non-internal name of the SDK and the Learn-only names, and nothing else`` () =
        let sdkPublicNames =
            sdkNames
            |> Array.filter (fun name -> not (name.StartsWith ("_", StringComparison.Ordinal)))
        let catalogNames = ignoreCase (Catalog.all |> Seq.map _.Name)

        Assert.HasCount (176, sdkNames, "SDK 3.62.0 lists 176 function names; a different count means the SDK changed.")

        let missing =
            sdkPublicNames
            |> Array.filter (fun name -> not (catalogNames.Contains name))
        Assert.IsEmpty (missing, "Every SDK function name except the internal ones should be in the catalog.")

        let internalNames =
            sdkNames
            |> Array.filter (fun name -> name.StartsWith ("_", StringComparison.Ordinal))
            |> ignoreCase
        let leaked =
            catalogNames
            |> Seq.filter (fun name -> internalNames.Contains name)
            |> Seq.toArray
        Assert.IsEmpty (leaked, "The internal SDK names, which start with an underscore, should not be in the catalog.")

        let expected =
            ignoreCase (
                seq {
                    yield! sdkPublicNames
                    yield! learnOnlyNames
                }
            )
        let extra =
            catalogNames
            |> Seq.filter (fun name -> not (expected.Contains name))
            |> Seq.toArray
        Assert.IsEmpty (extra, "Every catalog name should come from the SDK or from the list of Learn-only names.")

        Assert.HasCount (sdkPublicNames.Length + learnOnlyNames.Length, Catalog.all, "The catalog should hold each name once.")

    [<TestMethod>]
    member _.``Names are unique ignoring case and the lookup ignores case`` () =
        Assert.HasCount (Catalog.all.Length, Catalog.byName, "Each name should be one dictionary entry.")

        let spec =
            Catalog.tryFind "is_defined"
            |> ValueOption.defaultWith (fun () -> failwith "IS_DEFINED should be found.")
        Assert.AreEqual ("IS_DEFINED", spec.Name, "The lookup should ignore case and return the catalog's spelling.")
        Assert.IsTrue (Catalog.tryFind "NoSuchFunction" |> ValueOption.isNone, "An unknown name should not be found.")

    [<TestMethod>]
    member _.``Every entry is either specified or carries the reason why not`` () =
        for spec in Catalog.all do
            match spec.Status with
            | FunctionStatus.Supported ->
                Assert.AreNotEqual (Arity.Unspecified, spec.Arity, $"{spec.Name} is supported, so its arity should be set.")
                Assert.AreNotEqual (
                    ReturnKind.Unspecified,
                    spec.ReturnKind,
                    $"{spec.Name} is supported, so its return kind should be set."
                )
                Assert.IsTrue (spec.DocUrl.IsValueSome, $"{spec.Name} is supported, so it should be documented.")
                Assert.IsTrue (spec.Availability.HasFlag Availability.Service, $"{spec.Name} should be confirmed on the service.")
            | FunctionStatus.Unsupported reason ->
                Assert.IsFalse (String.IsNullOrWhiteSpace reason, $"{spec.Name} is unsupported, so it should say why.")

            if spec.OnlyInOrderByRank then
                Assert.IsTrue (
                    spec.AllowedInOrderByRank,
                    $"{spec.Name} may appear only in ORDER BY RANK, so it should be allowed there."
                )

            match spec.DocUrl with
            | ValueSome url ->
                Assert.StartsWith (
                    "https://learn.microsoft.com/cosmos-db/query/",
                    url,
                    StringComparison.Ordinal,
                    $"{spec.Name} should link to its Learn page."
                )
            | ValueNone ->
                Assert.AreEqual (
                    FunctionCategory.Undocumented,
                    spec.Category,
                    $"{spec.Name} has no page, so it should be undocumented."
                )

    [<TestMethod>]
    member _.``The functions that read the clock or random numbers are not deterministic`` () =
        let nonDeterministic =
            Catalog.all
            |> Seq.filter (fun spec -> not spec.Deterministic)
            |> Seq.map _.Name
            |> Seq.sort
            |> Seq.toArray

        CollectionAssert.AreEqual (
            [|
                "AGO"
                "GETCURRENTDATETIMESTATIC"
                "GETCURRENTTICKSSTATIC"
                "GETCURRENTTIMESTAMPSTATIC"
                "GetCurrentDateTime"
                "GetCurrentTicks"
                "GetCurrentTimestamp"
                "NOW"
                "RAND"
            |],
            nonDeterministic,
            "Only RAND and the functions that read the current time should be non-deterministic."
        )

    [<TestMethod>]
    member _.``The ORDER BY flags mark the scoring functions`` () =
        let flagged predicate =
            Catalog.all
            |> Seq.filter predicate
            |> Seq.map _.Name
            |> Seq.sort
            |> Seq.toArray

        CollectionAssert.AreEqual (
            [| "VectorDistance" |],
            flagged _.AllowedInOrderBy,
            "Only VectorDistance may be a plain ORDER BY item."
        )

        CollectionAssert.AreEqual (
            [| "FullTextScore"; "RRF"; "VectorDistance" |],
            flagged _.AllowedInOrderByRank,
            "The scoring functions may be ORDER BY RANK items."
        )

        CollectionAssert.AreEqual (
            [| "FullTextScore"; "RRF" |],
            flagged _.OnlyInOrderByRank,
            "FullTextScore and RRF may appear only in ORDER BY RANK."
        )

    [<TestMethod>]
    member _.``Call builds a call with the catalog's spelling when the arity fits`` () =
        let path = alias "c" |> prop "name"

        match Catalog.call "is_defined" (arguments [ path ]) with
        | Ok call ->
            Assert.AreEqual (
                ScalarExpression.FunctionCall (FunctionRef.BuiltIn "IS_DEFINED", EquatableArray.singleton path),
                call,
                "The call should use the catalog's spelling of the name."
            )

            Assert.AreEqual ("IS_DEFINED(c[\"name\"])", Printer.printScalar PropertyStyle.Brackets call, "The call should print.")
        | Error error -> Assert.Fail $"IS_DEFINED with one argument should be accepted: {error}"

        let optionalArgument = Catalog.call "StringEquals" (arguments [ path; str "x"; boolean true ])
        Assert.IsTrue (optionalArgument.IsOk, "StringEquals should accept its optional third argument.")

        let variadic = Catalog.call "CONCAT" (arguments [ str "a"; str "b"; str "c"; str "d" ])
        Assert.IsTrue (variadic.IsOk, "CONCAT should accept any number of arguments from two on.")

        let noArguments = Catalog.call "GetCurrentDateTime" ImmutableArray.Empty
        Assert.IsTrue (noArguments.IsOk, "GetCurrentDateTime should accept no arguments.")

    [<TestMethod>]
    member _.``Call refuses unknown names, unsupported entries and wrong arities`` () =
        Assert.AreEqual (
            Error (CatalogError.UnknownFunction "NoSuchFunction"),
            Catalog.call "NoSuchFunction" ImmutableArray.Empty,
            "An unknown name should be refused."
        )

        match Catalog.call "abs" (arguments [ integer -1L ]) with
        | Error (CatalogError.Unsupported (name, reason)) ->
            Assert.AreEqual ("ABS", name, "The error should use the catalog's spelling.")
            Assert.IsFalse (String.IsNullOrWhiteSpace reason, "The error should carry the reason.")
        | other -> Assert.Fail $"ABS is not specified yet, so the call should be refused: %A{other}"

        Assert.AreEqual (
            Error (CatalogError.ArityMismatch ("IS_DEFINED", Arity.Exactly 1, 2)),
            Catalog.call "IS_DEFINED" (arguments [ integer 1L; integer 2L ]),
            "Two arguments for a one-argument function should be refused."
        )

        Assert.AreEqual (
            Error (CatalogError.ArityMismatch ("VectorDistance", Arity.Between (2, 4), 1)),
            Catalog.call "VectorDistance" (arguments [ integer 1L ]),
            "Fewer arguments than the minimum should be refused."
        )

        Assert.AreEqual (
            "The number of arguments of the built-in function 'IS_DEFINED' must be 1, but the call has 2.",
            string (CatalogError.ArityMismatch ("IS_DEFINED", Arity.Exactly 1, 2)),
            "The error should read as a sentence."
        )

    [<TestMethod>]
    member _.``Arity accepts the counts it describes`` () =
        Assert.IsTrue ((Arity.Exactly 2).Accepts 2, "Exactly 2 should accept 2.")
        Assert.IsFalse ((Arity.Exactly 2).Accepts 3, "Exactly 2 should refuse 3.")
        Assert.IsTrue ((Arity.Between (2, 3)).Accepts 3, "2 to 3 should accept 3.")
        Assert.IsFalse ((Arity.Between (2, 3)).Accepts 1, "2 to 3 should refuse 1.")
        Assert.IsTrue ((Arity.AtLeast 2).Accepts 20, "2 or more should accept 20.")
        Assert.IsFalse ((Arity.AtLeast 2).Accepts 1, "2 or more should refuse 1.")
        Assert.IsTrue (Arity.Unspecified.Accepts 7, "An unspecified arity should accept any count.")
