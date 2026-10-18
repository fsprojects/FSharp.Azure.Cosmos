namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// The active patterns that match an <see cref="T:System.Collections.Immutable.ImmutableArray`1"/> by its length.
/// </summary>
[<TestClass; UnitTestCategory; CollectionsTestCategory>]
type ImmutableArrayPatternsTests () =

    [<TestMethod>]
    member _.``The length patterns match exactly their length`` () =
        let describe (items : ImmutableArray<int>) =
            match items with
            | Arr0 -> "none"
            | Arr1 single -> $"one %d{single}"
            | Arr2 (first, second) -> $"two %d{first} %d{second}"
            | Arr3 (first, second, third) -> $"three %d{first} %d{second} %d{third}"
            | ArrN 4 -> "four"
            | _ -> "more"

        Assert.AreEqual ("none", describe ImmutableArray.Empty, "Arr0 should match an empty array.")
        Assert.AreEqual ("none", describe Unchecked.defaultof<ImmutableArray<int>>, "Arr0 should match a default array.")
        Assert.AreEqual ("one 1", describe (ImmutableArray.Create 1), "Arr1 should match one element.")
        Assert.AreEqual ("two 1 2", describe (ImmutableArray.Create (1, 2)), "Arr2 should match two elements.")
        Assert.AreEqual ("three 1 2 3", describe (ImmutableArray.Create (1, 2, 3)), "Arr3 should match three elements.")
        Assert.AreEqual ("four", describe (ImmutableArray.Create (1, 2, 3, 4)), "ArrN 4 should match four elements.")
        Assert.AreEqual ("more", describe (ImmutableArray.Create (1, 2, 3, 4, 5)), "Longer arrays should fall through.")

    [<TestMethod>]
    member _.``Arr2 binds a struct tuple pattern too`` () =
        match ImmutableArray.Create ("a", "b") with
        | Arr2 (struct (first, second)) ->
            Assert.AreEqual ("ab", first + second, "The struct tuple pattern should bind both elements.")
        | _ -> Assert.Fail "Arr2 should match an array of two elements."
