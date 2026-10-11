// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/AutoGenConfig.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/AutoGenConfig.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the generic arguments of a config method are
// inferred from wherever each generic parameter occurs in the parameter types, nested ones such as 'a[] included, and
// every occurrence must give the same type, where upstream takes one argument type per parameter that is itself a
// generic parameter; a type that is not inferred yet is a voption, types are compared through Type.(=), and the one
// config method is found with the Seq.tryExactlyOne of this repository's collection helpers; the error for a type
// without exactly one such method says "member" where upstream says "property", because a method that takes
// AutoGenConfigArgs is accepted as well; the arguments may hold null, a null array of them counts as none where the
// attributes take it (argsOrNone), and a config method that returns no IAutoGenConfig is an error; propertyDefaults is
// added, the configuration that a property starts from, with the default generators of DateOnly and TimeOnly that
// Hedgehog 2.0.4 lacks.
// The file is formatted with Fantomas, under the settings of this repository.

namespace Hedgehog.MSTest

open System
open System.Reflection
open Hedgehog
open Hedgehog.FSharp
// For the collection functions of this repository that return a voption or struct tuples
open FSharp.Azure.Cosmos

module internal AutoGenConfig =

    // Hedgehog 2.0.4 has no generator of DateOnly or TimeOnly: Gen.auto calls a constructor of the type with integers
    // that it generates, and the constructor refuses them. hedgehogqa/fsharp-hedgehog#488 adds the two default
    // generators below to Hedgehog itself, as DefaultGenerators.DateOnly and DefaultGenerators.TimeOnly:
    // https://github.com/hedgehogqa/fsharp-hedgehog/pull/488
    // The adapter registers them for the parameters of a property until a release of Hedgehog has them. Then these
    // three values go, and PropertyContext.defaults takes AutoGenConfig.defaults again; a test of the adapter's suite
    // fails as soon as Hedgehog generates the two types by itself.

    // Dates around 2000-01-01, further away the larger a case is, up to any date at all
    let private dateOnly : Gen<DateOnly> =
        Range.exponentialFrom (DateOnly (2000, 1, 1)).DayNumber DateOnly.MinValue.DayNumber DateOnly.MaxValue.DayNumber
        |> Gen.int32
        |> Gen.map DateOnly.FromDayNumber

    // A linear range: an exponential one would keep the times of half of all sizes within the first tenth of a second
    // after midnight, because a day has 864 billion ticks.
    let private timeOnly : Gen<TimeOnly> =
        Range.linear TimeOnly.MinValue.Ticks TimeOnly.MaxValue.Ticks
        |> Gen.int64
        |> Gen.map TimeOnly

    /// <summary>
    /// The configuration that a property starts from: <see cref="P:Hedgehog.AutoGenConfig.Defaults"/> and generators
    /// of <see cref="T:System.DateOnly"/> and <see cref="T:System.TimeOnly"/>. The configuration that a test names is
    /// merged over it, so a generator of the test for one of the two types wins.
    /// </summary>
    let propertyDefaults : IAutoGenConfig =
        AutoGenConfig.defaults
        |> AutoGenConfig.addGenerator dateOnly
        |> AutoGenConfig.addGenerator timeOnly

    // Infers the generic arguments of a generic config method from the runtime types of the arguments it is given
    let private inferGenericArguments (configType : Type) (methodInfo : MethodInfo) (configArgs : objnull array) : Type array =
        let parameters = methodInfo.GetParameters ()

        if parameters.Length <> configArgs.Length then
            failwith
                $"%s{configType.FullName}.%s{methodInfo.Name} takes %d{parameters.Length} arguments, but AutoGenConfigArgs gives %d{configArgs.Length}."

        let genericParameters = methodInfo.GetGenericArguments ()
        let inferred : Type voption array = Array.create genericParameters.Length ValueNone

        let rec baseTypes (t : Type) = seq {
            match t.BaseType with
            | null -> ()
            | baseType ->
                yield baseType
                yield! baseTypes baseType
        }

        let rec unify (parameterType : Type) (argumentType : Type) =
            if parameterType.IsGenericParameter then
                let position = parameterType.GenericParameterPosition

                match inferred.[position] with
                | ValueNone -> inferred.[position] <- ValueSome argumentType
                | ValueSome earlier when Type.(=) (earlier, argumentType) -> ()
                | ValueSome earlier ->
                    failwith
                        $"The generic parameter %s{parameterType.Name} of %s{methodInfo.Name} gets both %s{earlier.FullName} and %s{argumentType.FullName} from AutoGenConfigArgs."
            elif parameterType.IsArray && argumentType.IsArray then
                // An array type has an element type
                unify (nonNull (parameterType.GetElementType ())) (nonNull (argumentType.GetElementType ()))
            elif
                parameterType.IsGenericType
                && parameterType.ContainsGenericParameters
            then
                // The argument's own type, a base type or an interface built from the same generic definition
                let definition = parameterType.GetGenericTypeDefinition ()

                seq {
                    yield argumentType
                    yield! baseTypes argumentType
                    yield! argumentType.GetInterfaces ()
                }
                |> Seq.tryFind (fun t ->
                    t.IsGenericType
                    && Type.(=) (t.GetGenericTypeDefinition (), definition)
                )
                |> ValueOption.iter (fun constructed ->
                    Array.iter2 unify (parameterType.GetGenericArguments ()) (constructed.GetGenericArguments ())
                )

        Array.iter2
            (fun (parameter : ParameterInfo) (configArg : objnull) ->
                // A null argument says nothing about the type of its parameter
                match configArg with
                | null -> ()
                | configArg -> unify parameter.ParameterType (configArg.GetType ())
            )
            parameters
            configArgs

        inferred
        |> Array.mapi (fun index inferredType ->
            match inferredType with
            | ValueSome t -> t
            | ValueNone ->
                failwith
                    $"The generic parameter %s{genericParameters.[index].Name} of %s{methodInfo.Name} cannot be inferred from AutoGenConfigArgs."
        )

    /// The arguments as they are given, or none for the null that a caller compiled without nullness checking can give
    let argsOrNone (configArgs : objnull array) : objnull array =
        match withNull configArgs with
        | null -> [||]
        | configArgs -> configArgs

    let instantiate (configType : Type) (configArgs : objnull array) : IAutoGenConfig =
        let methodInfo =
            match
                configType.GetMethods ()
                |> Seq.filter (fun p ->
                    p.IsStatic
                    && Type.(=) (p.ReturnType, KnownTypes.autoGenConfig)
                )
                |> Seq.tryExactlyOne
            with
            | ValueSome methodInfo -> methodInfo
            | ValueNone ->
                failwith
                    $"%s{configType.FullName} must have exactly one public static member that returns an AutoGenConfig.

An example type definition:

type %s{configType.Name} =
  static member __ =
    AutoGenConfig.defaults |> AutoGenConfig.addGenerator (Gen.constant 13)
"

        let methodInfo =
            if methodInfo.IsGenericMethod then
                methodInfo.MakeGenericMethod (inferGenericArguments configType methodInfo configArgs)
            else
                methodInfo

        match methodInfo.Invoke (null, configArgs) with
        | :? IAutoGenConfig as config -> config
        | _ -> failwith $"%s{configType.FullName}.%s{methodInfo.Name} returned no AutoGenConfig."
