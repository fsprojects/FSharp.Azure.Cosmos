namespace FSharp.Azure.Cosmos.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Azure.Cosmos

// The functions of FSharp.Core, which the shadows hide under the short module names
module CoreSeq = Microsoft.FSharp.Collections.Seq
module CoreList = Microsoft.FSharp.Collections.List
module CoreArray = Microsoft.FSharp.Collections.Array

/// Collects the answers of a shadow that differ from those of FSharp.Core, so that a failure names all of them at once.
type private Differences () =

    let found = ResizeArray<string>()

    /// Records the call when the shadow does not answer what FSharp.Core answers.
    member _.Expect (call : string, expected : 'T option, actual : 'T voption) =
        let agrees =
            match expected, actual with
            | Some expectedValue, ValueSome actualValue -> expectedValue = actualValue
            | None, ValueNone -> true
            | _ -> false

        if not agrees then
            found.Add $"%s{call}: FSharp.Core gives %A{expected}, the shadow gives %A{actual}"

    /// The recorded differences.
    member _.Found = found

/// <summary>
/// Emulator-free coverage of <see cref="T:FSharp.Azure.Cosmos.ValueCollections"/>: every function that shadows one of
/// <see cref="T:Microsoft.FSharp.Collections.SeqModule"/>, <see cref="T:Microsoft.FSharp.Collections.ListModule"/> or
/// <see cref="T:Microsoft.FSharp.Collections.ArrayModule"/> answers what that function answers, as a
/// <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/> or in struct tuples.
/// </summary>
[<TestClass; ValueCollectionsUnitTestCategory>]
type ValueCollectionsTests () =

    // Empty, one element, repeated elements and a longer run
    static let samples = [| [||]; [| 7 |]; [| 1; 2; 3; 2; 1 |]; [| 4; 4 |]; [| 0..9 |] |]

    static let predicates = [|
        struct ("never", (fun (_ : int) -> false))
        struct ("always", (fun (_ : int) -> true))
        struct ("even", (fun (item : int) -> item % 2 = 0))
        struct ("two", (fun (item : int) -> item = 2))
    |]

    // Before the first element, inside the samples and past the end of most of them
    static let indexes = [| -1; 0; 1; 4; 5 |]

    [<TestMethod>]
    member _.``Seq shadows answer what the functions of FSharp.Core answer`` () =
        let differences = Differences ()

        for sample in samples do
            // A sequence that is not an array, so that nothing can answer from the length of the array
            let source = sample |> Seq.map id

            differences.Expect ($"tryHead %A{sample}", CoreSeq.tryHead source, Seq.tryHead source)
            differences.Expect ($"tryLast %A{sample}", CoreSeq.tryLast source, Seq.tryLast source)
            differences.Expect ($"tryExactlyOne %A{sample}", CoreSeq.tryExactlyOne source, Seq.tryExactlyOne source)

            for index in indexes do
                differences.Expect ($"tryItem %d{index} %A{sample}", CoreSeq.tryItem index source, Seq.tryItem index source)

            for struct (name, predicate) in predicates do
                differences.Expect (
                    $"tryFind %s{name} %A{sample}",
                    CoreSeq.tryFind predicate source,
                    Seq.tryFind predicate source
                )

                differences.Expect (
                    $"tryFindBack %s{name} %A{sample}",
                    CoreSeq.tryFindBack predicate source,
                    Seq.tryFindBack predicate source
                )

                differences.Expect (
                    $"tryFindIndex %s{name} %A{sample}",
                    CoreSeq.tryFindIndex predicate source,
                    Seq.tryFindIndex predicate source
                )

                differences.Expect (
                    $"tryFindIndexBack %s{name} %A{sample}",
                    CoreSeq.tryFindIndexBack predicate source,
                    Seq.tryFindIndexBack predicate source
                )

                differences.Expect (
                    $"tryPick %s{name} %A{sample}",
                    source
                    |> CoreSeq.tryPick (fun item -> if predicate item then Some (item * 10) else None),
                    source
                    |> Seq.tryPick (fun item -> if predicate item then ValueSome (item * 10) else ValueNone)
                )

        Assert.IsEmpty (differences.Found, "Every Seq shadow should answer what the function of FSharp.Core answers.")

    [<TestMethod>]
    member _.``List shadows answer what the functions of FSharp.Core answer`` () =
        let differences = Differences ()

        for sample in samples do
            let source = List.ofArray sample

            differences.Expect ($"tryHead %A{sample}", CoreList.tryHead source, List.tryHead source)
            differences.Expect ($"tryLast %A{sample}", CoreList.tryLast source, List.tryLast source)
            differences.Expect ($"tryExactlyOne %A{sample}", CoreList.tryExactlyOne source, List.tryExactlyOne source)

            for index in indexes do
                differences.Expect ($"tryItem %d{index} %A{sample}", CoreList.tryItem index source, List.tryItem index source)

            for struct (name, predicate) in predicates do
                differences.Expect (
                    $"tryFind %s{name} %A{sample}",
                    CoreList.tryFind predicate source,
                    List.tryFind predicate source
                )

                differences.Expect (
                    $"tryFindBack %s{name} %A{sample}",
                    CoreList.tryFindBack predicate source,
                    List.tryFindBack predicate source
                )

                differences.Expect (
                    $"tryFindIndex %s{name} %A{sample}",
                    CoreList.tryFindIndex predicate source,
                    List.tryFindIndex predicate source
                )

                differences.Expect (
                    $"tryFindIndexBack %s{name} %A{sample}",
                    CoreList.tryFindIndexBack predicate source,
                    List.tryFindIndexBack predicate source
                )

                differences.Expect (
                    $"tryPick %s{name} %A{sample}",
                    source
                    |> CoreList.tryPick (fun item -> if predicate item then Some (item * 10) else None),
                    source
                    |> List.tryPick (fun item -> if predicate item then ValueSome (item * 10) else ValueNone)
                )

        Assert.IsEmpty (differences.Found, "Every List shadow should answer what the function of FSharp.Core answers.")

    [<TestMethod>]
    member _.``Array shadows answer what the functions of FSharp.Core answer`` () =
        let differences = Differences ()

        for sample in samples do
            differences.Expect ($"tryHead %A{sample}", CoreArray.tryHead sample, Array.tryHead sample)
            differences.Expect ($"tryLast %A{sample}", CoreArray.tryLast sample, Array.tryLast sample)
            differences.Expect ($"tryExactlyOne %A{sample}", CoreArray.tryExactlyOne sample, Array.tryExactlyOne sample)

            for index in indexes do
                differences.Expect ($"tryItem %d{index} %A{sample}", CoreArray.tryItem index sample, Array.tryItem index sample)

            for struct (name, predicate) in predicates do
                differences.Expect (
                    $"tryFind %s{name} %A{sample}",
                    CoreArray.tryFind predicate sample,
                    Array.tryFind predicate sample
                )

                differences.Expect (
                    $"tryFindBack %s{name} %A{sample}",
                    CoreArray.tryFindBack predicate sample,
                    Array.tryFindBack predicate sample
                )

                differences.Expect (
                    $"tryFindIndex %s{name} %A{sample}",
                    CoreArray.tryFindIndex predicate sample,
                    Array.tryFindIndex predicate sample
                )

                differences.Expect (
                    $"tryFindIndexBack %s{name} %A{sample}",
                    CoreArray.tryFindIndexBack predicate sample,
                    Array.tryFindIndexBack predicate sample
                )

                differences.Expect (
                    $"tryPick %s{name} %A{sample}",
                    sample
                    |> CoreArray.tryPick (fun item -> if predicate item then Some (item * 10) else None),
                    sample
                    |> Array.tryPick (fun item -> if predicate item then ValueSome (item * 10) else ValueNone)
                )

        Assert.IsEmpty (differences.Found, "Every Array shadow should answer what the function of FSharp.Core answers.")

    [<TestMethod>]
    member _.``zip pairs elements into struct tuples and treats lengths as FSharp.Core does`` () =
        let expected = [| struct (1, "a"); struct (2, "b") |]

        CollectionAssert.AreEqual (
            expected,
            Seq.zip [ 1; 2; 3 ] [ "a"; "b" ] |> Seq.toArray,
            "Seq.zip should pair by position until the shorter sequence ends."
        )

        CollectionAssert.AreEqual (expected, Array.zip [| 1; 2 |] [| "a"; "b" |], "Array.zip should pair by index.")

        CollectionAssert.AreEqual (expected, List.zip [ 1; 2 ] [ "a"; "b" ] |> List.toArray, "List.zip should pair by position.")

        Assert.ThrowsExactly<ArgumentException>(
            (fun () -> Array.zip [| 1; 2; 3 |] [| "a"; "b" |] |> ignore),
            "Array.zip should reject arrays of different lengths, as the function of FSharp.Core does."
        )
        |> ignore

        Assert.ThrowsExactly<ArgumentException>(
            (fun () -> List.zip [ 1; 2; 3 ] [ "a"; "b" ] |> ignore),
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
