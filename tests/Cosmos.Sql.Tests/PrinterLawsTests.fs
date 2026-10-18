namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Collections.Immutable
open System.Globalization
open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.VisualStudio.TestTools.UnitTesting
open Hedgehog
open Hedgehog.MSTest

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Models

/// <summary>
/// Generates any <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/>, valid or not, for a parameter of a property.
/// </summary>
type ArbitraryQueryAttribute () =
    inherit GenAttribute<SqlQuery> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.arbitrarySqlQueryGen

/// <summary>
/// Generates a constant that JSON can carry, as a <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/>, for a
/// parameter of a property.
/// </summary>
type JsonConstantAttribute () =
    inherit GenAttribute<ScalarExpression> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.constantGen

/// Generates the text of a string literal, of a property name or of an object key for a parameter of a property.
type TextAttribute () =
    inherit GenAttribute<string> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.textGen

/// Generates a name that may or may not be written as a bare identifier for a parameter of a property.
type NameAttribute () =
    inherit GenAttribute<string> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.nameGen

/// Generates an integer that a JSON number holds exactly for a parameter of a property.
type ExactIntegerAttribute () =
    inherit GenAttribute<int64> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.exactIntegerGen

/// Generates a finite number for a parameter of a property.
type FiniteNumberAttribute () =
    inherit GenAttribute<float> ()

    /// <inheritdoc />
    override _.Generator = SqlGenerators.finiteNumberGen

/// <summary>
/// The parameters of a syntax tree, found by a traversal of its own: a law about
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Inline"/> must not ask that pass which parameters a tree has.
/// </summary>
[<RequireQualifiedAccess>]
module Parameters =

    let private ofCount (count : SpecValue) : ParameterName seq =
        match count with
        | SpecValue.Parameter name -> Seq.singleton name
        | SpecValue.Literal _ -> Seq.empty

    /// <summary>
    /// The parameters of <paramref name="expression"/>, one for every place it is used at.
    /// </summary>
    /// <param name="expression">The expression.</param>
    let rec ofScalar (expression : ScalarExpression) : ParameterName seq = seq {
        match expression with
        | ScalarExpression.ParameterRef name -> name
        | ScalarExpression.Literal _
        | ScalarExpression.PropertyRef _ -> ()
        | ScalarExpression.Unary (_, operand) -> yield! ofScalar operand
        | ScalarExpression.MemberIndexer (first, second)
        | ScalarExpression.Binary (_, first, second)
        | ScalarExpression.Coalesce (first, second)
        | ScalarExpression.Like (first, second, _, _) ->
            yield! ofScalar first
            yield! ofScalar second
        | ScalarExpression.Conditional (first, second, third)
        | ScalarExpression.Between (first, _, second, third) ->
            yield! ofScalar first
            yield! ofScalar second
            yield! ofScalar third
        | ScalarExpression.In (needle, _, haystack) ->
            yield! ofScalar needle
            yield! haystack |> Seq.collect ofScalar
        | ScalarExpression.FunctionCall (_, items)
        | ScalarExpression.ArrayCreate items -> yield! items |> Seq.collect ofScalar
        | ScalarExpression.ObjectCreate properties ->
            yield!
                properties
                |> Seq.collect (fun struct (_, value) -> ofScalar value)
        | ScalarExpression.Exists query
        | ScalarExpression.Array query
        | ScalarExpression.All query
        | ScalarExpression.First query
        | ScalarExpression.Last query
        | ScalarExpression.Subquery query -> yield! ofQuery query
    }

    and private ofCollection (collection : Collection) : ParameterName seq =
        match collection with
        | Collection.InputPath _ -> Seq.empty
        | Collection.Subquery query -> ofQuery query

    and private ofCollectionExpression (expression : CollectionExpression) : ParameterName seq = seq {
        match expression with
        | CollectionExpression.Aliased (collection, _)
        | CollectionExpression.ArrayIterator (_, collection) -> yield! ofCollection collection
        | CollectionExpression.Join (left, right) ->
            yield! ofCollectionExpression left
            yield! ofCollectionExpression right
    }

    /// <summary>
    /// The parameters of <paramref name="query"/>, one for every place it is used at.
    /// </summary>
    /// <param name="query">The query.</param>
    and ofQuery (query : SqlQuery) : ParameterName seq = seq {
        match query.Top with
        | ValueSome top -> yield! ofCount top
        | ValueNone -> ()

        match query.Select with
        | SelectSpec.Star -> ()
        | SelectSpec.Value expression -> yield! ofScalar expression
        | SelectSpec.List items -> yield! items |> Seq.collect (fun item -> ofScalar item.Expression)

        match query.From with
        | ValueSome from -> yield! ofCollectionExpression from
        | ValueNone -> ()

        match query.Where with
        | ValueSome predicate -> yield! ofScalar predicate
        | ValueNone -> ()

        yield! query.GroupBy |> Seq.collect ofScalar
        yield!
            query.OrderBy
            |> Seq.collect (fun item -> ofScalar item.Expression)

        match query.OffsetLimit with
        | ValueSome (struct (offset, limit)) ->
            yield! ofCount offset
            yield! ofCount limit
        | ValueNone -> ()
    }

