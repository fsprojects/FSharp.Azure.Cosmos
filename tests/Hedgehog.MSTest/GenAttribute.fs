// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/GenAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/GenAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

namespace Hedgehog.MSTest

open System
open Hedgehog
open Hedgehog.FSharp

/// Set a Generator for a parameter of a test annotated with `Property`
///
/// Example usage:
///
/// ```
///
/// type ConstantInt(i: int) =
///   inherit GenAttribute<int>()
///   override _.Generator = Gen.constant i
///
/// [<Property>]
/// let ``is always 2`` ([<ConstantInt(2)>] i) =
///   Assert.AreEqual(2, i)
///
/// ```
[<AbstractClass>]
[<AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)>]
type GenAttribute<'a>() =
    inherit Attribute()

    abstract member Generator: Gen<'a>
    member this.Box() = this.Generator |> Gen.map box
