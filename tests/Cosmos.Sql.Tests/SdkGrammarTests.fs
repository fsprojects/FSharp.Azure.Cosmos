namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Grammar
open FSharp.Azure.Cosmos.Sql.Tests.Ast
open FSharp.Azure.Cosmos.Tests

/// Assertions on what the SDK's parser reads.
[<RequireQualifiedAccess>]
module GrammarAssert =

    /// <summary>
    /// The query the SDK's parser reads from <paramref name="text"/>; fails when the parser rejects the text.
    /// </summary>
    let WantQuery (text : string) : SqlQuery =
        match SdkGrammar.parse text with
        | Ok query -> query
        | Error error -> raise (AssertFailedException $"The SDK's parser should read the text.\n%s{text}\n%s{error}")

    /// <summary>
    /// The scalar expression the SDK's parser reads from <paramref name="text"/>, which is put after
    /// <c>SELECT VALUE</c> for that.
    /// </summary>
    let WantScalar (text : string) : ScalarExpression =
        match (WantQuery $"SELECT VALUE %s{text}").Select with
        | SelectSpec.Value expression -> expression
        | other -> raise (AssertFailedException $"SELECT VALUE should give a value, but gave %A{other}.")

    /// <summary>
    /// Asserts that the SDK's parser rejects <paramref name="text"/> and gives the reason.
    /// </summary>
    let WantError (text : string) : string =
        match SdkGrammar.parse text with
        | Ok query -> raise (AssertFailedException $"The text should be rejected, but was read as %A{query}.")
        | Error error -> error