/// Assertions on the text the printer writes.
[<RequireQualifiedAccess>]
module PrinterAssert =

    // The decimal comma of French and German, the dotted and dotless i of Turkish, and the digits of Persian
    let private cultures =
        seq {
            CultureInfo.InvariantCulture
            CultureInfo.GetCultureInfo "fr-FR"
            CultureInfo.GetCultureInfo "de-DE"
            CultureInfo.GetCultureInfo "tr-TR"
            CultureInfo.GetCultureInfo "fa-IR"
        }
        |> ImmutableArray.CreateRange

    /// <summary>
    /// What <paramref name="print"/> writes while the current culture is <paramref name="culture"/>.
    /// </summary>
    let private printedUnder (culture : CultureInfo) (print : unit -> string) : string =
        let current = CultureInfo.CurrentCulture

        try
            CultureInfo.CurrentCulture <- culture
            print ()
        finally
            CultureInfo.CurrentCulture <- current

    /// <summary>
    /// Fails unless <paramref name="query"/> is written in <paramref name="style"/> the same way twice and under every
    /// culture.
    /// </summary>
    /// <param name="style">The style of the property accesses.</param>
    /// <param name="query">The query to write.</param>
    let IsStableText (style : PropertyStyle) (query : SqlQuery) =
        let expected = Printer.print style query

        StringAssert.StartsWith (expected, "SELECT ", StringComparison.Ordinal, "A query should be written from SELECT on.")

        for culture in cultures do
            Assert.AreEqual<string>(
                expected,
                printedUnder culture (fun () -> Printer.print style query),
                $"The text should not depend on the culture %s{culture.Name} or on the number of times it is written."
            )

