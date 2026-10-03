// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/RecheckAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/RecheckAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

namespace Hedgehog.MSTest

open System

/// Runs Property.reportRecheck
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple = false)>]
type RecheckAttribute(recheckData) =
    inherit Attribute()

    let _recheckData: string = recheckData

    member internal _.GetRecheckData = _recheckData
