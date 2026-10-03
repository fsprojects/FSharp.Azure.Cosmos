// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/Prelude.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/Prelude.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

[<AutoOpen>]
module internal Hedgehog.MSTest.Prelude

open System

module Option =
    let requireSome msg =
        function
        | Some x -> x
        | None -> failwith msg

module Seq =
    // https://github.com/dotnet/fsharp/blob/b9942004e8ba19bf73862b69b2d71151a98975ba/src/FSharp.Core/seqcore.fs#L172-L174
    let inline private checkNonNull argName arg =
        if isNull arg then nullArg argName

    // https://github.com/dotnet/fsharp/blob/b9942004e8ba19bf73862b69b2d71151a98975ba/src/FSharp.Core/seq.fs#L1710-L1719
    let seqTryExactlyOne (source: seq<_>) =
        checkNonNull "source" source
        use e = source.GetEnumerator()

        if e.MoveNext() then
            let v = e.Current
            if e.MoveNext() then None else Some v
        else
            None

module internal Type =
    let getAllAttributes<'T> (t: Type) : 'T list =
        t
        |> Seq.unfold (fun ty -> if ty = null then None else Some(ty, ty.BaseType))
        |> Seq.choose (fun ty ->
            ty.GetCustomAttributes(false)
            |> Seq.tryFind (fun attr -> attr :? 'T)
            |> Option.map (fun attr -> attr :?> 'T))
        |> Seq.toList
