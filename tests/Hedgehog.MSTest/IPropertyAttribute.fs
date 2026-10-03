// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/IPropertyAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/IPropertyAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

namespace Hedgehog.MSTest

open System
open Hedgehog

// Represents the interface for property attributes used in property-based testing.
// This interface is shared between Property and Properties attributes.
[<Interface>]
type internal IPropertyAttribute =
    abstract member AutoGenConfig: Type option with get, set
    abstract member AutoGenConfigArgs: obj array with get, set
    abstract member Tests: int<tests> option with get, set
    abstract member Shrinks: int<shrinks> option with get, set
    abstract member Size: Size option with get, set
