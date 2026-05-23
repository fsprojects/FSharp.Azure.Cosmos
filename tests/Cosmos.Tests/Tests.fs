namespace Tests

open System
open System.Linq
open System.Linq.Expressions
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type TestClass () =

    [<TestMethod>]
    member _.``Pipe operator is translated in IQueryable expression`` () =
        let query = [ 1..10 ].AsQueryable().Where(fun x -> x |> ((>) 5))
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.IsFalse (
            translatedText.Contains ("op_PipeRight", StringComparison.Ordinal),
            $"Pipe-forward operator calls should be rewritten before Cosmos LINQ translation. Actual: {translatedText}"
        )
        StringAssert.Contains (translatedText, "Where", "Translated expression should still contain the original query shape.")

    [<TestMethod>]
    member _.``Inline composition is translated in IQueryable expression`` () =
        let query = [ 1..10 ].AsQueryable().Select(fun x -> ((((+) 1) >> ((*) 2)) x))
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.IsFalse (
            translatedText.Contains ("op_ComposeRight", StringComparison.Ordinal),
            $"Composition operators should be normalized before Cosmos LINQ translation. Actual: {translatedText}"
        )
        Assert.IsFalse (
            translatedText.Contains (".Invoke(", StringComparison.Ordinal),
            "Composed F# functions should be inlined for query translation."
        )

    [<TestMethod>]
    member _.``Option helpers are translated in IQueryable expression`` () =
        let query = [ Some 1; None; Some 5 ].AsQueryable().Where(fun x -> Option.isSome x && Option.defaultValue 0 x > 2)
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.IsFalse (
            translatedText.Contains ("IsSome(", StringComparison.Ordinal),
            "Option.isSome should be rewritten to null-check semantics in LINQ expressions."
        )
        Assert.IsFalse (
            translatedText.Contains ("DefaultValue(", StringComparison.Ordinal),
            "Option.defaultValue should be rewritten to conditional expressions in LINQ expressions."
        )

    [<TestMethod>]
    member _.``F# collection Contains is translated to Enumerable.Contains`` () =
        let query = [ 1..10 ].AsQueryable().Where(fun x -> Seq.contains x [ 2; 4; 6 ])
        let translated = LinqTranslation.translateExpression query.Expression
        let translatedText = translated.ToString ()
        Assert.IsFalse (
            translatedText.Contains ("SeqModule.Contains", StringComparison.Ordinal),
            "F# Seq.contains should be rewritten to LINQ Contains for Cosmos translation compatibility."
        )
        StringAssert.Contains (translatedText, "Contains", "Translated expression should contain a LINQ Contains call.")

    [<TestMethod>]
    member _.``Quotation conversion call can be translated`` () =
        let converterType = Type.GetType ("Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter, FSharp.Core", true)
        let methodInfo =
            converterType.GetMethod (
                "QuotationToExpression",
                [| typeof<FSharp.Quotations.Expr> |]
            )
        let quotationCall =
            Expression.Call (
                methodInfo,
                Expression.Constant (<@ fun x -> x + 1 @>, typeof<FSharp.Quotations.Expr>)
            )
        let translated = LinqTranslation.translateExpression quotationCall
        Assert.AreEqual (
            ExpressionType.Call,
            translated.NodeType,
            "Quotation conversion should translate to a callable expression tree instead of remaining a converter call."
        )
        let translatedText = translated.ToString ()
        Assert.IsFalse (
            translatedText.Contains ("QuotationToExpression", StringComparison.Ordinal),
            "LeafExpressionConverter.QuotationToExpression should be evaluated by the translator."
        )

    [<TestMethod>]
    member _.``Custom translator can be registered and cleared`` () =
        LinqTranslation.clearCustomExpressionTranslators ()
        let constant = Expression.Constant 1
        LinqTranslation.registerExpressionTranslator (fun expression ->
            match expression with
            | :? ConstantExpression as value when value.Type = typeof<int> && value.Value :?> int = 1 ->
                ValueSome (Expression.Constant 2 :> Expression)
            | _ ->
                ValueNone)
        let translated = LinqTranslation.translateExpression constant
        let translatedValue = translated :?> ConstantExpression
        Assert.IsTrue ((translatedValue.Value :?> int) = 2, "Custom translators should participate in expression translation.")
        LinqTranslation.clearCustomExpressionTranslators ()
