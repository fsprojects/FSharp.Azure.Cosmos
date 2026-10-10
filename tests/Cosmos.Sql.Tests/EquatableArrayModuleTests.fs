namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Immutable
open System.Runtime.InteropServices
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Sql

/// <summary>
/// The functions of the module <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArrayModule"/>, which create and
/// transform <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArray`1"/> values.
/// </summary>
[<TestClass; CollectionsUnitTestCategory>]
type EquatableArrayModuleTests () =

    [<TestMethod>]
    member _.``ofArray and ofSeq copy the elements in order`` () =
        let source = [| 1; 2; 3 |]
        let fromArray = EquatableArray.ofArray source
        let fromSeq = EquatableArray.ofSeq (Seq.ofArray source)

        source[0] <- 9

        CollectionAssert.AreEqual ([| 1; 2; 3 |], Seq.toArray fromArray, "ofArray should copy the array it is given.")
        CollectionAssert.AreEqual ([| 1; 2; 3 |], Seq.toArray fromSeq, "ofSeq should copy the sequence it is given.")

    [<TestMethod>]
    member _.``unsafeOfArray wraps the array it is given without copying it`` () =
        let source = [| 1; 2; 3 |]
        let array = EquatableArray.unsafeOfArray source

        Assert.AreSame<int array | null>(
            source,
            ImmutableCollectionsMarshal.AsArray array.Items,
            "unsafeOfArray should hold the very array it was given."
        )

        Assert.AreEqual (EquatableArray.unsafeOfArray [| 1; 2; 3 |], array, "The wrapped array should hold the elements.")

    [<TestMethod>]
    member _.``Map, singleton, empty and the conversion back keep the elements`` () =
        let doubled =
            EquatableArray.unsafeOfArray [| 1; 2; 3 |]
            |> EquatableArray.map (fun item -> item * 2)

        Assert.AreEqual (EquatableArray.unsafeOfArray [| 2; 4; 6 |], doubled, "Map should apply the function in order.")
        Assert.AreEqual (EquatableArray.unsafeOfArray [| 7 |], EquatableArray.singleton 7, "Singleton should hold one element.")
        Assert.IsTrue (EquatableArray.empty<int>.IsEmpty, "The empty array should have no elements.")

        let immutable = ImmutableArray.Create (4, 5)

        let unwrapped =
            EquatableArray.ofImmutableArray immutable
            |> EquatableArray.toImmutableArray

        // ImmutableArray equality compares the reference of the underlying array, so this asserts that nothing was copied
        Assert.AreEqual<ImmutableArray<int>>(immutable, unwrapped, "Wrapping and unwrapping should not copy the array.")
