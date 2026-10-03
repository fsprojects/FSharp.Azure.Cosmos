namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Tests.Ast

/// <summary>
/// The inline print mode: <see cref="M:FSharp.Azure.Cosmos.Sql.Inline.substituteParameters(Microsoft.FSharp.Core.FSharpFunc{FSharp.Azure.Cosmos.Sql.ParameterName,Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Sql.ScalarExpression}},FSharp.Azure.Cosmos.Sql.SqlQuery)"/>
/// and the printing functions built on it.
/// </summary>
[<TestClass; PrinterUnitTestCategory>]
type InlineTests () =

    /// An encoder over a fixed set of literals, given by parameter name.
    static let encoder (values : struct (string * ScalarExpression) list) =
        let lookup = Dictionary<string, ScalarExpression>(StringComparer.Ordinal)

        for struct (name, value) in values do
            lookup[name] <- value

        fun (name : ParameterName) ->
            let mutable value = Unchecked.defaultof<ScalarExpression>

            if lookup.TryGetValue (name.Value, &value) then
                ValueSome value
            else
                ValueNone

    static let codes (errors : ValidationError seq) = errors |> Seq.map _.Code |> Seq.toArray

    [<TestMethod>]
    member _.``Inline mode replaces parameters in every clause`` () =
        let query = {
            selectRoot with
                Top = ValueSome (SpecValue.Parameter (ParameterName "@top"))
                Where =
                    ValueSome (
                        binary
                            BinaryOperator.And
                            (binary BinaryOperator.GreaterThanOrEqual (alias "root" |> prop "age") (param "@minAge"))
                            (call "ARRAY_CONTAINS" [ param "@tags"; alias "root" |> prop "tag" ])
                    )
        }

        let encode =
            encoder [
                struct ("@top", integer 10L)
                struct ("@minAge", integer 18L)
                struct ("@tags", arrayOf [ str "a"; str "b" ])
            ]

        let printed =
            Printer.printInline PropertyStyle.Brackets encode query
            |> Result.defaultWith (fun errors -> $"%A{errors}")

        Assert.AreEqual (
            "SELECT TOP 10 VALUE root FROM root WHERE ((root[\"age\"] >= 18) AND ARRAY_CONTAINS([\"a\", \"b\"], root[\"tag\"]))",
            printed,
            "Every parameter should be replaced by its literal"
        )

        Assert.AreEqual (
            "SELECT TOP @top VALUE root FROM root WHERE ((root[\"age\"] >= @minAge) AND ARRAY_CONTAINS(@tags, root[\"tag\"]))",
            Printer.print PropertyStyle.Brackets query,
            "The parameterized mode should keep the parameters"
        )

    [<TestMethod>]
    member _.``Inline mode replaces parameters in subqueries, OFFSET and LIMIT`` () =
        let subquery = {
            selectValue (alias "t") with
                From =
                    ValueSome (
                        CollectionExpression.ArrayIterator (
                            Identifier "t",
                            Collection.InputPath (Identifier "c", ValueSome (PathExpression.String (ValueNone, "tags")))
                        )
                    )
                Where = ValueSome (binary BinaryOperator.Equal (alias "t") (param "@tag"))
        }

        let query = {
            selectValue (alias "c") with
                From = from "c"
                Where = ValueSome (ScalarExpression.Exists subquery)
                OffsetLimit =
                    ValueSome (struct (SpecValue.Parameter (ParameterName "@skip"), SpecValue.Parameter (ParameterName "@take")))
        }

        let encode =
            encoder [ struct ("@tag", str "vip"); struct ("@skip", integer 20L); struct ("@take", integer 10L) ]

        Assert.AreEqual (
            "SELECT VALUE c FROM c WHERE EXISTS(SELECT VALUE t FROM t IN c[\"tags\"] WHERE (t = \"vip\")) OFFSET 20 LIMIT 10",
            Printer.printInline PropertyStyle.Brackets encode query
            |> Result.defaultWith (fun errors -> $"%A{errors}"),
            "Parameters in a subquery, OFFSET and LIMIT should be replaced"
        )

    [<TestMethod>]
    member _.``A predicate prints inline as the filter text of a patch`` () =
        let predicate = binary BinaryOperator.GreaterThan (alias "c" |> prop "qty") (param "@min")

        Assert.AreEqual (
            "FROM c WHERE (c[\"qty\"] > 0)",
            Printer.printPredicateInline
                PropertyStyle.Brackets
                (encoder [ struct ("@min", integer 0L) ])
                (Identifier "c")
                predicate
            |> Result.defaultWith (fun errors -> $"%A{errors}"),
            "The filter predicate of a patch takes no parameters, so they should be written as literals"
        )

    [<TestMethod>]
    member _.``A parameter without an inline value is reported once`` () =
        let query = {
            selectRoot with
                Where =
                    ValueSome (
                        binary
                            BinaryOperator.Or
                            (binary BinaryOperator.Equal (param "@x") (integer 1L))
                            (binary BinaryOperator.Equal (param "@x") (integer 2L))
                    )
        }

        match Inline.substituteParameters (encoder []) query with
        | Ok _ -> Assert.Fail "A parameter without a value should fail the substitution."
        | Error errors ->
            let error = Assert.ContainsSingle (errors, "A parameter used twice should be reported once.")
            Assert.AreEqual (ValidationErrorCode.MissingInlineValue, error.Code, "The error should name the missing value.")
            Assert.Contains ("@x", error.Message, StringComparison.Ordinal, "The message should name the parameter.")

    [<TestMethod>]
    member _.``Inline values must be constants and counts integers`` () =
        let query = {
            selectRoot with
                Top = ValueSome (SpecValue.Parameter (ParameterName "@top"))
                Where = ValueSome (binary BinaryOperator.Equal (alias "root" |> prop "a") (param "@value"))
        }

        let encode = encoder [ struct ("@top", str "ten"); struct ("@value", alias "root" |> prop "b") ]

        match Inline.substituteParameters encode query with
        | Ok _ -> Assert.Fail "Unusable inline values should fail the substitution."
        | Error errors ->
            CollectionAssert.AreEqual (
                [| ValidationErrorCode.InlineCountNotInteger; ValidationErrorCode.InlineValueNotConstant |],
                codes errors,
                "A string TOP and a property path value should both be reported, in clause order."
            )

    [<TestMethod>]
    member _.``Constants are literals and array or object literals of literals`` () =
        Assert.IsTrue (Inline.isConstant (integer 1L), "A literal should be a constant.")
        Assert.IsTrue (
            Inline.isConstant (objectOf [ struct ("a", arrayOf [ nul; str "x" ]) ]),
            "Nested literals should be a constant."
        )

        Assert.IsFalse (Inline.isConstant (param "@p"), "A parameter should not be a constant.")
        Assert.IsFalse (Inline.isConstant (arrayOf [ alias "c" ]), "An array with a reference should not be a constant.")

    [<TestMethod>]
    member _.``JSON values become the literals that print the same JSON`` () =
        use document =
            JsonDocument.Parse
                """{"name":"a\"b","count":3,"price":2.5,"big":1e400,"flag":true,"none":null,"tags":["x",1],"nested":{"k":-7}}"""

        let literal = Inline.ofJsonElement document.RootElement

        Assert.AreEqual (
            objectOf [
                struct ("name", str "a\"b")
                struct ("count", integer 3L)
                struct ("price", number 2.5)
                struct ("big", number Double.PositiveInfinity)
                struct ("flag", boolean true)
                struct ("none", nul)
                struct ("tags", arrayOf [ str "x"; integer 1L ])
                struct ("nested", objectOf [ struct ("k", integer -7L) ])
            ],
            literal,
            "Every JSON kind should map to its literal, with property order kept"
        )

        Assert.AreEqual (
            undefined,
            Inline.ofJsonElement Unchecked.defaultof<JsonElement>,
            "The default JsonElement holds no value."
        )

        let serialized = JsonSerializer.SerializeToElement {| Id = "1"; Score = 0.75 |}

        Assert.AreEqual (
            "{\"Id\": \"1\", \"Score\": 0.75}",
            Printer.printScalar PropertyStyle.Brackets (Inline.ofJsonElement serialized),
            "A serialized value should print as the JSON it was serialized to"
        )