/// <summary>
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Grammar.SdkGrammar.parse(System.String)"/>: what the SDK's parser reads, as a
/// syntax tree of this repository. The laws that compare it with the printer on generated trees are in
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.PrinterLawsTests"/>.
/// </summary>
[<TestClass; UnitTestCategory; GrammarTestCategory>]
type SdkGrammarTests () =

    static let c = alias "c"

    [<TestMethod>]
    member _.``A query with every clause is read into its tree`` () =
        let expected = {
            Select = SelectSpec.Value c
            Distinct = true
            Top = ValueSome (SpecValue.Literal 10L)
            From = from "c"
            Where = ValueSome (binary BinaryOperator.GreaterThanOrEqual (c |> prop "age") (param "@minAge"))
            GroupBy = EquatableArray.singleton (c |> prop "city")
            OrderBy = EquatableArray.singleton (orderBy (ValueSome SortOrder.Descending) (c |> prop "name"))
            OrderByRank = false
            OffsetLimit = ValueSome (struct (SpecValue.Parameter (ParameterName "@skip"), SpecValue.Literal 5L))
        }

        Assert.AreEqual (
            expected,
            GrammarAssert.WantQuery
                "SELECT DISTINCT TOP 10 VALUE c FROM c WHERE (c[\"age\"] >= @minAge) GROUP BY c[\"city\"] ORDER BY c[\"name\"] DESC OFFSET @skip LIMIT 5",
            "Every clause should arrive in the tree."
        )

    [<TestMethod>]
    member _.``A select list is read with its aliases and a star as a star`` () =
        Assert.AreEqual (
            SelectSpec.List (
                EquatableArray.unsafeOfArray [|
                    { Expression = c |> prop "name"; Alias = ValueSome (Identifier "n") }
                    { Expression = c |> prop "age"; Alias = ValueNone }
                |]
            ),
            (GrammarAssert.WantQuery "SELECT c.name AS n, c.age FROM c").Select,
            "A select list should keep its items and their aliases."
        )

        Assert.AreEqual (SelectSpec.Star, (GrammarAssert.WantQuery "SELECT * FROM c").Select, "A star should be read as a star.")

    [<TestMethod>]
    member _.``A property access with a dot and one with brackets are the same node`` () =
        let expected = c |> prop "address" |> prop "city"

        Assert.AreEqual (expected, GrammarAssert.WantScalar "c.address.city", "A dot should give a member indexer with a string.")
        Assert.AreEqual (expected, GrammarAssert.WantScalar "c[\"address\"][\"city\"]", "Brackets should give the same node.")
        Assert.AreEqual (c |> prop "tags" |> at 0L, GrammarAssert.WantScalar "c.tags[0]", "A number in brackets is an index.")

    [<TestMethod>]
    member _.``A string literal is decoded with every escape, in both kinds of quotes`` () =
        Assert.AreEqual (
            str "a\"b\\c/d\b\f\n\r\té\U0001F600",
            GrammarAssert.WantScalar "\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\\u00e9\\uD83D\\uDE00\"",
            "Every escape of the grammar should be decoded, a surrogate pair included."
        )

        Assert.AreEqual (
            str "it is \"quoted\"",
            GrammarAssert.WantScalar "'it is \"quoted\"'",
            "Single quotes hold double quotes."
        )

    [<TestMethod>]
    member _.``A number is an integer when it fits Int64 and a double otherwise`` () =
        Assert.AreEqual (
            arrayOf [|
                integer 1L
                integer -7L
                integer Int64.MaxValue
                number 2.5
                number 1000.0
                number 18446744073709551616.0
                number -0.0
            |],
            GrammarAssert.WantScalar "[1, -7, 9223372036854775807, 2.5, 1E+3, 18446744073709551616, -0]",
            "Integers within Int64 should stay integers; a fraction, an exponent, a larger integer and the negative zero are doubles."
        )

    [<TestMethod>]
    member _.``A sign directly before a digit belongs to the number`` () =
        Assert.AreEqual (
            binary BinaryOperator.Subtract (integer 1L) (integer 2L),
            GrammarAssert.WantScalar "(1 - 2)",
            "A minus with a space after it should be the operator."
        )

        Assert.AreEqual (
            ScalarExpression.Unary (UnaryOperator.Minus, integer 2L),
            GrammarAssert.WantScalar "(- 2)",
            "A unary minus with a space after it should stay an operator."
        )

        Assert.AreEqual (
            integer -2L,
            GrammarAssert.WantScalar "(-2)",
            "A minus directly before a digit should be the sign of the number."
        )

        // The lexer reads -2 as one number, so the grammar sees two numbers in a row
        Assert.Contains (
            "line 1:",
            GrammarAssert.WantError "SELECT VALUE 1 -2",
            StringComparison.Ordinal,
            "A subtraction without a space should be rejected."
        )

    [<TestMethod>]
    member _.``Operators without parentheses follow the precedence of the grammar`` () =
        Assert.AreEqual (
            binary BinaryOperator.Add (integer 1L) (binary BinaryOperator.Multiply (integer 2L) (integer 3L)),
            GrammarAssert.WantScalar "1 + 2 * 3",
            "Multiplication should bind tighter than addition."
        )

        Assert.AreEqual (
            binary
                BinaryOperator.Or
                (binary BinaryOperator.And (ScalarExpression.Unary (UnaryOperator.Not, c |> prop "a")) (c |> prop "b"))
                (c |> prop "d"),
            GrammarAssert.WantScalar "NOT c.a AND c.b OR c.d",
            "NOT should bind tighter than AND, and AND tighter than OR."
        )

    [<TestMethod>]
    member _.``The conditional and the coalescing operator are read`` () =
        Assert.AreEqual (
            ScalarExpression.Conditional (c |> prop "a", integer 1L, integer 2L),
            GrammarAssert.WantScalar "(c.a ? 1 : 2)",
            "A conditional should keep its three parts."
        )

        Assert.AreEqual (
            ScalarExpression.Coalesce (c |> prop "a", str "none"),
            GrammarAssert.WantScalar "(c.a ?? \"none\")",
            "A coalescing operator should keep both sides."
        )

    [<TestMethod>]
    member _.``IN, BETWEEN and LIKE are read with their negation and the escape`` () =
        Assert.AreEqual (
            ScalarExpression.In (c |> prop "a", true, EquatableArray.unsafeOfArray [| integer 1L; integer 2L |]),
            GrammarAssert.WantScalar "(c.a NOT IN (1, 2))",
            "NOT IN should be a negated list test."
        )

        Assert.AreEqual (
            ScalarExpression.Between (c |> prop "a", false, integer 1L, integer 9L),
            GrammarAssert.WantScalar "(c.a BETWEEN 1 AND 9)",
            "BETWEEN should keep its bounds."
        )

        Assert.AreEqual (
            ScalarExpression.Like (c |> prop "a", str "x!%%", true, ValueSome "!"),
            GrammarAssert.WantScalar "(c.a NOT LIKE \"x!%%\" ESCAPE \"!\")",
            "NOT LIKE should keep the pattern and the escape character."
        )

    [<TestMethod>]
    member _.``Function calls are read as built-in or user-defined, LEFT and RIGHT included`` () =
        Assert.AreEqual (
            call "ARRAY_CONTAINS" [| c |> prop "tags"; param "@tag" |],
            GrammarAssert.WantScalar "ARRAY_CONTAINS(c.tags, @tag)",
            "A call should keep its name and its arguments."
        )

        Assert.AreEqual (
            udf "isVip" [| c |],
            GrammarAssert.WantScalar "udf.isVip(c)",
            "The udf prefix should give a user-defined call."
        )
        Assert.AreEqual (
            call "LEFT" [| c |> prop "name"; integer 1L |],
            GrammarAssert.WantScalar "LEFT(c.name, 1)",
            "LEFT is a keyword and a function."
        )
        Assert.AreEqual (
            call "RIGHT" [| c |> prop "name"; integer 1L |],
            GrammarAssert.WantScalar "RIGHT(c.name, 1)",
            "RIGHT is a keyword and a function."
        )
        Assert.AreEqual (call "PI" [||], GrammarAssert.WantScalar "PI()", "A call may have no argument.")

    [<TestMethod>]
    member _.``Array and object literals are read with their items in order`` () =
        Assert.AreEqual (
            objectOf [| struct ("a", arrayOf [| integer 1L; nul |]); struct ("b c", objectOf [||]) |],
            GrammarAssert.WantScalar "{\"a\": [1, null], \"b c\": {}}",
            "An object literal should keep its properties in order, nested literals included."
        )

        Assert.AreEqual (
            arrayOf [| boolean true; boolean false; undefined |],
            GrammarAssert.WantScalar "[true, false, undefined]",
            "The constants should be read as their literals."
        )

    [<TestMethod>]
    member _.``Every form of a subquery is read`` () =
        let subquery = { selectValue (alias "t") with From = from "t" }

        let forms = [|
            struct ("EXISTS(SELECT VALUE t FROM t)", ScalarExpression.Exists subquery)
            struct ("ARRAY(SELECT VALUE t FROM t)", ScalarExpression.Array subquery)
            struct ("ALL(SELECT VALUE t FROM t)", ScalarExpression.All subquery)
            struct ("FIRST(SELECT VALUE t FROM t)", ScalarExpression.First subquery)
            struct ("LAST(SELECT VALUE t FROM t)", ScalarExpression.Last subquery)
            struct ("(SELECT VALUE t FROM t)", ScalarExpression.Subquery subquery)
        |]

        for struct (text, expected) in forms do
            Assert.AreEqual (expected, GrammarAssert.WantScalar text, text)

    [<TestMethod>]
    member _.``The collections of FROM are read: paths, aliases, iteration, joins and subqueries`` () =
        let fromOf (text : string) = (GrammarAssert.WantQuery $"SELECT VALUE 1 FROM %s{text}").From

        let path =
            PathExpression.String (
                ValueSome (PathExpression.Number (ValueSome (PathExpression.Identifier (ValueNone, Identifier "items")), 0L)),
                "x y"
            )

        Assert.AreEqual (
            ValueSome (
                CollectionExpression.Aliased (Collection.InputPath (Identifier "c", ValueSome path), ValueSome (Identifier "i"))
            ),
            fromOf "c.items[0][\"x y\"] AS i",
            "An input path should keep its steps, and AS its alias."
        )

        Assert.AreEqual (
            ValueSome (
                CollectionExpression.Join (
                    CollectionExpression.Aliased (Collection.InputPath (Identifier "c", ValueNone), ValueNone),
                    CollectionExpression.ArrayIterator (
                        Identifier "t",
                        Collection.InputPath (
                            Identifier "c",
                            ValueSome (PathExpression.Identifier (ValueNone, Identifier "tags"))
                        )
                    )
                )
            ),
            fromOf "c JOIN t IN c.tags",
            "A join with an iteration should keep both sides."
        )

        Assert.AreEqual (
            ValueSome (CollectionExpression.Aliased (Collection.Subquery (selectValue (integer 2L)), ValueSome (Identifier "s"))),
            fromOf "(SELECT VALUE 2) s",
            "A subquery may be a collection, and its alias needs no AS."
        )

    [<TestMethod>]
    member _.``ORDER BY is read with its directions, and ORDER BY RANK with its scoring functions`` () =
        let ordered = GrammarAssert.WantQuery "SELECT VALUE c FROM c ORDER BY c.a, c.b ASC, c.d DESC"

        Assert.AreEqual (
            EquatableArray.unsafeOfArray [|
                orderBy ValueNone (c |> prop "a")
                orderBy (ValueSome SortOrder.Ascending) (c |> prop "b")
                orderBy (ValueSome SortOrder.Descending) (c |> prop "d")
            |],
            ordered.OrderBy,
            "An item without a direction should have none; ASC and DESC should be kept."
        )

        Assert.IsFalse (ordered.OrderByRank, "A plain ORDER BY is no ranking.")

        let ranked =
            GrammarAssert.WantQuery "SELECT VALUE c FROM c ORDER BY RANK FullTextScore(c.text, \"bike\")"

        Assert.AreEqual (
            EquatableArray.singleton (orderBy ValueNone (call "FullTextScore" [| c |> prop "text"; str "bike" |])),
            ranked.OrderBy,
            "ORDER BY RANK should keep the scoring function."
        )

        Assert.IsTrue (ranked.OrderByRank, "ORDER BY RANK should be marked as a ranking.")

    [<TestMethod>]
    member _.``A double without a fraction is written like an integer and read back as one`` () =
        let text = Printer.printScalar PropertyStyle.Brackets (number 2.0)

        Assert.AreEqual<string>("2", text, "The printer should write 2.0 the way the SDK does, without a fraction.")
        Assert.AreEqual (
            integer 2L,
            GrammarAssert.WantScalar text,
            "The text 2 is an integer: the kind of the number is not in the text."
        )

    // The generator of valid trees relies on where the printer starts to write an exponent: not at 1E+15, the limit of
    // the numbers it draws, which is written in full and read as an integer, but at 1E+17
    [<TestMethod>]
    member _.``The printer writes 1E+15 in full and 1E+17 with an exponent`` () =
        let limit = Printer.printScalar PropertyStyle.Brackets (number 1e15)

        Assert.AreEqual<string>("1000000000000000", limit, "The printer should write 1E+15 in full.")
        Assert.AreEqual (integer 1000000000000000L, GrammarAssert.WantScalar limit, "The text of 1E+15 is an integer.")

        let beyond = Printer.printScalar PropertyStyle.Brackets (number 1e17)

        Assert.AreEqual<string>("1E+17", beyond, "The printer should write 1E+17 with an exponent.")
        Assert.AreEqual (number 1e17, GrammarAssert.WantScalar beyond, "The text of 1E+17 is a double.")

    [<TestMethod>]
    member _.``Joins are read left-deep, whatever tree they were written from`` () =
        let collection (name : string) =
            CollectionExpression.Aliased (Collection.InputPath (Identifier name, ValueNone), ValueNone)

        let joined (from : CollectionExpression) = { selectValue (integer 1L) with From = ValueSome from }

        let rightDeep =
            joined (CollectionExpression.Join (collection "a", CollectionExpression.Join (collection "b", collection "d")))

        let leftDeep =
            joined (CollectionExpression.Join (CollectionExpression.Join (collection "a", collection "b"), collection "d"))

        let text = Printer.print PropertyStyle.Brackets rightDeep

        Assert.AreEqual<string>("SELECT VALUE 1 FROM a JOIN b JOIN d", text, "The text has no brackets around a join.")
        Assert.AreEqual<string>(text, Printer.print PropertyStyle.Brackets leftDeep, "Both trees should be written alike.")
        Assert.AreEqual (leftDeep, GrammarAssert.WantQuery text, "The parser should read a chain of joins left-deep.")

    [<TestMethod>]
    member _.``A step of an input path written with a dot is read as an identifier step`` () =
        let withPath (step : PathExpression) = {
            selectValue (integer 1L) with
                From = ValueSome (CollectionExpression.Aliased (Collection.InputPath (Identifier "c", ValueSome step), ValueNone))
        }

        let stringStep = withPath (PathExpression.String (ValueNone, "tags"))
        let identifierStep = withPath (PathExpression.Identifier (ValueNone, Identifier "tags"))

        Assert.AreEqual (
            stringStep,
            GrammarAssert.WantQuery (Printer.print PropertyStyle.Brackets stringStep),
            "In brackets a string step should stay a string step."
        )

        Assert.AreEqual (
            identifierStep,
            GrammarAssert.WantQuery (Printer.print PropertyStyle.DotWhenSafe stringStep),
            "The dot style writes a safe name with a dot, and a dot is read as an identifier step."
        )

    [<TestMethod>]
    member _.``Text the grammar rejects gives the place of the first error`` () =
        Assert.Contains (
            "line 1:40",
            GrammarAssert.WantError "SELECT VALUE c FROM c WHERE c.a IN (1, 2",
            StringComparison.Ordinal,
            "A missing bracket should be reported at the end of the text."
        )

        Assert.Contains (
            "line 1:",
            GrammarAssert.WantError "SELEC VALUE 1",
            StringComparison.Ordinal,
            "A misspelt keyword should be rejected."
        )

        Assert.Contains (
            "line 1:",
            GrammarAssert.WantError "SELECT VALUE 'it''s'",
            StringComparison.Ordinal,
            "A quote cannot be doubled inside a string."
        )

    [<TestMethod>]
    member _.``Text the grammar reads and the syntax tree cannot hold is refused with the reason`` () =
        Assert.Contains (
            "1.5",
            GrammarAssert.WantError "SELECT TOP 1.5 VALUE c FROM c",
            StringComparison.Ordinal,
            "The grammar takes any number after TOP, the tree only an integer."
        )
