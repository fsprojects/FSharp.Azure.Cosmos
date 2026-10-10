// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/Prelude.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/Prelude.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; Option.requireSome and Seq.seqTryExactlyOne are
// removed, because the collection helpers of this repository (src/Shared/ValueCollections.fs) give Seq.tryExactlyOne
// with a voption; Type.getAllAttributes returns an array and takes a type that may be null, as the reflected type of a
// method is declared; Gen.sequenceArray is added.
// The file is formatted with Fantomas, under the settings of this repository.

[<AutoOpen>]
module internal Hedgehog.MSTest.Prelude

open System
open System.Linq
open Hedgehog
open Hedgehog.FSharp

module Gen =
    /// <summary>
    /// Turns an array of generators into a generator of arrays.
    /// </summary>
    /// <remarks>
    /// Hedgehog 2.0.4 has <see cref="M:Hedgehog.FSharp.GenTraversable.Gen.sequence``2(``0)"/> for sequences and
    /// <see cref="M:Hedgehog.FSharp.GenTraversable.Gen.sequenceList``1(Microsoft.FSharp.Collections.FSharpList{Hedgehog.Gen{``0}})"/>
    /// for lists, which share one traversal and shrink alike, and nothing for arrays.
    /// </remarks>
    let sequenceArray (gens : Gen<'a> array) : Gen<'a array> = gens |> Gen.sequence |> Gen.map Array.ofSeq

module internal Type =
    /// <summary>
    /// The first attribute of type <typeparamref name="T"/> on the type and on each of its base types, that of the type
    /// itself first.
    /// </summary>
    let getAllAttributes<'T> (t : Type | null) : 'T array =
        t
        // Seq.unfold takes a function that returns an option, and FSharp.Core has no counterpart for a voption
        |> Seq.unfold (fun ty ->
            match ty with
            | null -> None
            | ty -> Some (ty, ty.BaseType)
        )
        |> Seq.collect (fun ty -> ty.GetCustomAttributes(false).OfType<'T>() |> Seq.truncate 1)
        |> Seq.toArray
