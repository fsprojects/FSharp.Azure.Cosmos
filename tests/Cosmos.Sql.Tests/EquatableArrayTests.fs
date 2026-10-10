namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql

/// <summary>
/// Equality and hashing of <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArray`1"/>, and the active patterns over
/// <see cref="T:System.Collections.Immutable.ImmutableArray`1"/>.
/// </summary>
[<TestClass; CollectionsUnitTestCategory>]
type EquatableArrayTests () =

    [<TestMethod>]
    member _.``Arrays with equal elements in the same order are equal and hash alike`` () =
        let first = EquatableArray.ofArray [| 1; 2; 3 |]
        let second = EquatableArray.ofSeq [ 1; 2; 3 ]

        Assert.IsTrue (first.Equals second, "Arrays built separately from equal elements should be equal.")
        Assert.AreEqual (first, second, "Object equality should agree with the typed equality.")
        Assert.AreEqual (first.GetHashCode (), second.GetHashCode (), "Equal arrays should have equal hash codes.")

    [<TestMethod>]
    member _.``Arrays that differ in order, length or an element are not equal`` () =
        let array = EquatableArray.ofArray [| 1; 2; 3 |]

        Assert.AreNotEqual (EquatableArray.ofArray [| 3; 2; 1 |], array, "The order of the elements should matter.")
        Assert.AreNotEqual (EquatableArray.ofArray [| 1; 2 |], array, "The length should matter.")
        Assert.AreNotEqual (EquatableArray.ofArray [| 1; 2; 4 |], array, "Every element should matter.")
        Assert.IsFalse (array.Equals (box [| 1; 2; 3 |]), "An array of another type should not be equal.")

    [<TestMethod>]
    member _.``The hash code combines the elements in order`` () =
        let hash (items : int array) = (EquatableArray.ofArray items).GetHashCode()

        Assert.AreNotEqual (hash [| 1; 2 |], hash [| 2; 1 |], "Swapping two elements should change the hash code.")
        Assert.AreNotEqual (hash [| 1 |], hash [| 1; 1 |], "Repeating an element should change the hash code.")

    [<TestMethod>]
    member _.``The default value behaves as and equals an empty array`` () =
        let defaultArray = Unchecked.defaultof<EquatableArray<int>>

        Assert.IsEmpty (defaultArray, "The default value should have no elements.")
        Assert.IsTrue (defaultArray.IsEmpty, "The default value should be empty.")
        Assert.IsTrue (defaultArray.Items.IsEmpty, "The default value should expose an empty, not a default, array.")
        Assert.AreEqual (EquatableArray.empty<int>, defaultArray, "The default value should equal the empty array.")
        Assert.AreEqual (
            EquatableArray.empty<int>.GetHashCode(),
            defaultArray.GetHashCode (),
            "The default value should hash like the empty array."
        )

    [<TestMethod>]
    member _.``Elements are compared with their own equality`` () =
        let first = EquatableArray.ofArray [| "a"; "b" |]
        let second = EquatableArray.ofArray [| "a"; System.String ([| 'b' |]) |]

        Assert.AreEqual (first, second, "Equal strings in different instances should make the arrays equal.")

    [<TestMethod>]
    member _.``Equality holds under every comparer, unlike the own equality of ImmutableArray`` () =
        let first = ImmutableArray.Create (1, 2)
        let second = ImmutableArray.Create (1, 2)

        // The reason the syntax tree does not hold ImmutableArray: its own equality compares array references
        Assert.IsFalse (
            EqualityComparer<ImmutableArray<int>>.Default.Equals(first, second),
            "ImmutableArray's default equality should compare references."
        )

        Assert.IsFalse (
            (struct (1, first)).Equals(struct (1, second)),
            "A struct tuple of ImmutableArrays should compare references."
        )

        Assert.IsTrue (
            EqualityComparer<EquatableArray<int>>.Default.Equals(EquatableArray first, EquatableArray second),
            "EquatableArray's default equality should compare elements."
        )

        Assert.IsTrue (
            (struct (1, EquatableArray first)).Equals(struct (1, EquatableArray second)),
            "A struct tuple of EquatableArrays should compare elements."
        )

        let set = HashSet<EquatableArray<int>>()
        set.Add (EquatableArray first) |> ignore
        Assert.IsTrue (set.Contains (EquatableArray second), "A hash set with the default comparer should find an equal array.")

    [<TestMethod>]
    member _.``The read-only list view and the enumerator yield the elements in order`` () =
        let array = EquatableArray.ofArray [| 10; 20; 30 |]
        let list = array :> IReadOnlyList<int>

        Assert.HasCount (3, list, "The list view should report the length.")
        Assert.AreEqual (20, list[1], "The list view should index the elements.")
        CollectionAssert.AreEqual ([| 10; 20; 30 |], Seq.toArray list, "The list view should enumerate in order.")

        let mutable sum = 0

        for item in array do
            sum <- sum + item

        Assert.AreEqual (60, sum, "A for loop over the array should visit every element.")

    [<TestMethod>]
    member _.``Map, singleton, empty and the conversion back keep the elements`` () =
        let doubled =
            EquatableArray.ofArray [| 1; 2; 3 |]
            |> EquatableArray.map (fun item -> item * 2)

        Assert.AreEqual (EquatableArray.ofArray [| 2; 4; 6 |], doubled, "Map should apply the function in order.")
        Assert.AreEqual (EquatableArray.ofArray [| 7 |], EquatableArray.singleton 7, "Singleton should hold one element.")
        Assert.IsTrue (EquatableArray.empty<int>.IsEmpty, "The empty array should have no elements.")

        let immutable = ImmutableArray.Create (4, 5)

        let unwrapped =
            EquatableArray.ofImmutableArray immutable
            |> EquatableArray.toImmutableArray

        // ImmutableArray equality compares the reference of the underlying array, so this asserts that nothing was copied
        Assert.AreEqual<ImmutableArray<int>>(immutable, unwrapped, "Wrapping and unwrapping should not copy the array.")

    [<TestMethod>]
    member _.``ToString shows the elements like an F# array`` () =
        Assert.AreEqual ("[| 1; 2 |]", (EquatableArray.ofArray [| 1; 2 |]).ToString(), "ToString should list the elements.")

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
