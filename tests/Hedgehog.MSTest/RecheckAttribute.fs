// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/RecheckAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/RecheckAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the documentation is rewritten.

namespace Hedgehog.MSTest

open System

/// <summary>
/// Replays the counterexample of a failed run of the property instead of generating new cases.
/// <para>
/// A failed property reports its recheck data, the size, the seed and the shrink path of the counterexample, together
/// with this attribute ready to paste. The replay runs exactly one case, at the recorded size and seed, so it overrides
/// <see cref="P:Hedgehog.MSTest.PropertyAttribute.Size"/> and <see cref="P:Hedgehog.MSTest.PropertyAttribute.Seed"/>.
/// An <see cref="M:Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(System.String)"/> in the replayed case fails it,
/// because a replay cannot discard its only case, and recheck data that cannot be read gives an error result.
/// </para>
/// </summary>
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple = false)>]
type RecheckAttribute
    /// <summary>
    /// Replays the counterexample that <paramref name="recheckData"/> describes.
    /// </summary>
    /// <param name="recheckData">The recheck data that the failed run reported.</param>
    (recheckData) =
    inherit Attribute()

    let _recheckData: string = recheckData

    member internal _.GetRecheckData = _recheckData
