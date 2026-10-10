// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/PropertyContext.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/PropertyContext.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the Seed setting is added; class-level settings
// are read from the reflected type of the method instead of its declaring type, and a derived class's settings win over
// its base class's; a setting that is not given is a voption instead of an option, and the attributes are folded from
// a sequence expression instead of an appended list.
// The file is formatted with Fantomas, under the settings of this repository.

namespace Hedgehog.MSTest

open System.Linq
open System.Reflection
open Hedgehog
open Hedgehog.FSharp
// For the collection functions of this repository that return a voption or struct tuples
open FSharp.Azure.Cosmos

/// Represents the context for property-based testing, including configuration and test parameters.
type internal PropertyContext = {
    AutoGenConfig : IAutoGenConfig
    Tests : int<tests> voption
    Shrinks : int<shrinks> voption
    Size : Size voption
    Seed : uint64 voption
    Recheck : string voption
}

module internal PropertyContext =
    let defaults : PropertyContext = {
        AutoGenConfig = AutoGenConfig.defaults
        Tests = ValueNone
        Shrinks = ValueNone
        Size = ValueNone
        Seed = ValueNone
        Recheck = ValueNone
    }

    let private append (ctx : PropertyContext) (attr : IPropertyAttribute) : PropertyContext =
        let config =
            match attr.AutoGenConfig with
            | ValueSome t ->
                AutoGenConfig.instantiate t attr.AutoGenConfigArgs
                |> AutoGenConfig.merge ctx.AutoGenConfig
            | ValueNone -> ctx.AutoGenConfig

        {
            ctx with
                AutoGenConfig = config
                Tests = attr.Tests |> ValueOption.orElse ctx.Tests
                Shrinks = attr.Shrinks |> ValueOption.orElse ctx.Shrinks
                Size = attr.Size |> ValueOption.orElse ctx.Size
                Seed = attr.Seed |> ValueOption.orElse ctx.Seed
        }

    let fromMethod (method : MethodInfo) =
        let propertyAttribute =
            method.GetCustomAttributes().OfType<IPropertyAttribute>()
            |> Seq.exactlyOne

        // MSTest runs a property inherited from an abstract base class once per derived test class: the class it runs is
        // the reflected type of the method, while the declaring type is the base class. Type.getAllAttributes lists a
        // class before its base classes; reversed, the settings of a derived class are applied last and win.
        let classAttributes =
            method.ReflectedType
            |> Type.getAllAttributes<IPropertyAttribute>
            |> Array.rev

        let context =
            seq {
                yield! classAttributes
                yield propertyAttribute
            }
            |> Seq.fold append defaults

        let recheckData =
            method.GetCustomAttributes<RecheckAttribute>()
            |> Seq.tryHead
            |> ValueOption.map _.GetRecheckData

        { context with Recheck = recheckData }
