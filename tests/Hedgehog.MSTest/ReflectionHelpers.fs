// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/ReflectionHelpers.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/ReflectionHelpers.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the method invocation helpers are removed, because MSTest invokes the test method and awaits its result;
// the type checks remain and name the return types that MSTest cannot run; the types they compare with come from
// KnownTypes and are compared through Type.(=).
// The file is formatted with Fantomas, under the settings of this repository.

/// Reflection utilities for type checking
module internal ReflectionHelpers

open System
open Hedgehog.MSTest

// ========================================
// Type Checking
// ========================================

/// Whether the type is the generic type of the definition with any type arguments
let private isConstructedFrom (definition : Type) (t : Type) =
    t.IsGenericType
    && Type.(=) (t.GetGenericTypeDefinition (), definition)

let isGenericTask (t : Type) = t.IsGenericType && KnownTypes.task.IsAssignableFrom (t)

let isGenericValueTask (t : Type) = isConstructedFrom KnownTypes.valueTaskDefinition t

let isAsync (t : Type) = isConstructedFrom KnownTypes.asyncDefinition t

let isResult (t : Type) = isConstructedFrom KnownTypes.resultDefinition t
