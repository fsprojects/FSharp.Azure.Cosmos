/// <summary>
/// Generators of optional values. The module has the name of <see cref="T:Hedgehog.FSharp.GenModule"/> on purpose: a
/// file that opens the namespace of that module and this one reaches the functions of both through the one name.
/// <para>
/// Two families. The sized ones make an absent value rarer as the size grows, as
/// <see cref="M:Hedgehog.FSharp.GenModule.option``1(Hedgehog.Gen{``0})"/> does. The balanced ones make it as likely as
/// a present one at every size, for data in which absence has to stay common, such as the documents that the
/// translation of a missing or null property is checked against.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Models.Gen

open System.Text.Json.Serialization
open Hedgehog
open Hedgehog.FSharp

// The weight of the absent case in the sized generators; the present case weighs 1 + size
[<Literal>]
let private AbsentWeight = 2

/// <summary>
/// Generates <see cref="F:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> with the weight 2 against
/// <c>1 + size</c> for a value of <paramref name="source"/>, so an absent value becomes rarer as the size grows.
/// </summary>
/// <param name="source">The generator of the present values.</param>
let voption (source : Gen<'T>) : Gen<'T voption> =
    Gen.sized (fun size -> Gen.frequency [ AbsentWeight, Gen.constant ValueNone; 1 + size, Gen.map ValueSome source ])

/// <summary>
/// Generates <see cref="P:System.Text.Json.Serialization.Skippable`1.Skip"/> with the weight 2 against
/// <c>1 + size</c> for a value of <paramref name="source"/>, so a skipped value becomes rarer as the size grows.
/// </summary>
/// <param name="source">The generator of the included values.</param>
let skippable (source : Gen<'T>) : Gen<Skippable<'T>> =
    Gen.sized (fun size -> Gen.frequency [ AbsentWeight, Gen.constant Skip; 1 + size, Gen.map Include source ])

/// <summary>
/// Generates <see cref="P:Microsoft.FSharp.Core.FSharpOption`1.None"/> or a value of <paramref name="source"/> with
/// equal weight at every size. A failing case shrinks towards the absent value first.
/// </summary>
/// <param name="source">The generator of the present values.</param>
let optionBalanced (source : Gen<'T>) : Gen<'T option> = Gen.frequency [ 1, Gen.constant None; 1, Gen.map Some source ]

/// <summary>
/// Generates <see cref="F:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> or a value of
/// <paramref name="source"/> with equal weight at every size. A failing case shrinks towards the absent value first.
/// </summary>
/// <param name="source">The generator of the present values.</param>
let voptionBalanced (source : Gen<'T>) : Gen<'T voption> =
    Gen.frequency [ 1, Gen.constant ValueNone; 1, Gen.map ValueSome source ]

/// <summary>
/// Generates <see cref="P:System.Text.Json.Serialization.Skippable`1.Skip"/> or a value of <paramref name="source"/>
/// with equal weight at every size. A failing case shrinks towards the skipped value first.
/// </summary>
/// <param name="source">The generator of the included values.</param>
let skippableBalanced (source : Gen<'T>) : Gen<Skippable<'T>> = Gen.frequency [ 1, Gen.constant Skip; 1, Gen.map Include source ]
