// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/ReflectionHelpers.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/ReflectionHelpers.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the method invocation helpers are removed, because MSTest invokes the test method and awaits its result;
// the type checks remain and name the return types that MSTest cannot run.

/// Reflection utilities for type checking
module internal ReflectionHelpers

open System
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
