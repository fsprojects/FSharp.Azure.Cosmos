// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/PropertiesAttribute.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/PropertiesAttribute.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the Seed setting is added; the primary
// constructor, whose optional parameters an attribute cannot take, is private, as in PropertyAttribute; a setting that
// is not given is a voption instead of an option; the documentation is rewritten.
// The file is formatted with Fantomas, under the settings of this repository.

namespace Hedgehog.MSTest

open System
open Hedgehog

/// <summary>
/// Sets the defaults of every <see cref="T:Hedgehog.MSTest.PropertyAttribute"/> method of a test class.
/// <para>
/// The settings come from the class that MSTest runs, so a property inherited from an abstract base class gets the
/// settings of each derived test class. Along the inheritance chain the settings of a derived class win over those of
/// its base classes, and the <see cref="T:Hedgehog.IAutoGenConfig"/> configurations are merged in the same order; a
/// setting of <see cref="T:Hedgehog.MSTest.PropertyAttribute"/> wins over all of them.
/// </para>
/// </summary>
[<AttributeUsage(AttributeTargets.Class, AllowMultiple = false)>]
type PropertiesAttribute private (autoGenConfig, autoGenConfigArgs, tests, shrinks, size) =
    inherit Attribute ()

    let mutable _autoGenConfig : Type voption = autoGenConfig
    let mutable _autoGenConfigArgs : objnull array = autoGenConfigArgs
    let mutable _tests : int<tests> voption = tests
    let mutable _shrinks : int<shrinks> voption = shrinks
    let mutable _size : Size voption = size
    let mutable _seed : uint64 voption = ValueNone

    /// <summary>
    /// A type with exactly one public static member that returns <see cref="T:Hedgehog.IAutoGenConfig"/>, optionally
    /// taking <see cref="P:Hedgehog.MSTest.PropertiesAttribute.AutoGenConfigArgs"/>. Only for setting: reading it throws.
    /// </summary>
    member _.AutoGenConfig
        with set v = _autoGenConfig <- ValueSome v
        and get () : Type = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// The arguments of the member of <see cref="P:Hedgehog.MSTest.PropertiesAttribute.AutoGenConfig"/>. Only for
    /// setting: reading it throws.
    /// </summary>
    member _.AutoGenConfigArgs
        with set v = _autoGenConfigArgs <- AutoGenConfig.argsOrNone v
        and get () : objnull array = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// The number of cases that must pass. Only for setting: reading it throws.
    /// </summary>
    member _.Tests
        with set v = _tests <- ValueSome v
        and get () : int<tests> = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// The largest number of shrink steps of a failure. Only for setting: reading it throws.
    /// </summary>
    member _.Shrinks
        with set v = _shrinks <- ValueSome v
        and get () : int<shrinks> = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// The size of every generated case. Only for setting: reading it throws.
    /// </summary>
    member _.Size
        with set v = _size <- ValueSome v
        and get () : Size = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// The seed of every run, such as one seed per suite. Only for setting: reading it throws.
    /// </summary>
    member _.Seed
        with set v = _seed <- ValueSome v
        and get () : uint64 = failwith "this getter only exists to make C# named arguments work"

    /// <summary>
    /// Sets no defaults; named arguments set them.
    /// </summary>
    new () = PropertiesAttribute (ValueNone, [||], ValueNone, ValueNone, ValueNone)

    /// <summary>
    /// Sets the number of cases that must pass.
    /// </summary>
    /// <param name="tests">The number of cases that must pass.</param>
    new (tests) = PropertiesAttribute (ValueNone, [||], ValueSome tests, ValueNone, ValueNone)

    /// <summary>
    /// Sets the number of cases that must pass and the largest number of shrink steps.
    /// </summary>
    /// <param name="tests">The number of cases that must pass.</param>
    /// <param name="shrinks">The largest number of shrink steps.</param>
    new (tests, shrinks) = PropertiesAttribute (ValueNone, [||], ValueSome tests, ValueSome shrinks, ValueNone)

    /// <summary>
    /// Sets the configuration of the generated arguments.
    /// </summary>
    /// <param name="autoGenConfig">
    /// A type with exactly one public static member that returns <see cref="T:Hedgehog.IAutoGenConfig"/>.
    /// </param>
    new (autoGenConfig) = PropertiesAttribute (ValueSome autoGenConfig, [||], ValueNone, ValueNone, ValueNone)

    /// <summary>
    /// Sets the configuration of the generated arguments and the number of cases that must pass.
    /// </summary>
    /// <param name="autoGenConfig">
    /// A type with exactly one public static member that returns <see cref="T:Hedgehog.IAutoGenConfig"/>.
    /// </param>
    /// <param name="tests">The number of cases that must pass.</param>
    new (autoGenConfig : Type, tests) = PropertiesAttribute (ValueSome autoGenConfig, [||], ValueSome tests, ValueNone, ValueNone)

    /// <summary>
    /// Sets the configuration of the generated arguments, the number of cases that must pass and the largest number of
    /// shrink steps.
    /// </summary>
    /// <param name="autoGenConfig">
    /// A type with exactly one public static member that returns <see cref="T:Hedgehog.IAutoGenConfig"/>.
    /// </param>
    /// <param name="tests">The number of cases that must pass.</param>
    /// <param name="shrinks">The largest number of shrink steps.</param>
    new (autoGenConfig : Type, tests, shrinks)
        =
        PropertiesAttribute (ValueSome autoGenConfig, [||], ValueSome tests, ValueSome shrinks, ValueNone)

    interface IPropertyAttribute with
        member _.AutoGenConfig
            with get () = _autoGenConfig
            and set v = _autoGenConfig <- v
        member _.AutoGenConfigArgs
            with get () = _autoGenConfigArgs
            and set v = _autoGenConfigArgs <- v
        member _.Tests
            with get () = _tests
            and set v = _tests <- v
        member _.Shrinks
            with get () = _shrinks
            and set v = _shrinks <- v
        member _.Size
            with get () = _size
            and set v = _size <- v
        member _.Seed
            with get () = _seed
            and set v = _seed <- v
