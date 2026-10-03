// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/PropertyContext.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/PropertyContext.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

namespace Hedgehog.MSTest

open System.Reflection
open Hedgehog
open Hedgehog.FSharp

/// Represents the context for property-based testing, including configuration and test parameters.
type internal PropertyContext =
    { AutoGenConfig: IAutoGenConfig
      Tests: int<tests> option
      Shrinks: int<shrinks> option
      Size: Size option
      Recheck: string option }

module internal PropertyContext =
    let defaults: PropertyContext =
        { AutoGenConfig = AutoGenConfig.defaults
          Tests = None
          Shrinks = None
          Size = None
          Recheck = None }

    let private append (ctx: PropertyContext) (attr: IPropertyAttribute) : PropertyContext =
        let config =
            match attr.AutoGenConfig with
            | Some t ->
                AutoGenConfig.instantiate t attr.AutoGenConfigArgs
                |> AutoGenConfig.merge ctx.AutoGenConfig
            | None -> ctx.AutoGenConfig

        { ctx with
            AutoGenConfig = config
            Tests = attr.Tests |> Option.orElse ctx.Tests
            Shrinks = attr.Shrinks |> Option.orElse ctx.Shrinks
            Size = attr.Size |> Option.orElse ctx.Size }

    let fromMethod (method: MethodInfo) =
        let propertyAttribute =
            method.GetCustomAttributes()
            |> Seq.cast<obj>
            |> Seq.filter (fun attr -> attr :? IPropertyAttribute)
            |> Seq.exactlyOne
            :?> IPropertyAttribute

        let classAttributes =
            method.DeclaringType |> Type.getAllAttributes<IPropertyAttribute>

        let context =
            [ propertyAttribute ] |> Seq.append classAttributes |> Seq.fold append defaults

        let recheckData =
            method.GetCustomAttributes(typeof<RecheckAttribute>)
            |> Seq.tryHead
            |> Option.map (fun x -> (x :?> RecheckAttribute).GetRecheckData)

        { context with Recheck = recheckData }
