/// <summary>
/// Draws values from a generator outside of a property: the data of a hand-written test, or of a dataset that a whole
/// test class shares.
/// <para>
/// A property gets its values from <see cref="T:Hedgehog.MSTest.PropertyAttribute"/>, the attribute of the Hedgehog
/// MSTest adapter, and never calls these functions.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Models.Sample

open System
open System.Collections.Immutable
open Hedgehog
open Hedgehog.FSharp

// The sizes Hedgehog itself runs a property through
[<Literal>]
let private MinSize = 1

[<Literal>]
let private MaxSize = 100

// A range of Hedgehog grows with the size it is sampled at, so one fixed small size would keep every generator at the
// low end of its range: short strings, small numbers and, as a result, values that repeat
let private randomSize () = Random.Shared.Next (MinSize, MaxSize + 1)

/// <summary>
/// Draws the value that <paramref name="source"/> generates at <paramref name="seed"/> and <paramref name="size"/>.
/// The same seed and size always give the same value, so data drawn this way can be drawn again.
/// </summary>
/// <param name="seed">The seed of the random numbers the value is made from.</param>
/// <param name="size">The size, from 1 to 100, that the ranges of the generator scale with.</param>
/// <param name="source">The generator.</param>
let singleWith (seed : Seed) (size : int) (source : Gen<'T>) : 'T =
    source
    |> Gen.toRandom
    |> Random.run seed size
    |> Tree.outcome

/// <summary>
/// Draws <paramref name="count"/> values from <paramref name="source"/> that can be drawn again: the value at index
/// <c>i</c> has the seed <c>seed + i</c> and the size <c>1 + i % 100</c>, so the values run through every size instead
/// of sharing one.
/// </summary>
/// <param name="seed">The seed of the first value; the call uses the seeds up to <c>seed + count - 1</c>.</param>
/// <param name="count">The number of values.</param>
/// <param name="source">The generator.</param>
let multipleWith (seed : uint64) (count : int) (source : Gen<'T>) : ImmutableArray<'T> =
    let values = ImmutableArray.CreateBuilder<'T> count

    for index in 0 .. count - 1 do
        values.Add (singleWith (Seed.from (seed + uint64 index)) (MinSize + index % MaxSize) source)

    values.MoveToImmutable ()

/// <summary>
/// Draws one value from <paramref name="source"/> at a random seed and a random size from 1 to 100. The value cannot
/// be drawn again; data that a failure must be able to replay comes from <see cref="singleWith"/>.
/// </summary>
/// <param name="source">The generator.</param>
let single (source : Gen<'T>) : 'T = singleWith (Seed.random ()) (randomSize ()) source

/// <summary>
/// Draws <paramref name="count"/> values from <paramref name="source"/> at random seeds and one random size from 1 to
/// 100. The values cannot be drawn again; data that a failure must be able to replay comes from
/// <see cref="multipleWith"/>.
/// </summary>
/// <param name="count">The number of values.</param>
/// <param name="source">The generator.</param>
let multiple (count : int) (source : Gen<'T>) : ImmutableArray<'T> =
    let size = randomSize ()
    let values = ImmutableArray.CreateBuilder<'T> count

    for _ in 1..count do
        values.Add (singleWith (Seed.random ()) size source)

    values.MoveToImmutable ()
