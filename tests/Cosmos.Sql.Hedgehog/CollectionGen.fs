/// <summary>
/// Generators of collections, named after the collection type each one builds.
/// <para>
/// Every generator has a twin with the suffix <c>Balanced</c>. The plain one grows with the size from an empty
/// collection, so at a large size it is hardly ever empty. The twin is empty in one draw of two at every size, for data
/// in which an empty collection has to stay common.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Models.CollectionGen

open System.Collections.Generic
open System.Collections.Immutable
open Hedgehog
open Hedgehog.FSharp

// Small on purpose: a failing case is read by a person, and a document of a test stays far below the size limits of
// the database
[<Literal>]
let private MaxElements = 5

[<Literal>]
let private MaxEntries = 3

/// <summary>
/// An empty collection, or one of <paramref name="nonEmpty"/>, with equal weight at every size.
/// </summary>
let private emptyOr (empty : 'Collection) (nonEmpty : Gen<'Collection>) : Gen<'Collection> =
    Gen.frequency [ 1, Gen.constant empty; 1, nonEmpty ]

/// Entries of a key and a value; a dictionary built from them keeps the last value of a key that repeats.
let private entries (range : Range<int>) (key : Gen<'Key>) (value : Gen<'Value>) : Gen<KeyValuePair<'Key, 'Value> array> =
    Gen.map2 (fun key value -> KeyValuePair (key, value)) key value
    |> Gen.array range

let private toImmutableDictionary (entries : KeyValuePair<'Key, 'Value> array) : ImmutableDictionary<'Key, 'Value> =
    let builder = ImmutableDictionary.CreateBuilder<'Key, 'Value>()

    for entry in entries do
        builder[entry.Key] <- entry.Value

    builder.ToImmutable ()

let private toMap (entries : KeyValuePair<'Key, 'Value> array) : Map<'Key, 'Value> =
    entries
    |> Array.fold (fun map entry -> Map.add entry.Key entry.Value map) Map.empty

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableArray`1"/> of up to 5 elements.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let immutableArray (element : Gen<'T>) : Gen<ImmutableArray<'T>> =
    element
    |> Gen.array (Range.linear 0 MaxElements)
    |> Gen.map ImmutableArray.Create<'T>

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableArray`1"/> that is empty or has 1 to 5 elements,
/// with equal weight.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let immutableArrayBalanced (element : Gen<'T>) : Gen<ImmutableArray<'T>> =
    element
    |> Gen.array (Range.linear 1 MaxElements)
    |> Gen.map ImmutableArray.Create<'T>
    |> emptyOr ImmutableArray<'T>.Empty

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableHashSet`1"/> from up to 5 elements; elements that
/// repeat collapse, so the set can be smaller.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let immutableHashSet (element : Gen<'T>) : Gen<ImmutableHashSet<'T>> =
    element
    |> Gen.array (Range.linear 0 MaxElements)
    |> Gen.map ImmutableHashSet.Create<'T>

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableHashSet`1"/> that is empty or built from 1 to 5
/// elements, with equal weight.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let immutableHashSetBalanced (element : Gen<'T>) : Gen<ImmutableHashSet<'T>> =
    element
    |> Gen.array (Range.linear 1 MaxElements)
    |> Gen.map ImmutableHashSet.Create<'T>
    |> emptyOr ImmutableHashSet<'T>.Empty

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableDictionary`2"/> from up to 3 entries; a key that
/// repeats keeps its last value, so the dictionary can be smaller.
/// </summary>
/// <param name="key">The generator of the keys.</param>
/// <param name="value">The generator of the values.</param>
let immutableDictionary (key : Gen<'Key>) (value : Gen<'Value>) : Gen<ImmutableDictionary<'Key, 'Value>> =
    entries (Range.linear 0 MaxEntries) key value
    |> Gen.map toImmutableDictionary

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableDictionary`2"/> that is empty or built from 1 to 3
/// entries, with equal weight.
/// </summary>
/// <param name="key">The generator of the keys.</param>
/// <param name="value">The generator of the values.</param>
let immutableDictionaryBalanced (key : Gen<'Key>) (value : Gen<'Value>) : Gen<ImmutableDictionary<'Key, 'Value>> =
    entries (Range.linear 1 MaxEntries) key value
    |> Gen.map toImmutableDictionary
    |> emptyOr ImmutableDictionary<'Key, 'Value>.Empty

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableSortedSet`1"/> from as many elements as
/// <paramref name="range"/> says; elements that repeat collapse, so the set can be smaller.
/// </summary>
/// <param name="range">The number of elements the set is built from.</param>
/// <param name="element">The generator of the elements.</param>
let immutableSortedSet (range : Range<int>) (element : Gen<'T>) : Gen<ImmutableSortedSet<'T>> =
    element
    |> Gen.array range
    |> Gen.map ImmutableSortedSet.Create<'T>

/// <summary>
/// Generates an <see cref="T:System.Collections.Immutable.ImmutableSortedSet`1"/> that is empty or built from 1 to
/// <paramref name="maxElements"/> elements, with equal weight.
/// </summary>
/// <param name="maxElements">The largest number of elements a set that is not empty is built from.</param>
/// <param name="element">The generator of the elements.</param>
let immutableSortedSetBalanced (maxElements : int) (element : Gen<'T>) : Gen<ImmutableSortedSet<'T>> =
    immutableSortedSet (Range.linear 1 maxElements) element
    |> emptyOr ImmutableSortedSet<'T>.Empty

/// <summary>
/// Generates a <see cref="T:Microsoft.FSharp.Collections.FSharpSet`1"/> from up to 5 elements; elements that repeat
/// collapse, so the set can be smaller.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let set (element : Gen<'T>) : Gen<Set<'T>> =
    element
    |> Gen.array (Range.linear 0 MaxElements)
    |> Gen.map Set.ofArray

/// <summary>
/// Generates a <see cref="T:Microsoft.FSharp.Collections.FSharpSet`1"/> that is empty or built from 1 to 5 elements,
/// with equal weight.
/// </summary>
/// <param name="element">The generator of the elements.</param>
let setBalanced (element : Gen<'T>) : Gen<Set<'T>> =
    element
    |> Gen.array (Range.linear 1 MaxElements)
    |> Gen.map Set.ofArray
    |> emptyOr Set.empty

/// <summary>
/// Generates a <see cref="T:Microsoft.FSharp.Collections.FSharpMap`2"/> from up to 3 entries; a key that repeats keeps
/// its last value, so the map can be smaller.
/// </summary>
/// <param name="key">The generator of the keys.</param>
/// <param name="value">The generator of the values.</param>
let map (key : Gen<'Key>) (value : Gen<'Value>) : Gen<Map<'Key, 'Value>> =
    entries (Range.linear 0 MaxEntries) key value
    |> Gen.map toMap

/// <summary>
/// Generates a <see cref="T:Microsoft.FSharp.Collections.FSharpMap`2"/> that is empty or built from 1 to 3 entries,
/// with equal weight.
/// </summary>
/// <param name="key">The generator of the keys.</param>
/// <param name="value">The generator of the values.</param>
let mapBalanced (key : Gen<'Key>) (value : Gen<'Value>) : Gen<Map<'Key, 'Value>> =
    entries (Range.linear 1 MaxEntries) key value
    |> Gen.map toMap
    |> emptyOr Map.empty
