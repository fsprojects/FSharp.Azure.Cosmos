/// <summary>
/// The types that the adapter compares other types with, each resolved once.
/// </summary>
/// <remarks>
/// <see cref="M:Microsoft.FSharp.Core.Operators.TypeDefOf``1"/> builds its type on every use, through
/// <see cref="M:System.Type.GetGenericTypeDefinition"/>. The types are compared through the equality operator of
/// <see cref="T:System.Type"/>, never through generic equality.
/// </remarks>
module internal Hedgehog.MSTest.KnownTypes

open System
open System.Threading.Tasks
open Hedgehog

/// <summary>
/// <see cref="T:System.Void"/>, the return type of a method that returns <see cref="T:Microsoft.FSharp.Core.Unit"/>.
/// </summary>
let voidType = typeof<Void>

/// <summary>
/// <see cref="T:System.Threading.Tasks.Task"/>.
/// </summary>
let task = typeof<Task>

/// <summary>
/// <see cref="T:System.Threading.Tasks.ValueTask"/>.
/// </summary>
let valueTask = typeof<ValueTask>

/// <summary>
/// <see cref="T:System.Threading.Tasks.ValueTask`1"/> without its type argument.
/// </summary>
let valueTaskDefinition = typedefof<ValueTask<_>>

/// <summary>
/// <see cref="T:Microsoft.FSharp.Control.FSharpAsync`1"/> without its type argument.
/// </summary>
let asyncDefinition = typedefof<Async<_>>

/// <summary>
/// <see cref="T:Microsoft.FSharp.Core.FSharpResult`2"/> without its type arguments.
/// </summary>
let resultDefinition = typedefof<Result<_, _>>

/// <summary>
/// <see cref="T:Hedgehog.IAutoGenConfig"/>.
/// </summary>
let autoGenConfig = typeof<IAutoGenConfig>
