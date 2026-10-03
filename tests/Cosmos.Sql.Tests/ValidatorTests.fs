namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Tests.Ast

/// <summary>
/// Accept and reject cases of <see cref="M:FSharp.Azure.Cosmos.Sql.SqlQueryModule.validate(FSharp.Azure.Cosmos.Sql.SqlQuery)"/>,
/// one rule at a time.
/// </summary>
[<TestClass; ValidatorUnitTestCategory>]
type ValidatorTests () =

    static let c = alias "c"
    static let selectStar = { selectValue nul with Select = SelectSpec.Star }

    static let codes (errors : ImmutableArray<ValidationError>) = errors |> Seq.map _.Code |> Seq.toArray

    /// Asserts that the query is valid.
    static let accepts (query : SqlQuery) (case : string) =
        let errors = SqlQuery.validate query
        Assert.IsEmpty (errors, $"{case} should be valid; the errors are listed in the failure.")

    /// Asserts that the query breaks exactly the given rules, in order.
    static let rejects (expected : ValidationErrorCode array) (query : SqlQuery) (case : string) =
        CollectionAssert.AreEqual (expected, codes (SqlQuery.validate query), $"{case} should break exactly these rules.")

    static let where (predicate : ScalarExpression) = { selectValue c with From = from "c"; Where = ValueSome predicate }

    static let orderedBy (rank : bool) (items : OrderByItem list) = {
        selectValue c with
            From = from "c"
            OrderBy = EquatableArray.ofSeq items
            OrderByRank = rank
    }

    [<TestMethod>]
    member _.``A typical filtered, sorted and paged query is valid`` () =
        accepts
            {
                selectValue (objectOf [ "name", c |> prop "name"; "nick", c |> prop "nick" ]) with
                    Top = ValueSome (SpecValue.Parameter (ParameterName "@take"))
                    From = from "c"
                    Where =
                        ValueSome (
                            binary
                                BinaryOperator.And
                                (binary BinaryOperator.GreaterThanOrEqual (c |> prop "age") (param "@minAge"))
                                (binary
                                    BinaryOperator.And
                                    (call "IS_DEFINED" [ c |> prop "nick" ])
                                    (call "ARRAY_CONTAINS" [ c |> prop "tags"; param "@p0" ]))
                        )
                    OrderBy = EquatableArray.singleton (orderBy ValueNone (c |> prop "name"))
            }
            "The query of the API sketch"

        accepts
            {
                selectValue c with
                    From = from "c"
                    OffsetLimit =
                        ValueSome (
                            struct (SpecValue.Parameter (ParameterName "@skip"), SpecValue.Parameter (ParameterName "@take"))
                        )
            }
            "OFFSET and LIMIT parameters outside ORDER BY RANK"

    [<TestMethod>]
    member _.``SELECT * needs exactly one collection`` () =
        accepts { selectStar with From = from "c" } "SELECT * FROM c"

        accepts
            {
                selectStar with
                    From =
                        ValueSome (
                            CollectionExpression.ArrayIterator (Identifier "t", Collection.InputPath (Identifier "c", ValueNone))
                        )
            }
            "SELECT * FROM t IN c"

        rejects [| ValidationErrorCode.SelectStarWithoutSingleAlias |] selectStar "SELECT * without FROM"

        rejects
            [| ValidationErrorCode.SelectStarWithoutSingleAlias |]
            {
                selectStar with
                    From =
                        ValueSome (
                            CollectionExpression.Join (
                                CollectionExpression.Aliased (Collection.InputPath (Identifier "c", ValueNone), ValueNone),
                                CollectionExpression.ArrayIterator (
                                    Identifier "t",
                                    Collection.InputPath (Identifier "c", ValueSome (PathExpression.String (ValueNone, "tags")))
                                )
                            )
                        )
            }
            "SELECT * over a JOIN"

    [<TestMethod>]
    member _.``TOP is accepted in the outer query and refused in every subquery`` () =
        let topQuery = { selectValue c with Top = ValueSome (SpecValue.Literal 1L); From = from "c" }

        accepts topQuery "TOP in the outer query"

        rejects [| ValidationErrorCode.TopInSubquery |] (where (ScalarExpression.Exists topQuery)) "TOP in an EXISTS subquery"

        rejects
            [| ValidationErrorCode.TopInSubquery |]
            {
                selectValue (alias "x") with
                    From = ValueSome (CollectionExpression.Aliased (Collection.Subquery topQuery, ValueSome (Identifier "x")))
            }
            "TOP in a FROM subquery"

        rejects [| ValidationErrorCode.TopInSubquery |] (selectValue (ScalarExpression.Array topQuery)) "TOP in an ARRAY subquery"

    [<TestMethod>]
    member _.``Parameters cannot choose an ORDER BY or GROUP BY key`` () =
        rejects
            [| ValidationErrorCode.OrderByItemNotPath; ValidationErrorCode.ParameterInOrderBy |]
            (orderedBy false [ orderBy ValueNone (param "@sort") ])
            "A parameter as ORDER BY item"

        rejects
            [| ValidationErrorCode.OrderByItemNotPath; ValidationErrorCode.ParameterInOrderBy |]
            (orderedBy false [ orderBy ValueNone (ScalarExpression.MemberIndexer (c, param "@property")) ])
            "A parameter as property name of an ORDER BY item"

        rejects
            [| ValidationErrorCode.ParameterInGroupBy |]
            {
                selectValue (call "COUNT" [ integer 1L ]) with
                    From = from "c"
                    GroupBy = EquatableArray.singleton (binary BinaryOperator.Add (c |> prop "a") (param "@p"))
            }
            "A parameter in a GROUP BY key"

    [<TestMethod>]
    member _.``Parameters are accepted as arguments of ORDER BY functions and as paging counts`` () =
        // The parameterized vector search of the Learn page "Tips for optimizing vector indexing and search performance"
        // and of the SDK's QueryPlanBaselineTests.VectorSearch.xml
        accepts
            (orderedBy false [ orderBy ValueNone (call "VectorDistance" [ c |> prop "embedding"; param "@embedding" ]) ])
            "A parameter as the query vector of VectorDistance in ORDER BY"

        // The parameterized hybrid search of the Learn page "Hybrid search"
        let rank =
            orderedBy true [
                orderBy
                    ValueNone
                    (call "RRF" [
                        call "VectorDistance" [ c |> prop "vector"; param "@queryVector" ]
                        call "FullTextScore" [ c |> prop "content"; param "@searchTerm1"; param "@searchTerm2" ]
                    ])
            ]

        accepts { rank with Top = ValueSome (SpecValue.Parameter (ParameterName "@k")) } "Parameters in ORDER BY RANK"

        accepts
            {
                rank with
                    OffsetLimit =
                        ValueSome (
                            struct (SpecValue.Parameter (ParameterName "@skip"), SpecValue.Parameter (ParameterName "@take"))
                        )
            }
            "Parameter paging of ORDER BY RANK"

    [<TestMethod>]
    member _.``Parameter names are an at sign and an identifier`` () =
        accepts (where (binary BinaryOperator.Equal (c |> prop "a") (param "@p_0"))) "@p_0"

        for name in [ "p"; "@"; "@1p"; "@p-q"; "" ] do
            rejects
                [| ValidationErrorCode.InvalidParameterName |]
                (where (binary BinaryOperator.Equal (c |> prop "a") (param name)))
                $"The parameter name '%s{name}'"

    [<TestMethod>]
    member _.``ORDER BY items are property paths or functions allowed in ORDER BY`` () =
        accepts (orderedBy false [ orderBy ValueNone (c |> prop "name") ]) "A property path"
        accepts
            (orderedBy false [ orderBy (ValueSome SortOrder.Descending) (c |> prop "items" |> at 0L |> prop "price") ])
            "A nested path with an index"

        accepts
            (orderedBy false [
                orderBy ValueNone (call "VectorDistance" [ c |> prop "vector"; arrayOf [ number 1.0; number 2.0 ] ])
            ])
            "VectorDistance, which is allowed in ORDER BY"

        rejects
            [| ValidationErrorCode.OrderByItemNotPath |]
            (orderedBy false [ orderBy ValueNone (binary BinaryOperator.Add (c |> prop "a") (integer 1L)) ])
            "A computed ORDER BY item"

        rejects
            [| ValidationErrorCode.OrderByItemNotPath |]
            (orderedBy false [ orderBy ValueNone (call "LOWER" [ c |> prop "name" ]) ])
            "A function not allowed in ORDER BY"

    [<TestMethod>]
    member _.``ORDER BY RANK items are scoring functions, which appear nowhere else`` () =
        let fullTextScore = call "FullTextScore" [ c |> prop "text"; str "keyword" ]
        let vectorDistance = call "VectorDistance" [ c |> prop "vector"; arrayOf [ number 1.0 ] ]

        accepts (orderedBy true [ orderBy ValueNone fullTextScore ]) "FullTextScore"
        accepts (orderedBy true [ orderBy ValueNone (call "RRF" [ fullTextScore; vectorDistance ]) ]) "RRF over scoring functions"
        accepts (orderedBy true [ orderBy (ValueSome SortOrder.Descending) vectorDistance ]) "VectorDistance"

        rejects
            [| ValidationErrorCode.RankItemNotScoringFunction |]
            (orderedBy true [ orderBy ValueNone (c |> prop "name") ])
            "A property path in ORDER BY RANK"

        rejects
            [| ValidationErrorCode.OrderByItemNotPath; ValidationErrorCode.ScoringFunctionOutsideRank |]
            (orderedBy false [ orderBy ValueNone fullTextScore ])
            "FullTextScore in a plain ORDER BY"

        rejects [| ValidationErrorCode.ScoringFunctionOutsideRank |] (selectValue fullTextScore) "FullTextScore in a projection"

        rejects
            [| ValidationErrorCode.ScoringFunctionOutsideRank; ValidationErrorCode.ScoringFunctionOutsideRank |]
            (where (binary BinaryOperator.GreaterThan (call "RRF" [ fullTextScore; vectorDistance ]) (integer 0L)))
            "RRF and the FullTextScore in it in a filter"

    [<TestMethod>]
    member _.``Identifiers must match the identifier rule and not be reserved`` () =

        rejects
            [| ValidationErrorCode.ReservedIdentifier; ValidationErrorCode.ReservedIdentifier |]
            {
                selectValue (alias "value") with
                    From =
                        ValueSome (CollectionExpression.Aliased (Collection.InputPath (Identifier "value", ValueNone), ValueNone))
            }
            "The reserved word value as reference and input name"

        rejects
            [| ValidationErrorCode.InvalidIdentifier |]
            {
                selectValue c with
                    From =
                        ValueSome (
                            CollectionExpression.Aliased (
                                Collection.InputPath (Identifier "c", ValueNone),
                                ValueSome (Identifier "some alias")
                            )
                        )
            }
            "An alias with a space"

        rejects
            [| ValidationErrorCode.ReservedIdentifier |]
            {
                selectStar with
                    Select =
                        SelectSpec.List (
                            EquatableArray.singleton { Expression = c |> prop "a"; Alias = ValueSome (Identifier "Select") }
                        )
                    From = from "c"
            }
            "A select alias that is a keyword in another letter case"

        rejects
            [| ValidationErrorCode.InvalidIdentifier |]
            {
                selectValue c with
                    From =
                        ValueSome (
                            CollectionExpression.Aliased (
                                Collection.InputPath (
                                    Identifier "c",
                                    ValueSome (PathExpression.Identifier (ValueNone, Identifier "1st"))
                                ),
                                ValueNone
                            )
                        )
            }
            "A path segment that starts with a digit"

        accepts
            {
                selectValue (alias "_root1") with
                    From =
                        ValueSome (
                            CollectionExpression.Aliased (Collection.InputPath (Identifier "_root1", ValueNone), ValueNone)
                        )
            }
            "An identifier with an underscore and digits"

    [<TestMethod>]
    member _.``IN lists and select lists need an element`` () =
        accepts (where (ScalarExpression.In (c |> prop "a", false, EquatableArray.singleton (integer 1L)))) "IN with one element"

        rejects
            [| ValidationErrorCode.EmptyInList |]
            (where (ScalarExpression.In (c |> prop "a", true, EquatableArray.empty)))
            "NOT IN with no element"

        rejects
            [| ValidationErrorCode.EmptySelectList |]
            {
                selectStar with
                    Select = SelectSpec.List EquatableArray.empty
                    From = from "c"
            }
            "An empty select list"

    [<TestMethod>]
    member _.``Built-in functions must be known and called with an accepted arity`` () =
        accepts (where (call "is_defined" [ c |> prop "a" ])) "A known function in another letter case"
        accepts (where (call "ABS" [ c |> prop "a" ])) "A known function the catalog does not specify yet"
        accepts (selectValue (call "LEFT" [ str "abc"; integer 1L ])) "LEFT, a keyword the grammar accepts as function name"
        accepts (where (udf "isVip" [ c ])) "A user-defined function"

        rejects [| ValidationErrorCode.UnknownFunction |] (where (call "IS_VIP" [ c ])) "An unknown built-in function"
        rejects [| ValidationErrorCode.FunctionArity |] (where (call "IS_DEFINED" [])) "IS_DEFINED without an argument"

        rejects
            [| ValidationErrorCode.FunctionArity |]
            (where (call "CONTAINS" [ c |> prop "a"; str "b"; boolean true; boolean false ]))
            "CONTAINS with four arguments"

        rejects [| ValidationErrorCode.InvalidFunctionName |] (selectValue (call "LIKE" [ str "a"; str "b" ])) "LIKE, a keyword"
        rejects
            [| ValidationErrorCode.InvalidFunctionName |]
            (where (udf "select" [ c ]))
            "A user-defined function named like a keyword"
        rejects
            [| ValidationErrorCode.InvalidFunctionName |]
            (where (udf "is-vip" [ c ]))
            "A user-defined function name with a hyphen"

    [<TestMethod>]
    member _.``Number literals must be finite and integers exact`` () =
        accepts (selectValue (integer 9007199254740992L)) "2^53"
        accepts (selectValue (integer -9007199254740992L)) "-2^53"
        rejects [| ValidationErrorCode.LossyInteger |] (selectValue (integer 9007199254740993L)) "2^53 + 1"
        rejects [| ValidationErrorCode.LossyInteger |] (selectValue (integer Int64.MinValue)) "Int64.MinValue"

        Assert.IsEmpty (
            SqlQuery.validateWith { AllowLossyInt64 = true } (selectValue (integer Int64.MaxValue)),
            "AllowLossyInt64 should accept integers beyond 2^53."
        )

        rejects [| ValidationErrorCode.NonFiniteNumber |] (selectValue (number Double.NaN)) "NaN"
        rejects [| ValidationErrorCode.NonFiniteNumber |] (selectValue (number Double.NegativeInfinity)) "-Infinity"

        Assert.IsEmpty (
            SqlQuery.validate (selectValue (param "@big")),
            "A parameter is never range checked, whatever value it will get."
        )

    [<TestMethod>]
    member _.``Counts must not be negative`` () =
        rejects
            [| ValidationErrorCode.NegativeCount |]
            { selectValue c with Top = ValueSome (SpecValue.Literal -1L); From = from "c" }
            "TOP -1"

        rejects
            [| ValidationErrorCode.NegativeCount; ValidationErrorCode.NegativeCount |]
            {
                selectValue c with
                    From = from "c"
                    OffsetLimit = ValueSome (struct (SpecValue.Literal -1L, SpecValue.Literal -2L))
            }
            "OFFSET -1 LIMIT -2"

    [<TestMethod>]
    member _.``Errors name the rule and quote the fragment`` () =
        let errors =
            SqlQuery.validate (orderedBy false [ orderBy ValueNone (binary BinaryOperator.Add (c |> prop "a") (integer 1L)) ])

        let error = Assert.ContainsSingle (errors, "The computed ORDER BY item should be the only error.")
        Assert.AreEqual (ValidationErrorCode.OrderByItemNotPath, error.Code, "The error should name the rule.")
        Assert.Contains ("(c[\"a\"] + 1)", error.Message, StringComparison.Ordinal, "The message should quote the item.")
