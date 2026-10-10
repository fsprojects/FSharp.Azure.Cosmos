// Golden strings for every node kind of the syntax tree. Where the SDK has a baseline for the same node, the expected
// text is the SDK's, cited with its file and case under Microsoft.Azure.Cosmos/tests of Azure/azure-cosmos-dotnet-v3
// 3.62.0 (MIT, see THIRD-PARTY-NOTICES.md):
// - SqlObjectVisitorBaselineTests.*.xml (Microsoft.Azure.Cosmos.Tests/BaselineTest/TestBaseline): the SDK's printer
//   applied to hand-built SqlObjects;
// - *SqlParserBaselineTests.*.xml (same folder): the SDK's printer applied to the parse of a query, written with dots
//   where the query had them, so those cases print with PropertyStyle.DotWhenSafe;
// - Linq*BaselineTests.*.xml (Microsoft.Azure.Cosmos.EmulatorTests/BaselineTest/TestBaseline): the SQL of the SDK's
//   LINQ provider, which writes every property access with brackets.
// The SDK's baselines are pretty-printed, so they are compared after Ast.normalize.
namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Globalization
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Tests.Ast

/// <summary>
/// Golden strings of <see cref="M:FSharp.Azure.Cosmos.Sql.Printer.print(FSharp.Azure.Cosmos.Sql.PropertyStyle,FSharp.Azure.Cosmos.Sql.SqlQuery)"/>
/// and its relatives for every node kind, escaping and culture independence.
/// </summary>
[<TestClass; PrinterUnitTestCategory>]
type PrinterTests () =

    static let brackets (expression : ScalarExpression) = Printer.printScalar PropertyStyle.Brackets expression
    static let dots (expression : ScalarExpression) = Printer.printScalar PropertyStyle.DotWhenSafe expression

    /// <summary>
    /// <c>"some"["random"]["path"][42]</c>, the path of the SDK's baselines <c>SqlObjectVisitorBaselineTests.*.xml</c>.
    /// </summary>
    static let somePath = str "some" |> prop "random" |> prop "path" |> at 42L

    static let inputPathCollection =
        Collection.InputPath (Identifier "inputPathCollection", ValueSome (PathExpression.String (ValueNone, "somePath")))

    /// <summary>
    /// <c>SELECT * FROM inputPathCollection["somePath"] AS some alias WHERE ("this path" &lt; 42) GROUP BY … ORDER BY …
    /// OFFSET 0 LIMIT 0</c>, the case <c>SqlQuery</c> of the SDK's baseline
    /// <c>SqlObjectVisitorBaselineTests.SqlQueries.xml</c> (the alias is deliberately not a valid identifier there; the
    /// printer is total).
    /// </summary>
    static let sdkSqlQuery = {
        selectValue nul with
            Select = SelectSpec.Star
            From = ValueSome (CollectionExpression.Aliased (inputPathCollection, ValueSome (Identifier "some alias")))
            Where = ValueSome (binary BinaryOperator.LessThan (str "this path") (integer 42L))
            GroupBy = EquatableArray.singleton somePath
            OrderBy = EquatableArray.singleton (orderBy (ValueSome SortOrder.Ascending) somePath)
            OffsetLimit = ValueSome (struct (SpecValue.Literal 0L, SpecValue.Literal 0L))
    }

    /// <summary>
    /// The SDK's text of the query above, from the same baseline case; the SDK writes two spaces before
    /// <c>ORDER BY</c>.
    /// </summary>
    static let sdkSqlQueryText =
        """SELECT * FROM inputPathCollection["somePath"] AS some alias WHERE ("this path" < 42) """
        + """GROUP BY "some"["random"]["path"][42]  ORDER BY "some"["random"]["path"][42] ASC OFFSET 0 LIMIT 0"""

    static let selectStar = { selectValue nul with Select = SelectSpec.Star }

    /// Prints with the culture of the current thread set to the given one, restoring it afterwards.
    static let withCulture (culture : CultureInfo) (print : unit -> string) =
        let previous = CultureInfo.CurrentCulture
        let previousUi = CultureInfo.CurrentUICulture
        CultureInfo.CurrentCulture <- culture
        CultureInfo.CurrentUICulture <- culture

        try
            print ()
        finally
            CultureInfo.CurrentCulture <- previous
            CultureInfo.CurrentUICulture <- previousUi

    [<TestMethod>]
    member _.``Control characters are escaped like the SDK escapes them`` () =
        // SqlObjectVisitorBaselineTests.SqlLiteral.xml, Escape Sequence 0 to 31
        for code in 0..31 do
            let expected =
                match code with
                | 8 -> "\"\\b\""
                | 9 -> "\"\\t\""
                | 10 -> "\"\\n\""
                | 12 -> "\"\\f\""
                | 13 -> "\"\\r\""
                | _ -> $"\"\\u%04X{code}\""

            Assert.AreEqual (expected, brackets (str (string (char code))), $"Escape Sequence %d{code}")

    [<TestMethod>]
    member _.``Quotes and backslashes are escaped and the solidus is not`` () =
        Assert.AreEqual ("\"a\\\"b\"", brackets (str "a\"b"), "A double quote should be escaped.")
        Assert.AreEqual ("\"a\\\\b\"", brackets (str "a\\b"), "A backslash should be escaped.")
        // StringLiteralSqlParserBaselineTests: "\/solidus" parses and prints as "/solidus"
        Assert.AreEqual ("\"/solidus\"", brackets (str "/solidus"), "A solidus should be written as is, as the SDK does.")
        Assert.AreEqual ("\"'single'\"", brackets (str "'single'"), "A single quote should be written as is.")

    [<TestMethod>]
    member _.``String, null, boolean and undefined literals print like the SDK baselines`` () =
        // SqlObjectVisitorBaselineTests.SqlLiteral.xml
        Assert.AreEqual ("\"\"", brackets (str ""), "Empty String")
        Assert.AreEqual ("\"Hello\"", brackets (str "Hello"), "SqlStringLiteral")
        Assert.AreEqual ("\"💩\"", brackets (str "💩"), "SqlStringLiteral With Unicode")
        Assert.AreEqual ("null", brackets nul, "SqlNullLiteral")
        Assert.AreEqual ("true", brackets (boolean true), "SqlBooleanLiteralTrue")
        Assert.AreEqual ("false", brackets (boolean false), "SqlBooleanLiteralFalse")
        Assert.AreEqual ("undefined", brackets undefined, "SqlUndefinedLiteral")

    [<TestMethod>]
    member _.``Number literals print like the SDK baselines`` () =
        // SqlObjectVisitorBaselineTests.SqlLiteral.xml
        let cases = [
            struct ("SqlNumberLiteral", integer 1597463007L, "1597463007")
            struct ("Zero", integer 0L, "0")
            struct ("Positive Number", integer 1L, "1")
            struct ("Negative Number", integer -1L, "-1")
            struct ("Double", number 1337.1337, "1337.1337")
            struct ("E", number Math.E, "2.718281828459045")
            struct ("Pi", number Math.PI, "3.141592653589793")
            struct ("1/3", number (1.0 / 3.0), "0.3333333333333333")
            struct ("Max Safe Integer 9007199254740991", integer 9007199254740991L, "9007199254740991")
            struct ("Max Safe Integer 9007199254740991 Plus One", integer 9007199254740992L, "9007199254740992")
            struct ("Min Safe Integer: -9007199254740991", integer -9007199254740991L, "-9007199254740991")
            struct ("Min Safe Integer: -9007199254740991 Minus One", integer -9007199254740992L, "-9007199254740992")
            struct ("PositiveInfinity", number Double.PositiveInfinity, "Infinity")
            struct ("NegativeInfinity", number Double.NegativeInfinity, "-Infinity")
            struct ("NaN NaN", number Double.NaN, "NaN")
            struct ("Epsilon 5E-324", number Double.Epsilon, "5E-324")
            struct ("MaxValue 1.7976931348623157E+308", number Double.MaxValue, "1.7976931348623157E+308")
            struct ("MinValue -1.7976931348623157E+308", number Double.MinValue, "-1.7976931348623157E+308")
            struct ("MinValue -9223372036854775808", integer Int64.MinValue, "-9223372036854775808")
            struct ("MaxValue 9223372036854775807", integer Int64.MaxValue, "9223372036854775807")
        ]

        for struct (case, literal, expected) in cases do
            Assert.AreEqual (expected, brackets literal, case)

    [<TestMethod>]
    member _.``A float without a fractional part prints without one, as the SDK prints it`` () =
        // LinqTranslationBaselineTests.TestVectorDistanceFunction.xml prints the float32 vector [2, 3, 4] that way
        Assert.AreEqual ("[2, 3, 4]", brackets (arrayOf [ number 2.0; number 3.0; number 4.0 ]), "Integral floats")

    [<TestMethod>]
    member _.``Parameters and bare identifiers print as they are`` () =
        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml, SqlParameterRefScalarExpression
        Assert.AreEqual ("@param0", brackets (param "@param0"), "SqlParameterRefScalarExpression")
        // ScalarExpressionSqlParserBaselineTests.PropertyRef.xml, root
        Assert.AreEqual ("c", brackets (alias "c"), "root")

    [<TestMethod>]
    member _.``Property accesses print with brackets and JSON escaping`` () =
        Assert.AreEqual ("c[\"name\"]", brackets (alias "c" |> prop "name"), "A property access")
        Assert.AreEqual ("c[\"value\"]", brackets (alias "c" |> prop "value"), "A reserved word as property name")
        Assert.AreEqual ("c[\"first name\"]", brackets (alias "c" |> prop "first name"), "A name with a space")
        Assert.AreEqual ("c[\"a\\\"b\"]", brackets (alias "c" |> prop "a\"b"), "A name with a quote")
        Assert.AreEqual ("c[\"a\"][\"b\"]", brackets (alias "c" |> prop "a" |> prop "b"), "A nested access")
        // SqlObjectVisitorBaselineTests.SqlQueries.xml prints this path in several cases
        Assert.AreEqual ("\"some\"[\"random\"][\"path\"][42]", brackets somePath, "A path over a literal")

    [<TestMethod>]
    member _.``Property accesses print with dots when the name is safe and the style asks for it`` () =
        Assert.AreEqual ("c.name", dots (alias "c" |> prop "name"), "A safe name")
        Assert.AreEqual ("c[\"value\"]", dots (alias "c" |> prop "value"), "A reserved word keeps brackets")
        Assert.AreEqual ("c[\"first name\"]", dots (alias "c" |> prop "first name"), "An unsafe name keeps brackets")
        Assert.AreEqual ("c[\"1a\"]", dots (alias "c" |> prop "1a"), "A name starting with a digit keeps brackets")
        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml, SqlPropertyRefScalarExpression
        Assert.AreEqual ("\"some\".path", dots (str "some" |> prop "path"), "SqlPropertyRefScalarExpression")
        // ScalarExpressionSqlParserBaselineTests.MemberIndexer.xml, Basic and Expression as indexer
        Assert.AreEqual ("c.arr[2]", dots (alias "c" |> prop "arr" |> at 2L), "Basic")

        Assert.AreEqual (
            "c.arr[(2 + 2)]",
            dots (ScalarExpression.MemberIndexer (alias "c" |> prop "arr", binary BinaryOperator.Add (integer 2L) (integer 2L))),
            "Expression as indexer"
        )

    [<TestMethod>]
    member _.``Every binary operator prints in parentheses like the SDK baseline`` () =
        // SqlObjectVisitorBaselineTests.SqlBinaryScalarOperators.xml
        let cases = [
            struct (BinaryOperator.Add, "+")
            struct (BinaryOperator.And, "AND")
            struct (BinaryOperator.BitwiseAnd, "&")
            struct (BinaryOperator.BitwiseOr, "|")
            struct (BinaryOperator.BitwiseXor, "^")
            struct (BinaryOperator.Divide, "/")
            struct (BinaryOperator.Equal, "=")
            struct (BinaryOperator.GreaterThan, ">")
            struct (BinaryOperator.GreaterThanOrEqual, ">=")
            struct (BinaryOperator.LessThan, "<")
            struct (BinaryOperator.LessThanOrEqual, "<=")
            struct (BinaryOperator.Modulo, "%")
            struct (BinaryOperator.Multiply, "*")
            struct (BinaryOperator.NotEqual, "!=")
            struct (BinaryOperator.Or, "OR")
            struct (BinaryOperator.StringConcat, "||")
            struct (BinaryOperator.Subtract, "-")
        ]

        Assert.HasCount (17, cases, "The cases should cover all seventeen binary operators.")

        for struct (operator, text) in cases do
            Assert.AreEqual (
                $"(3735928559 %s{text} 3131746989)",
                brackets (binary operator (integer 3735928559L) (integer 3131746989L)),
                $"%A{operator}"
            )

    [<TestMethod>]
    member _.``Every unary operator prints in parentheses like the SDK baseline`` () =
        // SqlObjectVisitorBaselineTests.SqlUnaryScalarOperators.xml
        let cases = [
            struct (UnaryOperator.BitwiseNot, "(~ 3735928559)")
            struct (UnaryOperator.Not, "(NOT 3735928559)")
            struct (UnaryOperator.Minus, "(- 3735928559)")
            struct (UnaryOperator.Plus, "(+ 3735928559)")
        ]

        for struct (operator, expected) in cases do
            Assert.AreEqual (expected, brackets (ScalarExpression.Unary (operator, integer 3735928559L)), $"%A{operator}")

    [<TestMethod>]
    member _.``Nested operations keep every parenthesis`` () =
        // ScalarExpressionSqlParserBaselineTests.Binary.xml, Multiplicative > Additive
        let expression =
            binary
                BinaryOperator.Subtract
                (binary BinaryOperator.Add (integer 1L) (binary BinaryOperator.Multiply (integer 2L) (integer 3L)))
                (binary BinaryOperator.Divide (integer 4L) (integer 5L))

        Assert.AreEqual ("((1 + (2 * 3)) - (4 / 5))", brackets expression, "Multiplicative > Additive")

    [<TestMethod>]
    member _.``Conditional and coalesce print like the SDK baselines`` () =
        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml
        Assert.AreEqual (
            "(\"if true\" ? \"then this\" : \"else this\")",
            brackets (ScalarExpression.Conditional (str "if true", str "then this", str "else this")),
            "SqlConditionalScalarExpression"
        )

        Assert.AreEqual (
            "(\"if this is null\" ?? \"then return this\")",
            brackets (ScalarExpression.Coalesce (str "if this is null", str "then return this")),
            "SqlCoalesceScalarExpression"
        )

    [<TestMethod>]
    member _.``IN and NOT IN print like the SDK baselines`` () =
        let haystack = EquatableArray.ofSeq [ str "this"; str "set"; str "of"; str "values" ]

        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml
        Assert.AreEqual (
            "(\"is this\" NOT IN (\"this\", \"set\", \"of\", \"values\"))",
            brackets (ScalarExpression.In (str "is this", true, haystack)),
            "SqlInScalarExpression Not: True"
        )

        Assert.AreEqual (
            "(\"is this\" IN (\"this\", \"set\", \"of\", \"values\"))",
            brackets (ScalarExpression.In (str "is this", false, haystack)),
            "SqlInScalarExpression Not: False"
        )

        // ScalarExpressionSqlParserBaselineTests.In.xml, Basic
        Assert.AreEqual (
            "(42 IN (42))",
            brackets (ScalarExpression.In (integer 42L, false, EquatableArray.singleton (integer 42L))),
            "Basic"
        )

        Assert.AreEqual (
            "(42 IN ())",
            brackets (ScalarExpression.In (integer 42L, false, EquatableArray.empty)),
            "An empty list prints, although the validator rejects it"
        )

    [<TestMethod>]
    member _.``BETWEEN and NOT BETWEEN print like the SDK baselines`` () =
        // ScalarExpressionSqlParserBaselineTests.Between.xml
        Assert.AreEqual (
            "(42 BETWEEN 15 AND 1337)",
            brackets (ScalarExpression.Between (integer 42L, false, integer 15L, integer 1337L)),
            "Regular Betweeen"
        )

        Assert.AreEqual (
            "(42 NOT BETWEEN 15 AND 1337)",
            brackets (ScalarExpression.Between (integer 42L, true, integer 15L, integer 1337L)),
            "NOT Betweeen"
        )

        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml
        Assert.AreEqual (
            "(\"some\"[\"random\"][\"path\"][42] BETWEEN 42 AND 1337)",
            brackets (ScalarExpression.Between (somePath, false, integer 42L, integer 1337L)),
            "SqlBetweenScalarExpression"
        )

    [<TestMethod>]
    member _.``LIKE prints like the SDK baselines with single spaces`` () =
        let age = alias "c" |> prop "age"

        // LikeClauseSqlParserBaselineTests.Tests.xml
        Assert.AreEqual ("(c.age LIKE \"$a\")", dots (ScalarExpression.Like (age, str "$a", false, ValueNone)), "Basic")

        Assert.AreEqual (
            "(c.age LIKE \"a!%\" ESCAPE \"!\")",
            dots (ScalarExpression.Like (age, str "a!%", false, ValueSome "!")),
            "With ESCAPE"
        )

        let negated = dots (ScalarExpression.Like (age, str "a!%", true, ValueSome "!"))

        Assert.AreEqual ("(c.age NOT LIKE \"a!%\" ESCAPE \"!\")", negated, "With NOT, single-spaced")

        Assert.AreEqual (
            normalize "(c.age NOT  LIKE \"a!%\" ESCAPE \"!\")",
            normalize negated,
            "With NOT, equal to the SDK's double-spaced text after normalization"
        )

    [<TestMethod>]
    member _.``Function calls print like the SDK baselines`` () =
        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml, SqlFunctionCallScalarExpression
        Assert.AreEqual ("ABS(-42)", brackets (call "ABS" [ integer -42L ]), "SqlFunctionCallScalarExpression")
        // SqlObjectVisitorBaselineTests.SqlFunctionCalls.xml
        Assert.AreEqual ("PI()", brackets (call "PI" []), "PI")

        Assert.AreEqual (
            "ARRAY_CONTAINS([1, 2, 3], 42, true)",
            brackets (call "ARRAY_CONTAINS" [ arrayOf [ integer 1L; integer 2L; integer 3L ]; integer 42L; boolean true ]),
            "ARRAY_CONTAINS"
        )

        Assert.AreEqual ("IIF(true, \"YES\", \"NO\")", brackets (call "IIF" [ boolean true; str "YES"; str "NO" ]), "IIF")

        // ScalarExpressionSqlParserBaselineTests.FunctionCall.xml, udf
        Assert.AreEqual ("udf.my_udf(-123)", brackets (udf "my_udf" [ integer -123L ]), "udf")

    [<TestMethod>]
    member _.``Array and object literals print like the SDK baselines`` () =
        // SqlObjectVisitorBaselineTests.SqlScalarExpression.xml
        Assert.AreEqual ("[]", brackets (arrayOf []), "SqlArrayCreateScalarExpressionEmpty")
        Assert.AreEqual ("[null]", brackets (arrayOf [ nul ]), "SqlArrayCreateScalarExpressionOneItem")
        Assert.AreEqual ("[null, null, null]", brackets (arrayOf [ nul; nul; nul ]), "SqlArrayCreateScalarExpressionMultItems")
        Assert.AreEqual ("{}", brackets (objectOf []), "SqlObjectCreateScalarExpression Empty")

        let hello = struct ("Hello", str "World")

        Assert.AreEqual ("{\"Hello\": \"World\"}", brackets (objectOf [ hello ]), "SqlObjectCreateScalarExpression OneProperty")

        Assert.AreEqual (
            "{\"Hello\": \"World\", \"Hello\": \"World\", \"Hello\": \"World\"}",
            brackets (objectOf [ hello; hello; hello ]),
            "SqlObjectCreateScalarExpression MultiProperty"
        )

        Assert.AreEqual (
            "{\"a\\\"b\": 1}",
            brackets (objectOf [ struct ("a\"b", integer 1L) ]),
            "Object keys are escaped like every other string"
        )

    [<TestMethod>]
    member _.``Subquery expressions print like the SDK baselines`` () =
        let cases = [
            // ScalarExpressionSqlParserBaselineTests.Exists.xml, Array.xml, All.xml, First.xml, Last.xml, Subquery.xml
            struct ("EXISTS(SELECT *)", ScalarExpression.Exists selectStar)
            struct ("ARRAY(SELECT *)", ScalarExpression.Array selectStar)
            struct ("ALL(SELECT *)", ScalarExpression.All selectStar)
            struct ("FIRST(SELECT *)", ScalarExpression.First selectStar)
            struct ("LAST(SELECT *)", ScalarExpression.Last selectStar)
            struct ("(SELECT *)", ScalarExpression.Subquery selectStar)
        ]

        for struct (expected, expression) in cases do
            Assert.AreEqual (expected, brackets expression, expected)

        // SqlObjectVisitorBaselineTests.SqlQueries.xml, SqlSubqueryScalarExpression, SqlArrayScalarExpression and
        // SqlExistsScalarExpression
        let sdkQuery = sdkSqlQueryText

        Assert.AreEqual (
            normalize $"({sdkQuery})",
            normalize (brackets (ScalarExpression.Subquery sdkSqlQuery)),
            "SqlSubqueryScalarExpression"
        )
        Assert.AreEqual (
            normalize $"ARRAY({sdkQuery})",
            normalize (brackets (ScalarExpression.Array sdkSqlQuery)),
            "SqlArrayScalarExpression"
        )
        Assert.AreEqual (
            normalize $"EXISTS({sdkQuery})",
            normalize (brackets (ScalarExpression.Exists sdkSqlQuery)),
            "SqlExistsScalarExpression"
        )

    [<TestMethod>]
    member _.``The select clause prints like the SDK baselines`` () =
        let top = ValueSome (SpecValue.Literal 42L)
        let list =
            SelectSpec.List (EquatableArray.singleton { Expression = somePath; Alias = ValueSome (Identifier "some alias") })

        // SqlObjectVisitorBaselineTests.SqlQueries.xml, SqlSelectClause
        let cases = [
            struct ("SELECT DISTINCT TOP 42 *", { selectStar with Distinct = true; Top = top })
            struct ("SELECT TOP 42 *", { selectStar with Top = top })
            struct ("SELECT DISTINCT *", { selectStar with Distinct = true })
            struct ("SELECT *", selectStar)
            struct ("SELECT DISTINCT TOP 42 \"some\"[\"random\"][\"path\"][42] AS some alias",
                    { selectStar with Select = list; Distinct = true; Top = top })
            struct ("SELECT \"some\"[\"random\"][\"path\"][42] AS some alias", { selectStar with Select = list })
        ]

        for struct (expected, query) in cases do
            Assert.AreEqual (expected, Printer.print PropertyStyle.Brackets query, expected)

        // SelectClauseSqlParserBaselineTests.Tests.xml, Select List with aliases, Select Value and TOP with parameters
        let item (expression : ScalarExpression) (name : string voption) = {
            Expression = expression
            Alias = name |> ValueOption.map Identifier
        }

        let items =
            EquatableArray.ofSeq [
                item (integer 1L) (ValueSome "asdf")
                item (integer 2L) ValueNone
                item (integer 3L) (ValueSome "asdf2")
            ]

        Assert.AreEqual (
            "SELECT 1 AS asdf, 2, 3 AS asdf2",
            Printer.print PropertyStyle.Brackets { selectStar with Select = SelectSpec.List items },
            "Select List with aliases"
        )

        Assert.AreEqual ("SELECT VALUE 1", Printer.print PropertyStyle.Brackets (selectValue (integer 1L)), "Select Value")

        Assert.AreEqual (
            "SELECT TOP @TOPCOUNT *",
            Printer.print PropertyStyle.Brackets {
                selectStar with
                    Top = ValueSome (SpecValue.Parameter (ParameterName "@TOPCOUNT"))
            },
            "TOP with parameters"
        )

    [<TestMethod>]
    member _.``Collections print like the SDK baselines`` () =
        let print (from : CollectionExpression) = Printer.print PropertyStyle.Brackets { selectStar with From = ValueSome from }
        let someAlias = Identifier "some alias"
        let subquery = Collection.Subquery selectStar

        // SqlObjectVisitorBaselineTests.SqlQueries.xml, the collection cases
        Assert.AreEqual (
            "SELECT * FROM inputPathCollection[\"somePath\"] AS some alias",
            print (CollectionExpression.Aliased (inputPathCollection, ValueSome someAlias)),
            "SqlAliasedCollectionExpression collectionType: SqlInputPathCollection"
        )

        Assert.AreEqual (
            "SELECT * FROM some alias IN inputPathCollection[\"somePath\"]",
            print (CollectionExpression.ArrayIterator (someAlias, inputPathCollection)),
            "SqlArrayIteratorCollectionExpression collectionType: SqlInputPathCollection"
        )

        Assert.AreEqual (
            "SELECT * FROM inputPathCollection[\"somePath\"] AS some alias JOIN some alias IN inputPathCollection[\"somePath\"]",
            print (
                CollectionExpression.Join (
                    CollectionExpression.Aliased (inputPathCollection, ValueSome someAlias),
                    CollectionExpression.ArrayIterator (someAlias, inputPathCollection)
                )
            ),
            "SqlJoinCollectionExpression collectionType: SqlInputPathCollection"
        )

        Assert.AreEqual (
            "SELECT * FROM (SELECT *) AS some alias JOIN some alias IN (SELECT *)",
            print (
                CollectionExpression.Join (
                    CollectionExpression.Aliased (subquery, ValueSome someAlias),
                    CollectionExpression.ArrayIterator (someAlias, subquery)
                )
            ),
            "SqlJoinCollectionExpression collectionType: SqlSubqueryCollection"
        )

    [<TestMethod>]
    member _.``Path expressions print like the SDK parser baselines`` () =
        let print (from : CollectionExpression) = Printer.print PropertyStyle.Brackets { selectStar with From = ValueSome from }
        let c = Identifier "c"
        let arr = ValueSome (PathExpression.Identifier (ValueNone, Identifier "arr"))
        let blah = ValueSome (PathExpression.Identifier (ValueNone, Identifier "blah"))

        // FromClauseSqlParserBaselineTests.AliasedCollection.xml
        Assert.AreEqual (
            "SELECT * FROM c.arr[5] AS asdf",
            print (
                CollectionExpression.Aliased (
                    Collection.InputPath (c, ValueSome (PathExpression.Number (arr, 5L))),
                    ValueSome (Identifier "asdf")
                )
            ),
            "collection: c.arr[5] + useAlias with AS: True"
        )

        Assert.AreEqual (
            "SELECT * FROM c.blah[\"asdf\"]",
            print (
                CollectionExpression.Aliased (
                    Collection.InputPath (c, ValueSome (PathExpression.String (blah, "asdf"))),
                    ValueNone
                )
            ),
            "collection: c.blah['asdf'] + useAlias with AS: False"
        )

        // FromClauseSqlParserBaselineTests.ArrayIteratorCollection.xml and JoinCollection.xml
        Assert.AreEqual (
            "SELECT * FROM item IN c.age",
            print (
                CollectionExpression.ArrayIterator (
                    Identifier "item",
                    Collection.InputPath (c, ValueSome (PathExpression.Identifier (ValueNone, Identifier "age")))
                )
            ),
            "collection: c.age"
        )

        Assert.AreEqual (
            "SELECT * FROM c JOIN d IN c.children",
            print (
                CollectionExpression.Join (
                    CollectionExpression.Aliased (Collection.InputPath (c, ValueNone), ValueNone),
                    CollectionExpression.ArrayIterator (
                        Identifier "d",
                        Collection.InputPath (c, ValueSome (PathExpression.Identifier (ValueNone, Identifier "children")))
                    )
                )
            ),
            "Basic join"
        )

        Assert.AreEqual (
            "SELECT * FROM c.blah.asdf",
            Printer.print PropertyStyle.DotWhenSafe {
                selectStar with
                    From =
                        ValueSome (
                            CollectionExpression.Aliased (
                                Collection.InputPath (c, ValueSome (PathExpression.String (blah, "asdf"))),
                                ValueNone
                            )
                        )
            },
            "A safe string segment prints with a dot under DotWhenSafe"
        )

    [<TestMethod>]
    member _.``The full query prints its clauses in order like the SDK baseline`` () =
        let printed = Printer.print PropertyStyle.Brackets sdkSqlQuery

        // SqlObjectVisitorBaselineTests.SqlQueries.xml, SqlQuery; the SDK writes two spaces before ORDER BY
        let expected =
            """SELECT * FROM inputPathCollection["somePath"] AS some alias WHERE ("this path" < 42) """
            + """GROUP BY "some"["random"]["path"][42] ORDER BY "some"["random"]["path"][42] ASC OFFSET 0 LIMIT 0"""

        Assert.AreEqual (expected, printed, "SqlQuery with single spaces")
        Assert.AreEqual (normalize sdkSqlQueryText, normalize printed, "SqlQuery equal to the SDK baseline after normalization")

    [<TestMethod>]
    member _.``ORDER BY, ORDER BY RANK and OFFSET LIMIT print like the SDK parser baselines`` () =
        let text = alias "c" |> prop "text"
        let score = call "FullTextScore" [ text; arrayOf [ str "keyword" ] ]

        // OrderByClauseSqlParserBaselineTests.MultiOrderBy.xml, Only one sort order
        Assert.AreEqual (
            "SELECT * ORDER BY 1 ASC, 2 DESC, 3",
            Printer.print PropertyStyle.Brackets {
                selectStar with
                    OrderBy =
                        EquatableArray.ofSeq [
                            orderBy (ValueSome SortOrder.Ascending) (integer 1L)
                            orderBy (ValueSome SortOrder.Descending) (integer 2L)
                            orderBy ValueNone (integer 3L)
                        ]
            },
            "An item without a sort order prints without one"
        )

        // OrderByClauseSqlParserBaselineTests.SingleOrderByRank.xml, Basic and Descending
        Assert.AreEqual (
            "SELECT * ORDER BY RANK FullTextScore(c.text, [\"keyword\"])",
            Printer.print PropertyStyle.DotWhenSafe {
                selectStar with
                    OrderBy = EquatableArray.singleton (orderBy ValueNone score)
                    OrderByRank = true
            },
            "Basic"
        )

        Assert.AreEqual (
            "SELECT * ORDER BY RANK FullTextScore(c.text, [\"keyword\"]) DESC",
            Printer.print PropertyStyle.DotWhenSafe {
                selectStar with
                    OrderBy = EquatableArray.singleton (orderBy (ValueSome SortOrder.Descending) score)
                    OrderByRank = true
            },
            "Descending"
        )

        // OffsetLimitClauseSqlParserBaselineTests.Tests.xml, Basic and Parameters
        Assert.AreEqual (
            "SELECT * OFFSET 10 LIMIT 10",
            Printer.print PropertyStyle.Brackets {
                selectStar with
                    OffsetLimit = ValueSome (struct (SpecValue.Literal 10L, SpecValue.Literal 10L))
            },
            "Basic"
        )

        Assert.AreEqual (
            "SELECT * OFFSET @OFFSETCOUNT LIMIT @LIMITCOUNT",
            Printer.print PropertyStyle.Brackets {
                selectStar with
                    OffsetLimit =
                        ValueSome (
                            struct (SpecValue.Parameter (ParameterName "@OFFSETCOUNT"),
                                    SpecValue.Parameter (ParameterName "@LIMITCOUNT"))
                        )
            },
            "Parameters"
        )

    [<TestMethod>]
    member _.``Queries print like the SDK LINQ baselines`` () =
        let root = alias "root"
        let print = Printer.print PropertyStyle.Brackets

        let check (expected : string) (query : SqlQuery) (case : string) =
            let printed = print query
            Assert.AreEqual (normalize expected, normalize printed, case)
            Assert.AreEqual (expected, printed, $"{case}, without normalization")

        // LinqGeneralBaselineTests.TestSkipTake.xml
        check
            "SELECT VALUE f0 FROM root JOIN f0 IN root[\"Children\"] WHERE (f0[\"Grade\"] > 100) OFFSET 10 LIMIT 20"
            {
                selectValue (alias "f0") with
                    From =
                        ValueSome (
                            CollectionExpression.Join (
                                CollectionExpression.Aliased (Collection.InputPath (Identifier "root", ValueNone), ValueNone),
                                CollectionExpression.ArrayIterator (
                                    Identifier "f0",
                                    Collection.InputPath (
                                        Identifier "root",
                                        ValueSome (PathExpression.String (ValueNone, "Children"))
                                    )
                                )
                            )
                        )
                    Where = ValueSome (binary BinaryOperator.GreaterThan (alias "f0" |> prop "Grade") (integer 100L))
                    OffsetLimit = ValueSome (struct (SpecValue.Literal 10L, SpecValue.Literal 20L))
            }
            "TestSkipTake"

        // LinqGeneralBaselineTests.TestSkipTake.xml; the SDK pretty-prints the subquery, so only the normalized text
        // is compared
        let skipTakeSubquery = {
            selectValue (alias "r0") with
                Distinct = true
                From =
                    ValueSome (
                        CollectionExpression.Aliased (
                            Collection.Subquery {
                                selectRoot with
                                    OffsetLimit = ValueSome (struct (SpecValue.Literal 3L, SpecValue.Literal 11L))
                            },
                            ValueSome (Identifier "r0")
                        )
                    )
        }

        Assert.AreEqual (
            normalize "SELECT DISTINCT VALUE r0 FROM ( SELECT VALUE root FROM root OFFSET 3 LIMIT 11 ) AS r0",
            normalize (print skipTakeSubquery),
            "TestSkipTake, subquery collection"
        )

        // LinqTranslationBaselineTests.TestVectorDistanceFunction.xml
        check
            "SELECT VALUE root[\"Pk\"] FROM root ORDER BY VectorDistance(root[\"VectorFloatField\"], [2, 3, 4], false)"
            {
                selectValue (root |> prop "Pk") with
                    From = from "root"
                    OrderBy =
                        EquatableArray.singleton (
                            orderBy
                                ValueNone
                                (call "VectorDistance" [
                                    root |> prop "VectorFloatField"
                                    arrayOf [ number 2.0; number 3.0; number 4.0 ]
                                    boolean false
                                ])
                        )
            }
            "TestVectorDistanceFunction"

        // LinqTranslationBaselineTests.TestRRFOrderByRankFunction.xml
        check
            ("""SELECT VALUE root["Pk"] FROM root ORDER BY RANK RRF(FullTextScore(root["StringField"], "test1"), """
             + """FullTextScore(root["StringField2"], "test1", "test2", "test3"))""")
            {
                selectValue (root |> prop "Pk") with
                    From = from "root"
                    OrderBy =
                        EquatableArray.singleton (
                            orderBy
                                ValueNone
                                (call "RRF" [
                                    call "FullTextScore" [ root |> prop "StringField"; str "test1" ]
                                    call "FullTextScore" [ root |> prop "StringField2"; str "test1"; str "test2"; str "test3" ]
                                ])
                        )
                    OrderByRank = true
            }
            "TestRRFOrderByRankFunction"

        // LinqTranslationBaselineTests.TestStringFunctions.xml
        check
            "SELECT VALUE (root[\"StringField\"] NOT IN (\"one\", \"two\", \"three\")) FROM root"
            {
                selectValue (
                    ScalarExpression.In (
                        root |> prop "StringField",
                        true,
                        EquatableArray.ofSeq [ str "one"; str "two"; str "three" ]
                    )
                ) with
                    From = from "root"
            }
            "TestStringFunctions, NOT IN"

        check
            "SELECT VALUE CONTAINS(root[\"StringField\"], \"Str\", true) FROM root"
            {
                selectValue (call "CONTAINS" [ root |> prop "StringField"; str "Str"; boolean true ]) with
                    From = from "root"
            }
            "TestStringFunctions, CONTAINS ignoring case"

        check
            "SELECT VALUE (NOT false) FROM root"
            {
                selectValue (ScalarExpression.Unary (UnaryOperator.Not, boolean false)) with
                    From = from "root"
            }
            "TestStringFunctions, NOT"

        // LinqTranslationBaselineTests.TestUDFs.xml
        check
            "SELECT VALUE udf.MultiParamterUDF(root[\"NumericField\"], root[\"StringField\"], root[\"Point\"]) FROM root"
            {
                selectValue (
                    udf "MultiParamterUDF" [ root |> prop "NumericField"; root |> prop "StringField"; root |> prop "Point" ]
                ) with
                    From = from "root"
            }
            "TestUDFs"

        // LinqTranslationBaselineTests.TestLiteralSerialization.xml
        check
            "SELECT VALUE {\"value\": -9223372036854775808} FROM root"
            {
                selectValue (objectOf [ struct ("value", integer Int64.MinValue) ]) with
                    From = from "root"
            }
            "TestLiteralSerialization, Int64.MinValue"

        check
            "SELECT VALUE {\"value\": 1.7976931348623157E+308} FROM root"
            {
                selectValue (objectOf [ struct ("value", number Double.MaxValue) ]) with
                    From = from "root"
            }
            "TestLiteralSerialization, Double.MaxValue"

        // LinqTranslationBaselineTests.TestConditional.xml
        check
            ("""SELECT VALUE root["StringField"] FROM root WHERE (root["NumericField"] = ((root["StringField"] = "str") ? 1 : """
             + """(ARRAY_CONTAINS(root["ArrayField"], 1) ? 3 : 4)))""")
            {
                selectValue (root |> prop "StringField") with
                    From = from "root"
                    Where =
                        ValueSome (
                            binary
                                BinaryOperator.Equal
                                (root |> prop "NumericField")
                                (ScalarExpression.Conditional (
                                    binary BinaryOperator.Equal (root |> prop "StringField") (str "str"),
                                    integer 1L,
                                    ScalarExpression.Conditional (
                                        call "ARRAY_CONTAINS" [ root |> prop "ArrayField"; integer 1L ],
                                        integer 3L,
                                        integer 4L
                                    )
                                ))
                        )
            }
            "TestConditional"

        // LinqTranslationBaselineTests.TestCoalesce.xml
        check
            "SELECT VALUE (root[\"StringField\"] ?? \"str\") FROM root"
            {
                selectValue (ScalarExpression.Coalesce (root |> prop "StringField", str "str")) with
                    From = from "root"
            }
            "TestCoalesce"

        // LinqGeneralBaselineTests.TestGroupByTranslation.xml; the SDK writes a space after the GROUP BY clause
        let groupBy = {
            selectValue nul with
                Select =
                    SelectSpec.List (
                        EquatableArray.ofSeq [
                            {
                                Expression = root |> prop "FamilyId"
                                Alias = ValueSome (Identifier "familyId")
                            }
                            {
                                Expression = call "COUNT" [ integer 1L ]
                                Alias = ValueSome (Identifier "familyIdCount")
                            }
                        ]
                    )
                From = from "root"
                GroupBy = EquatableArray.singleton (root |> prop "FamilyId")
        }

        Assert.AreEqual (
            normalize "SELECT root[\"FamilyId\"] AS familyId, COUNT(1) AS familyIdCount FROM root GROUP BY root[\"FamilyId\"] ",
            normalize (print groupBy),
            "TestGroupByTranslation"
        )

    [<TestMethod>]
    member _.``A predicate prints as the filter text of a patch`` () =
        Assert.AreEqual (
            "FROM c WHERE (c[\"qty\"] > 0)",
            Printer.printPredicate
                PropertyStyle.Brackets
                (Identifier "c")
                (binary BinaryOperator.GreaterThan (alias "c" |> prop "qty") (integer 0L)),
            "The filter predicate should start with FROM and the alias"
        )

    [<TestMethod>]
    member _.``Printing does not depend on the culture of the thread`` () =
        let query = {
            selectValue (arrayOf [ number 1337.1337; number -0.5; integer -42L; number 1.5e-7; number Double.NaN ]) with
                From = from "c"
                Where = ValueSome (binary BinaryOperator.LessThan (alias "c" |> prop "price") (number 1234567.891))
                OffsetLimit = ValueSome (struct (SpecValue.Literal 1000L, SpecValue.Literal 2000L))
        }

        let expected =
            "SELECT VALUE [1337.1337, -0.5, -42, 1.5E-07, NaN] FROM c WHERE (c[\"price\"] < 1234567.891) OFFSET 1000 LIMIT 2000"

        Assert.AreEqual (
            expected,
            withCulture CultureInfo.InvariantCulture (fun () -> Printer.print PropertyStyle.Brackets query),
            "Invariant culture"
        )

        Assert.AreEqual (
            expected,
            withCulture (CultureInfo.GetCultureInfo "fr-FR") (fun () -> Printer.print PropertyStyle.Brackets query),
            "fr-FR, whose decimal separator is a comma"
        )

        // A culture whose every number symbol differs from the invariant one
        let odd =
            CultureInfo (
                "fr-FR",
                NumberFormat =
                    NumberFormatInfo (
                        NegativeSign = "~",
                        PositiveSign = "#",
                        NumberDecimalSeparator = ",",
                        NumberGroupSeparator = ".",
                        NaNSymbol = "PasUnNombre",
                        PositiveInfinitySymbol = "+inf",
                        NegativeInfinitySymbol = "-inf"
                    )
            )

        Assert.AreEqual (
            expected,
            withCulture odd (fun () -> Printer.print PropertyStyle.Brackets query),
            "A culture with other number symbols"
        )

    [<TestMethod>]
    member _.``Printing is deterministic for equal trees`` () =
        let build () = {
            selectRoot with
                Where = ValueSome (call "IS_DEFINED" [ alias "root" |> prop "nick" ])
                OrderBy = EquatableArray.singleton (orderBy (ValueSome SortOrder.Descending) (alias "root" |> prop "name"))
        }

        Assert.AreEqual (
            Printer.print PropertyStyle.Brackets (build ()),
            Printer.print PropertyStyle.Brackets (build ()),
            "Two equal trees should print the same text"
        )
