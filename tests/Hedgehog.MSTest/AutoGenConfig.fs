// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/AutoGenConfig.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/AutoGenConfig.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the generic arguments of a config method are
// inferred from wherever each generic parameter occurs in the parameter types, nested ones such as 'a[] included, and
// every occurrence must give the same type, where upstream takes one argument type per parameter that is itself a
// generic parameter.

namespace Hedgehog.MSTest

open System
open System.Reflection
open Hedgehog

module internal AutoGenConfig =

    // Infers the generic arguments of a generic config method from the runtime types of the arguments it is given
    let private inferGenericArguments (methodInfo: MethodInfo) (configArgs: obj array) : Type array =
        let parameters = methodInfo.GetParameters()

        if parameters.Length <> configArgs.Length then
            failwith
                $"%s{methodInfo.DeclaringType.FullName}.%s{methodInfo.Name} takes %d{parameters.Length} arguments, but AutoGenConfigArgs gives %d{configArgs.Length}."

        let genericParameters = methodInfo.GetGenericArguments()
        let inferred: Type option array = Array.create genericParameters.Length None

        let rec baseTypes (t: Type) =
            seq {
                match t.BaseType with
                | null -> ()
                | baseType ->
                    yield baseType
                    yield! baseTypes baseType
            }

        let rec unify (parameterType: Type) (argumentType: Type) =
            if parameterType.IsGenericParameter then
                let position = parameterType.GenericParameterPosition

                match inferred.[position] with
                | None -> inferred.[position] <- Some argumentType
                | Some earlier when earlier = argumentType -> ()
                | Some earlier ->
                    failwith
                        $"The generic parameter %s{parameterType.Name} of %s{methodInfo.Name} gets both %s{earlier.FullName} and %s{argumentType.FullName} from AutoGenConfigArgs."
            elif parameterType.IsArray && argumentType.IsArray then
                unify (parameterType.GetElementType()) (argumentType.GetElementType())
            elif parameterType.IsGenericType && parameterType.ContainsGenericParameters then
                // The argument's own type, a base type or an interface built from the same generic definition
                let definition = parameterType.GetGenericTypeDefinition()

                seq {
                    yield argumentType
                    yield! baseTypes argumentType
                    yield! argumentType.GetInterfaces()
                }
                |> Seq.tryFind (fun t -> t.IsGenericType && t.GetGenericTypeDefinition() = definition)
                |> Option.iter (fun constructed ->
                    Array.iter2 unify (parameterType.GetGenericArguments()) (constructed.GetGenericArguments()))

        Array.iter2
            (fun (parameter: ParameterInfo) (configArg: obj) ->
                if not (isNull configArg) then
                    unify parameter.ParameterType (configArg.GetType()))
            parameters
            configArgs

        inferred
        |> Array.mapi (fun index inferredType ->
            match inferredType with
            | Some t -> t
            | None ->
                failwith
                    $"The generic parameter %s{genericParameters.[index].Name} of %s{methodInfo.Name} cannot be inferred from AutoGenConfigArgs.")

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
                    methodInfo.MakeGenericMethod(inferGenericArguments methodInfo configArgs)
                else
                    methodInfo

            methodInfo.Invoke(null, configArgs) :?> IAutoGenConfig
