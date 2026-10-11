namespace FSharp.Azure.Cosmos.Sql.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open Microsoft.VisualStudio.TestTools.UnitTesting
open Hedgehog
open Hedgehog.FSharp

open FSharp.Azure.Cosmos.Tests.Models

/// Assertions on how often a generator gives one of two outcomes.
[<RequireQualifiedAccess>]
module ShareAssert =

    // A share is counted over 2000 draws. For an outcome with the probability one half the standard deviation of that
    // share is 0.011, so these bounds are nine deviations away from it: wide enough to hold for every seed, narrow
    // enough to fail for a generator that favours one outcome
    [<Literal>]
    let private Lowest = 0.4

    [<Literal>]
    let private Highest = 0.6

    /// <summary>
    /// Fails unless <paramref name="share"/> is about one half.
    /// </summary>
    /// <param name="what">The outcome that was counted, for the failure message.</param>
    /// <param name="size">The size the values were drawn at, for the failure message.</param>
    /// <param name="share">The share of the draws that gave the outcome.</param>
    let IsAboutHalf (what : string) (size : int) (share : float) =
        Assert.IsInRange (Lowest, Highest, share, $"%s{what} should be about half of the draws at size %d{size}.")

/// <summary>
/// The generator helpers of the project <c>tests/Cosmos.Sql.Hedgehog</c>:
/// <see cref="T:FSharp.Azure.Cosmos.Tests.Models.Sample"/>, <see cref="T:FSharp.Azure.Cosmos.Tests.Models.Gen"/>,
/// <see cref="T:FSharp.Azure.Cosmos.Tests.Models.CollectionGen"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Tests.Models.TextGen"/>.
/// <para>
/// Every value is drawn at a fixed seed, so a test sees the same values on every run and cannot fail by chance.
/// </para>
/// </summary>
[<TestClass; GeneratorsUnitTestCategory>]
type GeneratorHelperTests () =

    static let seed = 2026UL

    static let draws = 2000

    /// A generator whose value is the size it is drawn at.
    static let sizeGen : Gen<int> = Gen.sized Gen.constant

    static let numberGen : Gen<int> = Gen.int32 (Range.constant 0 1_000_000)

    /// <summary>
    /// The share of the seeded draws of <paramref name="source"/> at <paramref name="size"/> that satisfy
    /// <paramref name="predicate"/>.
    /// </summary>
    static let shareOf (size : int) (predicate : 'T -> bool) (source : Gen<'T>) : float =
        let mutable hits = 0

        for index in 0 .. draws - 1 do
            if predicate (Sample.singleWith (Seed.from (seed + uint64 index)) size source) then
                hits <- hits + 1

        float hits / float draws

    /// <summary>
    /// The values of the seeded draws of <paramref name="source"/>, which run through every size.
    /// </summary>
    static let sampleOf (source : Gen<'T>) : ImmutableArray<'T> = Sample.multipleWith seed draws source

    [<TestMethod>]
    member _.``singleWith draws the same value again for the same seed and size`` () =
        let first = Sample.singleWith (Seed.from seed) 50 numberGen
        let second = Sample.singleWith (Seed.from seed) 50 numberGen
        let other = Sample.singleWith (Seed.from (seed + 1UL)) 50 numberGen

        Assert.AreEqual (first, second, "The same seed and size should give the same value.")
        Assert.AreNotEqual (first, other, "Another seed should give another value of a generator with a million values.")

    [<TestMethod>]
    member _.``multipleWith draws the same values again and runs through the sizes from 1 to 100`` () =
        let first = Sample.multipleWith seed 250 numberGen
        let second = Sample.multipleWith seed 250 numberGen
        let sizes = Sample.multipleWith seed 250 sizeGen
        let expectedSizes = Array.init 250 (fun index -> 1 + index % 100)

        CollectionAssert.AreEqual (Seq.toArray first, Seq.toArray second, "The same seed should give the same values.")
        CollectionAssert.AreEqual (expectedSizes, Seq.toArray sizes, "The value at index i should have the size 1 + i % 100.")

    [<TestMethod>]
    member _.``multipleWith gives a value the seed of its index`` () =
        let values = Sample.multipleWith seed 3 numberGen

        Assert.AreEqual (
            Sample.singleWith (Seed.from (seed + 2UL)) 3 numberGen,
            values[2],
            "The value at index 2 should be the one of the seed plus 2 at the size 3."
        )

    [<TestMethod>]
    member _.``single and multiple draw at a size from 1 to 100`` () =
        let size = Sample.single sizeGen
        let sizes = Sample.multiple 20 sizeGen

        Assert.IsInRange (1, 100, size, "A single value should be drawn at a size from 1 to 100.")
        Assert.HasCount (20, sizes, "As many values as asked for should be drawn.")
        Assert.IsInRange (1, 100, sizes[0], "Several values should be drawn at a size from 1 to 100.")

        Assert.IsTrue (sizes |> Seq.forall (fun other -> other = sizes[0]), "Several values of one call should share one size.")

    [<TestMethod>]
    [<DataRow(1, DisplayName = "at the smallest size")>]
    [<DataRow(50, DisplayName = "at a middle size")>]
    [<DataRow(100, DisplayName = "at the largest size")>]
    member _.``The balanced generators of optional values give an absent value about half of the time`` (size : int) =
        Gen.optionBalanced numberGen
        |> shareOf size _.IsNone
        |> ShareAssert.IsAboutHalf "None" size

        Gen.voptionBalanced numberGen
        |> shareOf size _.IsNone
        |> ShareAssert.IsAboutHalf "ValueNone" size

        Gen.skippableBalanced numberGen
        |> shareOf size _.IsSkip
        |> ShareAssert.IsAboutHalf "Skip" size

    [<TestMethod>]
    member _.``The sized generators of optional values make an absent value rarer as the size grows`` () =
        // The absent case weighs 2 against 1 + size: one half at the size 1 and 2 of 103 at the size 100
        Gen.voption numberGen
        |> shareOf 1 _.IsNone
        |> ShareAssert.IsAboutHalf "ValueNone" 1

        Gen.skippable numberGen
        |> shareOf 1 _.IsSkip
        |> ShareAssert.IsAboutHalf "Skip" 1

        Assert.IsInRange (
            0.001,
            0.06,
            Gen.voption numberGen |> shareOf 100 _.IsNone,
            "ValueNone should be rare, but still drawn, at the size 100."
        )

        Assert.IsInRange (
            0.001,
            0.06,
            Gen.skippable numberGen |> shareOf 100 _.IsSkip,
            "Skip should be rare, but still drawn, at the size 100."
        )

    [<TestMethod>]
    member _.``The collection generators build from an empty collection up to their largest size`` () =
        let arrays = sampleOf (CollectionGen.immutableArray numberGen)
        let hashSets = sampleOf (CollectionGen.immutableHashSet numberGen)
        let dictionaries = sampleOf (CollectionGen.immutableDictionary numberGen numberGen)
        let sortedSets = sampleOf (CollectionGen.immutableSortedSet (Range.constant 2 4) numberGen)
        let sets = sampleOf (CollectionGen.set numberGen)
        let maps = sampleOf (CollectionGen.map numberGen numberGen)

        let lengthsOf (lengths : int seq) = lengths |> Seq.distinct |> Seq.sort |> Seq.toArray

        CollectionAssert.AreEqual (
            [| 0..5 |],
            arrays |> Seq.map _.Length |> lengthsOf,
            "An immutable array should have 0 to 5 elements."
        )

        CollectionAssert.AreEqual (
            [| 0..5 |],
            hashSets |> Seq.map _.Count |> lengthsOf,
            "An immutable hash set should have 0 to 5 elements."
        )

        CollectionAssert.AreEqual (
            [| 0..3 |],
            dictionaries |> Seq.map _.Count |> lengthsOf,
            "An immutable dictionary should have 0 to 3 entries."
        )

        CollectionAssert.AreEqual (
            [| 2..4 |],
            sortedSets |> Seq.map _.Count |> lengthsOf,
            "An immutable sorted set should have as many elements as its range says."
        )

        CollectionAssert.AreEqual ([| 0..5 |], sets |> Seq.map _.Count |> lengthsOf, "A set should have 0 to 5 elements.")
        CollectionAssert.AreEqual ([| 0..3 |], maps |> Seq.map _.Count |> lengthsOf, "A map should have 0 to 3 entries.")

    [<TestMethod>]
    [<DataRow(1, DisplayName = "at the smallest size")>]
    [<DataRow(100, DisplayName = "at the largest size")>]
    member _.``The balanced collection generators give an empty collection about half of the time`` (size : int) =
        CollectionGen.immutableArrayBalanced numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty immutable array" size

        CollectionGen.immutableHashSetBalanced numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty immutable hash set" size

        CollectionGen.immutableDictionaryBalanced numberGen numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty immutable dictionary" size

        CollectionGen.immutableSortedSetBalanced 4 numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty immutable sorted set" size

        CollectionGen.setBalanced numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty set" size

        CollectionGen.mapBalanced numberGen numberGen
        |> shareOf size _.IsEmpty
        |> ShareAssert.IsAboutHalf "An empty map" size

    [<TestMethod>]
    member _.``A balanced collection that is not empty keeps to its largest size`` () =
        let arrays = sampleOf (CollectionGen.immutableArrayBalanced numberGen)
        let maps = sampleOf (CollectionGen.mapBalanced numberGen numberGen)
        let sortedSets = sampleOf (CollectionGen.immutableSortedSetBalanced 4 numberGen)

        Assert.AreEqual (5, arrays |> Seq.map _.Length |> Seq.max, "A balanced immutable array should have up to 5 elements.")
        Assert.AreEqual (3, maps |> Seq.map _.Count |> Seq.max, "A balanced map should have up to 3 entries.")

        Assert.AreEqual (
            4,
            sortedSets |> Seq.map _.Count |> Seq.max,
            "A balanced immutable sorted set should have up to the number of elements it was given."
        )

    [<TestMethod>]
    member _.``A dictionary and a map keep the last value of a key that repeats`` () =
        // Every draw of the value generator is numbered in the order the entries are drawn, so the value that an entry
        // holds says which draw it came from, and the number of the last draw is the number of draws
        let drawn = ref 0
        let numbered =
            Gen.constant ()
            |> Gen.map (fun () ->
                drawn.Value <- drawn.Value + 1
                drawn.Value
            )
        let oneKey = Gen.constant "key"

        let check (name : string) (source : Gen<#IReadOnlyDictionary<string, int>>) =
            let mutable repeated = false

            for index in 0 .. draws - 1 do
                drawn.Value <- 0
                let collection = Sample.singleWith (Seed.from (seed + uint64 index)) (1 + index % 100) source

                if drawn.Value = 0 then
                    Assert.IsFalse (collection.ContainsKey "key", $"A %s{name} built from no entry should be empty.")
                else
                    repeated <- repeated || drawn.Value > 1

                    Assert.AreEqual (
                        drawn.Value,
                        collection["key"],
                        $"A %s{name} should keep the value of the last of the entries that have its key."
                    )

            Assert.IsTrue (repeated, $"Some of the draws of a %s{name} should have had a key that repeats.")

        check "dictionary" (CollectionGen.immutableDictionary oneKey numbered)
        check "map" (CollectionGen.map oneKey numbered)

    [<TestMethod>]
    member _.``capitalize makes the first character upper case by the invariant culture`` () =
        let culture = CultureInfo.CurrentCulture

        try
            // In Turkish the upper case of "i" is the dotted "İ"; the invariant culture gives "I"
            CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo "tr-TR"

            let capitalized =
                Gen.constant "istanbul"
                |> TextGen.capitalize
                |> Sample.singleWith (Seed.from seed) 1

            let empty =
                Gen.constant ""
                |> TextGen.capitalize
                |> Sample.singleWith (Seed.from seed) 1

            Assert.AreEqual ("Istanbul", capitalized, "The first character should be upper-cased by the invariant culture.")
            Assert.AreEqual ("", empty, "An empty text should stay empty.")
        finally
            CultureInfo.CurrentCulture <- culture

    [<TestMethod>]
    member _.``nameString keeps to its lengths and to ASCII letters and digits`` () =
        let names = sampleOf (TextGen.nameString 3 12)

        Assert.IsTrue (
            names
            |> Seq.forall (fun name -> name.Length >= 3 && name.Length <= 12),
            "A name should be 3 to 12 characters long when asked for that."
        )

        Assert.IsTrue (
            names
            |> Seq.forall (String.forall Char.IsAsciiLetterOrDigit),
            "A name should hold ASCII letters and digits only."
        )

        Assert.AreEqual (12, names |> Seq.map _.Length |> Seq.max, "The longest length should be reached.")

    [<TestMethod>]
    member _.``notes can be empty and are at most 128 characters long`` () =
        let notes = sampleOf TextGen.notes

        Assert.IsTrue (notes |> Seq.exists String.IsNullOrEmpty, "Notes should be empty for some draws.")

        // The longest length is reached only at the size 100, and there by one length of 129, so the sample is asked
        // for the upper quarter of the range instead of the last value
        Assert.IsInRange (
            97,
            128,
            notes |> Seq.map _.Length |> Seq.max,
            "The longest notes should come close to 128 characters and never exceed them."
        )
