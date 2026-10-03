namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Sql.Tests.Ast

/// <summary>
/// The structural equality of the syntax tree, which its nodes get from holding their children in
/// <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArray`1"/>.
/// </summary>
[<TestClass; SyntaxTreeUnitTestCategory>]
type SyntaxTests () =

    [<TestMethod>]
    member _.``Separately built syntax trees with equal children are equal and hash alike`` () =
        let build name =
            selectValue (
                objectOf [
                    struct ("defined", call "IS_DEFINED" [ prop name (alias "c") ])
                    struct ("tags", arrayOf [ str "a" ])
                ]
            )

        let first = build "name"
        let second = build "name"

        Assert.AreEqual (first, second, "Two separately built syntax trees with equal children should be equal.")
        Assert.IsTrue (EqualityComparer<SqlQuery>.Default.Equals(first, second), "The default comparer should agree.")
        Assert.AreEqual (first.GetHashCode (), second.GetHashCode (), "Equal syntax trees should have equal hash codes.")
        Assert.AreNotEqual (build "nick", first, "Syntax trees that differ in a child should not be equal.")

    [<TestMethod>]
    member _.``Syntax trees can be dictionary keys`` () =
        let lookup = Dictionary<SqlQuery, string>()
        lookup[selectValue (arrayOf [ integer 1L; integer 2L ])] <- "cached"

        Assert.AreEqual (
            "cached",
            lookup[selectValue (arrayOf [ integer 1L; integer 2L ])],
            "A structurally equal key should find the entry, as the plan cache needs."
        )
