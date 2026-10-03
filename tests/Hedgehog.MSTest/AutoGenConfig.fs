// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/AutoGenConfig.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/AutoGenConfig.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest.

namespace Hedgehog.MSTest

open System
open Hedgehog

module internal AutoGenConfig =
    let instantiate (configType: Type) (configArgs: obj array) =
        let configArgs = configArgs |> Option.ofObj |> Option.defaultValue [||]

        configType.GetMethods()
        |> Seq.filter (fun p -> p.IsStatic && p.ReturnType = typeof<IAutoGenConfig>)
        |> Seq.seqTryExactlyOne
        |> Option.requireSome
            $"%s{configType.FullName} must have exactly one public static property that returns an AutoGenConfig.

An example type definition:

type %s{configType.Name} =
  static member __ =
    AutoGenConfig.defaults |> AutoGenConfig.addGenerator (Gen.constant 13)
"
        |> fun methodInfo ->
            let methodInfo =
                if methodInfo.IsGenericMethod then
                    methodInfo.GetParameters()
                    |> Array.map _.ParameterType.IsGenericParameter
                    |> Array.zip configArgs
                    |> Array.filter snd
                    |> Array.map (fun (arg, _) -> arg.GetType())
                    |> fun argTypes -> methodInfo.MakeGenericMethod argTypes
                else
                    methodInfo

            methodInfo.Invoke(null, configArgs) :?> IAutoGenConfig
