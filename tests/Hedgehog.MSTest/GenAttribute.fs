// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/GenAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/GenAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the non-generic base GenAttribute is added and
// declares Box, which GenAttribute<'a> overrides; the documentation is rewritten.

namespace Hedgehog.MSTest

open System
open Hedgehog
open Hedgehog.FSharp

/// <summary>
/// The non-generic base of <see cref="T:Hedgehog.MSTest.GenAttribute`1"/>.
/// <para>
/// The adapter finds the generator of a parameter through this type, so an attribute derived from another generator
/// attribute, such as one derived from <see cref="T:Hedgehog.MSTest.IntAttribute"/>, is found wherever it sits in the
/// inheritance chain. Upstream's adapters check only the direct base type and auto-generate the value of such a
/// parameter without a warning.
/// </para>
/// </summary>
[<AbstractClass>]
[<AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)>]
type GenAttribute
    /// <summary>
    /// Called by the constructors of derived attributes.
    /// </summary>
    () =
    inherit Attribute()

    /// <summary>
    /// Returns the generator of the parameter with its values boxed.
    /// </summary>
    abstract member Box: unit -> Gen<obj>

/// <summary>
/// Sets the generator of one parameter of a method marked with <see cref="T:Hedgehog.MSTest.PropertyAttribute"/>.
/// <para>
/// It wins over the <see cref="T:Hedgehog.IAutoGenConfig"/> that <see cref="T:Hedgehog.MSTest.PropertyAttribute"/>
/// and <see cref="T:Hedgehog.MSTest.PropertiesAttribute"/> name.
/// </para>
/// </summary>
/// <typeparam name="a">The type of the parameter.</typeparam>
/// <example>
/// <code lang="fsharp">
/// type ConstantInt(i: int) =
///   inherit GenAttribute&lt;int&gt;()
///   override _.Generator = Gen.constant i
///
/// [&lt;Property&gt;]
/// member _.``is always 2`` ([&lt;ConstantInt(2)&gt;] i) =
///   Assert.AreEqual(2, i, "the attribute sets the value")
/// </code>
/// </example>
[<AbstractClass>]
[<AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)>]
type GenAttribute<'a>
    /// <summary>
    /// Called by the constructors of derived attributes.
    /// </summary>
    () =
    inherit GenAttribute()

    /// <summary>
    /// The generator of the parameter.
    /// </summary>
    abstract member Generator: Gen<'a>

    /// <summary>
    /// Returns <see cref="P:Hedgehog.MSTest.GenAttribute`1.Generator"/> with its values boxed.
    /// </summary>
    override this.Box() = this.Generator |> Gen.map box