/// <summary>
/// The laws of <see cref="T:FSharp.Azure.Cosmos.Sql.Printer"/>: what holds for every syntax tree, checked on trees that
/// Hedgehog generates. A law that fails is reported with the smallest tree that breaks it.
/// <para>
/// The laws that read the printed text back as a query wait for a parser of the query language; these read back what
/// JSON and the number formats of .NET can read: literals, property names and constants.
/// </para>
/// </summary>
[<TestClass; UnitTestCategory; PrinterLawsTestCategory; Properties(typeof<SqlAutoGenConfig>)>]
type PrinterLawsTests () =

    static let styles =
        seq {
            PropertyStyle.Brackets
            PropertyStyle.DotWhenSafe
        }
        |> ImmutableArray.CreateRange

    static let alias = ScalarExpression.PropertyRef (Identifier "c")

    /// <summary>
    /// The identifier rule of the grammar, <c>[A-Za-z_][A-Za-z_0-9]*</c>, written out once more, so that the law of the
    /// dot style does not take the answer it checks from
    /// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isValidIdentifier(System.String)"/>.
    /// </summary>
    static let identifierRule = Regex (@"\A[A-Za-z_][A-Za-z_0-9]*\z", RegexOptions.CultureInvariant)

    /// The inline value of every parameter: an integer, which also a count accepts. It does not suit a parameter that
    /// stands for the weights of RRF, which have to be an array, and the validator accepts such a parameter. The
    /// generator writes the weights as an array literal, so no query of these laws has one; a query that had one would
    /// be outside the law about validity below.
    static let one (_ : ParameterName) = ValueSome (ScalarExpression.Literal (Literal.Int 1L))

    /// The encoder that has no value for any parameter.
    static let none (_ : ParameterName) : ScalarExpression voption = ValueNone

    /// What the inline pass reports for a parameter without a value that stands alone, where there is nothing to
    /// traverse: one error that names it.
    static let reportedAlone (name : ParameterName) : ValidationError =
        match Inline.substituteScalarParameters none (ScalarExpression.ParameterRef name) with
        | Ok _ -> raise (AssertFailedException $"The parameter %s{name.Value} has no value, so inlining it should fail.")
        | Error errors ->
            let error = Assert.ContainsSingle (errors, "A parameter that stands alone should be reported once.")

            Assert.IsTrue (
                error.Code.IsMissingInlineValue,
                "A parameter without a value should be reported as missing its inline value."
            )

            Assert.Contains (name.Value, error.Message, StringComparison.Ordinal, "The error should name the parameter.")
            error


    [<Property>]
    member _.``A generated query has no validation error`` (query : SqlQuery) =
        Assert.IsEmpty (
            SqlQuery.validate query,
            $"The generator should build valid queries only, but built: %s{Printer.print PropertyStyle.Brackets query}"
        )

    [<Property>]
    member _.``A valid query is written the same way again and under every culture`` (query : SqlQuery) =
        for style in styles do
            PrinterAssert.IsStableText style query

    [<Property>]
    member _.``Any tree is written, valid or not, the same way again and under every culture``
        ([<ArbitraryQuery>] query : SqlQuery)
        =
        for style in styles do
            PrinterAssert.IsStableText style query

    [<Property>]
    member _.``A string literal is written as a JSON string that reads back`` ([<Text>] text : string) =
        let printed =
            Printer.printScalar PropertyStyle.Brackets (ScalarExpression.Literal (Literal.String text))

        Assert.AreEqual<string | null>(
            text,
            JsonSerializer.Deserialize<string> printed,
            "A string literal should be a JSON string of the same text."
        )

    [<Property>]
    member _.``A property name in brackets is written as a JSON string that reads back`` ([<Text>] name : string) =
        let printed =
            Printer.printScalar
                PropertyStyle.Brackets
                (ScalarExpression.MemberIndexer (alias, ScalarExpression.Literal (Literal.String name)))

        StringAssert.StartsWith (printed, "c[", StringComparison.Ordinal, "The access should follow its target in brackets.")
        StringAssert.EndsWith (printed, "]", StringComparison.Ordinal, "The access should close its bracket.")

        Assert.AreEqual<string | null>(
            name,
            JsonSerializer.Deserialize<string>(printed.AsSpan (2, printed.Length - 3)),
            "The name in the brackets should be a JSON string of the same text."
        )

    [<Property>]
    member _.``The dot style writes a dot for a safe name and brackets for any other`` ([<Name>] name : string) =
        let access = ScalarExpression.MemberIndexer (alias, ScalarExpression.Literal (Literal.String name))

        // A name is safe when it follows the identifier rule and is no keyword. The rule is the one written out above,
        // not the library's; the keywords are the library's, which the validator tests compare with the SDK's lexer.
        let expected =
            if
                identifierRule.IsMatch name
                && not (Keywords.isReserved name)
            then
                $"c.%s{name}"
            else
                Printer.printScalar PropertyStyle.Brackets access

        Assert.AreEqual<string>(
            expected,
            Printer.printScalar PropertyStyle.DotWhenSafe access,
            "Only a name that is an identifier and no reserved word should be written with a dot."
        )

    [<Property>]
    member _.``An integer literal is written so that it reads back`` ([<ExactInteger>] value : int64) =
        let printed = Printer.printScalar PropertyStyle.Brackets (ScalarExpression.Literal (Literal.Int value))

        Assert.AreEqual (
            value,
            Int64.Parse (printed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            "An integer literal should be written in full, in the invariant culture."
        )

    [<Property>]
    member _.``A number literal is written so that it reads back to the same bits`` ([<FiniteNumber>] value : float) =
        let printed =
            Printer.printScalar PropertyStyle.Brackets (ScalarExpression.Literal (Literal.Float value))

        Assert.AreEqual (
            BitConverter.DoubleToInt64Bits value,
            Double.Parse (printed, NumberStyles.Float, CultureInfo.InvariantCulture)
            |> BitConverter.DoubleToInt64Bits,
            "A number literal should be written in the round-trip format of the invariant culture."
        )

    [<Property>]
    member _.``A constant is written as JSON that reads back as the same constant``
        ([<JsonConstant>] constant : ScalarExpression)
        =
        use document = JsonDocument.Parse (Printer.printScalar PropertyStyle.Brackets constant)

        Assert.AreEqual (
            constant,
            Inline.ofJsonElement document.RootElement,
            "The text of a constant should be the JSON of the same value, with the properties of an object in order."
        )

    [<Property>]
    member _.``Inline printing replaces every parameter, and an integer for each keeps valid a query in which no parameter has to be an array``
        (query : SqlQuery)
        =
        match Inline.substituteParameters one query with
        | Error errors -> Assert.Fail $"Every parameter has an integer as its inline value, but inlining failed: %A{errors}"
        | Ok inlined ->
            Assert.IsEmpty (Parameters.ofQuery inlined, "After inlining no parameter should be left anywhere in the tree.")
            Assert.IsEmpty (
                SqlQuery.validate inlined,
                "An integer for every parameter should keep valid a query in which no parameter has to be an array."
            )

            for style in styles do
                Assert.AreEqual (
                    Ok (Printer.print style inlined),
                    Printer.printInline style one query,
                    "The inline mode should write the query in which every parameter is replaced by its value."
                )

    [<Property>]
    member _.``Inlining without values reports every parameter of any tree once`` ([<ArbitraryQuery>] query : SqlQuery) =
        // The names come from the traversal of this file, and what is reported for a name from the parameter alone, so
        // an error for a wrong name fails the law as much as a missing or a repeated one
        let expected =
            Parameters.ofQuery query
            |> Seq.distinct
            |> Seq.map reportedAlone
            |> Seq.sortBy _.Message
            |> Seq.toArray

        match Inline.substituteParameters none query with
        | Ok unchanged ->
            Assert.IsEmpty (expected, "Inlining should succeed without values only for a tree without parameters.")

            // The texts are compared, not the trees: a tree that holds NaN is not equal to itself
            Assert.AreEqual<string>(
                Printer.print PropertyStyle.Brackets query,
                Printer.print PropertyStyle.Brackets unchanged,
                "A tree without parameters should come back as it is."
            )
        | Error errors ->
            CollectionAssert.AreEqual (
                expected,
                errors |> Seq.sortBy _.Message |> Seq.toArray,
                "A tree should report what each of its parameters reports alone: the same names, and each one once."
            )
