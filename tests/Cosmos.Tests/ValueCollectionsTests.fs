namespace FSharp.Azure.Cosmos.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Hedgehog
open Hedgehog.FSharp
open Hedgehog.MSTest
open FSharp.Azure.Cosmos

// The functions of FSharp.Core, which the shadows hide under the short module names
module CoreSeq = Microsoft.FSharp.Collections.Seq
module CoreList = Microsoft.FSharp.Collections.List
module CoreArray = Microsoft.FSharp.Collections.Array

/// <summary>
/// Generates an array of up to eight integers from 0 to 4 for a parameter of a property of
/// <see cref="ValueCollectionsTests"/>.
/// <para>
/// With so few values an array of two or more elements usually repeats one, and about one array in ten is empty and
/// one in ten holds a single element, which are the inputs on which the functions that look an element up differ.
/// </para>
/// </summary>
type SmallItemsAttribute () =
    inherit GenAttribute<int array> ()

    /// <inheritdoc />
    override _.Generator =
        Gen.int32 (Range.constant 0 4)
        |> Gen.array (Range.constant 0 8)

/// <summary>
/// Emulator-free coverage of <see cref="T:FSharp.Azure.Cosmos.ValueCollections"/>: every function that shadows one of
/// <see cref="T:Microsoft.FSharp.Collections.SeqModule"/>, <see cref="T:Microsoft.FSharp.Collections.ListModule"/> or
/// <see cref="T:Microsoft.FSharp.Collections.ArrayModule"/> answers what that function answers, as a
/// <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/> or in struct tuples.
/// <para>
/// The function of FSharp.Core is the oracle of its shadow, so the comparisons are properties: Hedgehog generates the
/// collections, indexes and predicates, and a difference is reported with the smallest input that shows it.
/// </para>
/// </summary>
[<TestClass; ValueCollectionsUnitTestCategory>]
type ValueCollectionsTests () =

    /// Fails unless the shadow answers what the function of FSharp.Core answers.
    static let agree (call : string) (expected : 'T option) (actual : 'T voption) =
        Assert.AreEqual (
            ValueOption.ofOption expected,
            actual,
            $"%s{call} should answer what the function of FSharp.Core answers."
        )

    /// The pairs of FSharp.Core as the struct tuples that a shadow returns.
    static let structPairs (pairs : (int * int) seq) =
        pairs
        |> Seq.map (fun (left, right) -> struct (left, right))
        |> Seq.toArray

    [<Property>]
    member _.``tryHead, tryLast and tryExactlyOne answer what the functions of FSharp.Core answer``
        ([<SmallItems>] items : int array)
        =
        // A sequence that is not an array, so that nothing can answer from the length of the array
        let source = items |> Seq.map id
        let list = List.ofArray items

        agree "Seq.tryHead" (CoreSeq.tryHead source) (Seq.tryHead source)
        agree "Seq.tryLast" (CoreSeq.tryLast source) (Seq.tryLast source)
        agree "Seq.tryExactlyOne" (CoreSeq.tryExactlyOne source) (Seq.tryExactlyOne source)

        agree "List.tryHead" (CoreList.tryHead list) (List.tryHead list)
        agree "List.tryLast" (CoreList.tryLast list) (List.tryLast list)
        agree "List.tryExactlyOne" (CoreList.tryExactlyOne list) (List.tryExactlyOne list)

        agree "Array.tryHead" (CoreArray.tryHead items) (Array.tryHead items)
        agree "Array.tryLast" (CoreArray.tryLast items) (Array.tryLast items)
        agree "Array.tryExactlyOne" (CoreArray.tryExactlyOne items) (Array.tryExactlyOne items)

    // The index starts before the first element and ends past the last one of the longest array
    [<Property>]
    member _.``tryItem answers what the functions of FSharp.Core answer``
        ([<SmallItems>] items : int array, [<Int(-1, 8)>] index : int)
        =
        let source = items |> Seq.map id
        let list = List.ofArray items

        agree "Seq.tryItem" (CoreSeq.tryItem index source) (Seq.tryItem index source)
        agree "List.tryItem" (CoreList.tryItem index list) (List.tryItem index list)
        agree "Array.tryItem" (CoreArray.tryItem index items) (Array.tryItem index items)

    // The predicate holds for the elements up to the threshold: for none at -1, for all at 4, and in between for
    // elements of different values, so that the first match and the last one differ in value and in index
    [<Property>]
    member _.``tryFind, tryFindBack, tryFindIndex, tryFindIndexBack and tryPick answer what the functions of FSharp.Core answer``
        ([<SmallItems>] items : int array, [<Int(-1, 4)>] threshold : int)
        =
        let source = items |> Seq.map id
        let list = List.ofArray items
        let predicate (item : int) = item <= threshold
        let choose (item : int) = if predicate item then Some (item * 10) else None
        let chooseValue (item : int) = if predicate item then ValueSome (item * 10) else ValueNone

        agree "Seq.tryFind" (CoreSeq.tryFind predicate source) (Seq.tryFind predicate source)
        agree "Seq.tryFindBack" (CoreSeq.tryFindBack predicate source) (Seq.tryFindBack predicate source)
        agree "Seq.tryFindIndex" (CoreSeq.tryFindIndex predicate source) (Seq.tryFindIndex predicate source)
        agree "Seq.tryFindIndexBack" (CoreSeq.tryFindIndexBack predicate source) (Seq.tryFindIndexBack predicate source)
        agree "Seq.tryPick" (CoreSeq.tryPick choose source) (Seq.tryPick chooseValue source)

        agree "List.tryFind" (CoreList.tryFind predicate list) (List.tryFind predicate list)
        agree "List.tryFindBack" (CoreList.tryFindBack predicate list) (List.tryFindBack predicate list)
        agree "List.tryFindIndex" (CoreList.tryFindIndex predicate list) (List.tryFindIndex predicate list)
        agree "List.tryFindIndexBack" (CoreList.tryFindIndexBack predicate list) (List.tryFindIndexBack predicate list)
        agree "List.tryPick" (CoreList.tryPick choose list) (List.tryPick chooseValue list)

        agree "Array.tryFind" (CoreArray.tryFind predicate items) (Array.tryFind predicate items)
        agree "Array.tryFindBack" (CoreArray.tryFindBack predicate items) (Array.tryFindBack predicate items)
        agree "Array.tryFindIndex" (CoreArray.tryFindIndex predicate items) (Array.tryFindIndex predicate items)
        agree "Array.tryFindIndexBack" (CoreArray.tryFindIndexBack predicate items) (Array.tryFindIndexBack predicate items)
        agree "Array.tryPick" (CoreArray.tryPick choose items) (Array.tryPick chooseValue items)

    [<Property>]
    member _.``zip pairs elements into struct tuples and treats lengths as FSharp.Core does``
        ([<SmallItems>] first : int array, [<SmallItems>] second : int array)
        =
        CollectionAssert.AreEqual (
            CoreSeq.zip first second |> structPairs,
            Seq.zip (first |> Seq.map id) (second |> Seq.map id)
            |> Seq.toArray,
            "Seq.zip should pair by position until the shorter sequence ends, as the function of FSharp.Core does."
        )

        // Arrays and lists pair only at equal lengths, so both inputs are cut to the shorter one
        let length = min first.Length second.Length
        let left = Array.truncate length first
        let right = Array.truncate length second

        CollectionAssert.AreEqual (
            CoreArray.zip left right |> structPairs,
            Array.zip left right,
            "Array.zip should pair by index, as the function of FSharp.Core does."
        )

        CollectionAssert.AreEqual (
            CoreList.zip (List.ofArray left) (List.ofArray right)
            |> structPairs,
            List.zip (List.ofArray left) (List.ofArray right)
            |> List.toArray,
            "List.zip should pair by position, as the function of FSharp.Core does."
        )

        if first.Length <> second.Length then
            Assert.ThrowsExactly<ArgumentException>(
                (fun () -> Array.zip first second |> ignore),
                "Array.zip should reject arrays of different lengths, as the function of FSharp.Core does."
            )
            |> ignore

            Assert.ThrowsExactly<ArgumentException>(
                (fun () ->
                    List.zip (List.ofArray first) (List.ofArray second)
                    |> ignore
                ),
                "List.zip should reject lists of different lengths, as the function of FSharp.Core does."
            )
            |> ignore

    [<TestMethod>]
    member _.``A shadow hides only the function of its name, which stays reachable by its full name`` () =
        let shadow : int voption = Seq.tryHead [ 1; 2 ]
        Assert.AreEqual (ValueSome 1, shadow, "Seq.tryHead should be the shadow, which returns a value option.")

        let core : int option = Microsoft.FSharp.Collections.Seq.tryHead [ 1; 2 ]
        Assert.AreEqual (Some 1, core, "The function of FSharp.Core should stay reachable by its full name.")

        CollectionAssert.AreEqual (
            [| 2; 3 |],
            [ 1; 2 ] |> Seq.map ((+) 1) |> Seq.toArray,
            "A function that only FSharp.Core has, such as Seq.map, should resolve to FSharp.Core."
        )
