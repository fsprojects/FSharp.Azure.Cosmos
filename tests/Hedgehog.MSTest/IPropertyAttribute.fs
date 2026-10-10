// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/IPropertyAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/IPropertyAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the Seed setting is added; a setting that is not
// given is a voption instead of an option.
// The file is formatted with Fantomas, under the settings of this repository.

namespace Hedgehog.MSTest

open System
open Hedgehog

// Represents the interface for property attributes used in property-based testing.
// This interface is shared between Property and Properties attributes.
[<Interface>]
type internal IPropertyAttribute =
    abstract member AutoGenConfig : Type voption with get, set
    abstract member AutoGenConfigArgs : objnull array with get, set
    abstract member Tests : int<tests> voption with get, set
    abstract member Shrinks : int<shrinks> voption with get, set
    abstract member Size : Size voption with get, set
    abstract member Seed : uint64 voption with get, set
