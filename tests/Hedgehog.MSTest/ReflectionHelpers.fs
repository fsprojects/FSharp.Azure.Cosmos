// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/ReflectionHelpers.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/ReflectionHelpers.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: none.

/// Reflection utilities for type checking and method invocation
module internal ReflectionHelpers

open System
open System.Reflection
open System.Threading
open System.Threading.Tasks

// ========================================
// Type Checking
// ========================================

let isGenericTask (t: Type) =
    t.IsGenericType && typeof<Task>.IsAssignableFrom(t)

let isGenericValueTask (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<ValueTask<_>>

let isAsync (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Async<_>>

let isResult (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Result<_, _>>

// ========================================
// Method Invocation
// ========================================

let invokeAwaitTask (taskObj: obj) =
    let taskType = taskObj.GetType()

    let awaitTaskMethod =
        typeof<Async>.GetMethods()
        |> Array.find (fun m -> m.Name = "AwaitTask" && m.IsGenericMethod)

    awaitTaskMethod.MakeGenericMethod(taskType.GetGenericArguments().[0]).Invoke(null, [| taskObj |])

let assertResultOk (resultObj: obj) (markerType: Type) (resultIsOkMethodName: string) =
    markerType
        .GetTypeInfo()
        .GetDeclaredMethod(resultIsOkMethodName)
        .MakeGenericMethod(resultObj.GetType().GetGenericArguments())
        .Invoke(null, [| resultObj |])
