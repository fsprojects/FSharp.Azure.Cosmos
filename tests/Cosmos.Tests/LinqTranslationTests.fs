namespace FSharp.Azure.Cosmos.Tests

open System
open System.Linq
open System.Linq.Expressions
open System.Threading.Tasks
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; UnitTestCategory; LinqTranslationTestCategory>]
type LinqTranslationTests () =

    [<TestMethod>]
    member _.``Pipe operator is translated in IQueryable expression`` () : Task = task {
        let query = [ 1..10 ].AsQueryable().Where(fun x -> x |> ((>) 5))
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.DoesNotContain (
            "op_PipeRight",
            translatedText,
            StringComparison.Ordinal,
            "Pipe-forward operator calls should be rewritten before Cosmos LINQ translation."
        )
        StringAssert.Contains (translatedText, "Where", "Translated expression should still contain the original query shape.")
        let translatedQuery = query.Provider.CreateQuery<int> translated |> Seq.toArray
        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], translatedQuery, "Translated pipe query should filter values less than 5.")
    }

    [<TestMethod>]
    member _.``Inline composition is translated in IQueryable expression`` () : Task = task {
        let query = [ 1..10 ].AsQueryable().Select(fun x -> (((+) 1 >> (*) 2) x))
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.DoesNotContain (
            "op_ComposeRight",
            translatedText,
            StringComparison.Ordinal,
            "Composition operators should be normalized before Cosmos LINQ translation."
        )
        Assert.DoesNotContain (
            ".Invoke(",
            translatedText,
            StringComparison.Ordinal,
            "Composed F# functions should be inlined for query translation."
        )
        let translatedQuery = query.Provider.CreateQuery<int> translated |> Seq.toArray
        CollectionAssert.AreEqual (
            [| 4; 6; 8; 10; 12; 14; 16; 18; 20; 22 |],
            translatedQuery,
            "Translated composition query should evaluate (x + 1) * 2 for all source values."
        )
    }

    [<TestMethod>]
    member _.``Option helpers are translated in IQueryable expression`` () : Task = task {
        let query =
            [ Some 1; None; Some 5 ].AsQueryable().Where(fun x -> Option.isSome x && Option.defaultValue 0 x > 2)
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.DoesNotContain (
            "IsSome(",
            translatedText,
            StringComparison.Ordinal,
            "Option.isSome should be rewritten to null-check semantics in LINQ expressions."
        )
        Assert.DoesNotContain (
            "DefaultValue(",
            translatedText,
            StringComparison.Ordinal,
            "Option.defaultValue should be rewritten to conditional expressions in LINQ expressions."
        )
        let translatedQuery =
            query.Provider.CreateQuery<option<int>> translated
            |> Seq.toArray
        CollectionAssert.AreEqual ([| Some 5 |], translatedQuery, "Translated option query should retain only Some 5.")
    }

    [<TestMethod>]
    member _.``F# collection Contains is translated to Enumerable.Contains`` () : Task = task {
        let query = [ 1..10 ].AsQueryable().Where(fun x -> Seq.contains x [ 2; 4; 6 ])
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.DoesNotContain (
            "SeqModule.Contains",
            translatedText,
            StringComparison.Ordinal,
            "F# Seq.contains should be rewritten to LINQ Contains for Cosmos translation compatibility."
        )
        StringAssert.Contains (translatedText, "Contains", "Translated expression should contain a LINQ Contains call.")
        let translatedQuery = query.Provider.CreateQuery<int> translated |> Seq.toArray
        CollectionAssert.AreEqual ([| 2; 4; 6 |], translatedQuery, "Translated Contains query should match the expected values.")
    }

    [<TestMethod>]
    member _.``Quotation conversion call can be translated`` () : Task = task {
        let converterType =
            typeof<FSharp.Quotations.Expr>.Assembly.GetType("Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter", true)
        let methodInfo = converterType.GetMethod ("QuotationToExpression", [| typeof<FSharp.Quotations.Expr> |])
        let quotationCall =
            Expression.Call (methodInfo, Expression.Constant (<@ fun x -> x + 1 @>, typeof<FSharp.Quotations.Expr>))
        let translated = LinqTranslation.translateExpression quotationCall
        Assert.AreEqual (
            ExpressionType.Call,
            translated.NodeType,
            "Quotation conversion should translate to a callable expression tree instead of remaining a converter call."
        )
        let translatedText = translated.ToString ()
        Assert.DoesNotContain (
            "QuotationToExpression",
            translatedText,
            StringComparison.Ordinal,
            "LeafExpressionConverter.QuotationToExpression should be evaluated by the translator."
        )
    }

    [<TestMethod>]
    member _.``Custom translator can be registered and cleared`` () : Task = task {
        LinqTranslation.clearCustomExpressionTranslators ()
        let constant = Expression.Constant 1
        LinqTranslation.registerExpressionTranslator (fun expression ->
            match expression with
            | :? ConstantExpression as constantExpr when
                constantExpr.Type = typeof<int>
                && constantExpr.Value :?> int = 1
                ->
                ValueSome (Expression.Constant 2 :> Expression)
            | _ -> ValueNone
        )
        let translated = LinqTranslation.translateExpression constant
        let translatedValue = translated :?> ConstantExpression
        Assert.AreEqual (2, translatedValue.Value :?> int, "Custom translators should participate in expression translation.")
        LinqTranslation.clearCustomExpressionTranslators ()
        let clearedTranslated = LinqTranslation.translateExpression constant
        let clearedValue = clearedTranslated :?> ConstantExpression
        Assert.AreEqual (
            1,
            clearedValue.Value :?> int,
            "Clearing custom translators should restore default translation behavior."
        )
    }
