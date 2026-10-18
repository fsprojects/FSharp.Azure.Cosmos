namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql
open FSharp.Azure.Cosmos.Tests

/// An element that writes its number into the list it is given whenever it is asked for its hash code.
[<Sealed>]
type private HashProbe (number : int, asked : List<int>) =

    /// Records the request and gives the number as the hash code.
    override _.GetHashCode () =
        asked.Add number
        number

    /// Equal to itself only: the test needs no equal probes.
    override this.Equals (other : objnull) = obj.ReferenceEquals (this, other)

/// <summary>
/// The struct <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArray`1"/>: its equality and hash code, its default
/// value, its views and its implicit conversions.
/// </summary>
[<TestClass; UnitTestCategory; CollectionsTestCategory>]
type EquatableArrayTests () =

    [<TestMethod>]
    member _.``Arrays with equal elements in the same order are equal and hash alike`` () =
        let first = EquatableArray.unsafeOfArray [| 1; 2; 3 |]
        let second = EquatableArray.unsafeOfArray [| 1; 2; 3 |]

        Assert.IsTrue (first.Equals second, "Arrays built separately from equal elements should be equal.")
        Assert.AreEqual (first, second, "Object equality should agree with the typed equality.")
        Assert.AreEqual (first.GetHashCode (), second.GetHashCode (), "Equal arrays should have equal hash codes.")

    [<TestMethod>]
    member _.``Arrays that differ in order, length or an element are not equal`` () =
        let array = EquatableArray.unsafeOfArray [| 1; 2; 3 |]

        Assert.AreNotEqual (EquatableArray.unsafeOfArray [| 3; 2; 1 |], array, "The order of the elements should matter.")
        Assert.AreNotEqual (EquatableArray.unsafeOfArray [| 1; 2 |], array, "The length should matter.")
        Assert.AreNotEqual (EquatableArray.unsafeOfArray [| 1; 2; 4 |], array, "Every element should matter.")
        Assert.IsFalse (array.Equals (box [| 1; 2; 3 |]), "An array of another type should not be equal.")

    [<TestMethod>]
    member _.``The hash code takes in every element once, first to last`` () =
        let asked = List<int>()
        // Twenty elements, because the structural hash code of an ImmutableArray takes in only the last eight
        let array = EquatableArray.unsafeOfArray (Array.init 20 (fun number -> HashProbe (number, asked)))

        array.GetHashCode () |> ignore

        CollectionAssert.AreEqual (
            Array.init 20 id,
            asked.ToArray (),
            "Every element should be asked for its hash code once, in the order of the array."
        )

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
        let first = EquatableArray.unsafeOfArray [| "a"; "b" |]
        let second = EquatableArray.unsafeOfArray [| "a"; System.String ([| 'b' |]) |]

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

        // Assert.Contains asks the set itself, so the lookup goes through the hash code and the equality of the array
        Assert.Contains (EquatableArray second, set, "A hash set with the default comparer should find an equal array.")

    [<TestMethod>]
    member _.``The read-only list view and the enumerator yield the elements in order`` () =
        let array = EquatableArray.unsafeOfArray [| 10; 20; 30 |]
        let list = array :> IReadOnlyList<int>

        Assert.HasCount (3, list, "The list view should report the length.")
        Assert.AreEqual (20, list[1], "The list view should index the elements.")
        CollectionAssert.AreEqual ([| 10; 20; 30 |], Seq.toArray list, "The list view should enumerate in order.")

        let mutable sum = 0

        for item in array do
            sum <- sum + item

        Assert.AreEqual (60, sum, "A for loop over the array should visit every element.")

    // F# applies an implicit conversion without warning FS3391 at method arguments only, so the conversions are
    // exercised through methods that take the other type
    static member private AsEquatable (array : EquatableArray<int>) = array
    static member private AsImmutable (items : ImmutableArray<int>) = items

    [<TestMethod>]
    member _.``An ImmutableArray is accepted where an EquatableArray is expected, and the other way round`` () =
        let immutable = ImmutableArray.Create (4, 5)
        let equatable = EquatableArrayTests.AsEquatable immutable

        Assert.AreEqual (EquatableArray.unsafeOfArray [| 4; 5 |], equatable, "The conversion should keep the elements.")

        // ImmutableArray equality compares the reference of the underlying array, so these assert that nothing was copied
        Assert.AreEqual<ImmutableArray<int>>(immutable, equatable.Items, "The conversion should wrap, not copy, the array.")

        Assert.AreEqual<ImmutableArray<int>>(
            immutable,
            EquatableArrayTests.AsImmutable equatable,
            "The conversion back should hand out the wrapped array."
        )

    [<TestMethod>]
    member _.``The implicit conversions turn a default array into an empty one`` () =
        let fromDefault = EquatableArrayTests.AsEquatable Unchecked.defaultof<ImmutableArray<int>>
        let toImmutable = EquatableArrayTests.AsImmutable Unchecked.defaultof<EquatableArray<int>>

        Assert.AreEqual (EquatableArray.empty<int>, fromDefault, "A default ImmutableArray should convert to the empty array.")
        Assert.IsFalse (toImmutable.IsDefault, "The default value should not convert to a default ImmutableArray.")
        Assert.IsEmpty (toImmutable, "The default value should convert to an empty ImmutableArray.")

    [<TestMethod>]
    member _.``ToString shows the elements like an F# array`` () =
        // F# itself is the reference: [|1; 2|] with no space inside the brackets, [||] for no element, a string in quotes
        let check (items : 'T array) (message : string) =
            Assert.AreEqual<string>($"%A{items}", (EquatableArray.unsafeOfArray items).ToString(), message)

        check [| 1; 2 |] "ToString should list the elements the way F# shows an array."
        check Array.empty<int> "ToString should show no element the way F# does."
        check [| "a"; "b" |] "ToString should show each element the way F# does."
