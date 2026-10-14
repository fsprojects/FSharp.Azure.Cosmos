/// <summary>
/// Struct partial active patterns that match an <see cref="T:System.Collections.Immutable.ImmutableArray`1"/> by its
/// length, so code over the children of a syntax node reads like a list pattern without converting to an F# list.
/// <para>
/// Each pattern returns a value option made a struct by <see cref="T:Microsoft.FSharp.Core.StructAttribute"/> on its
/// return value, or a <see cref="T:System.Boolean"/>, and its payload is a struct tuple, so a match allocates nothing.
/// A multi-case active pattern over the lengths would return a heap
/// <see cref="T:Microsoft.FSharp.Core.FSharpChoice`3"/> on every match, which is why every length is its own partial
/// pattern and lengths a pattern does not cover fall through to the next case.
/// </para>
/// </summary>
/// <example>
/// <code lang="fsharp">
/// match arguments with
/// | Arr0 -> "no argument"
/// | Arr1 single -> "one argument"
/// | Arr2 (first, second) -> "two arguments"
/// | Arr3 (first, second, third) -> "three arguments"
/// | ArrN 4 -> "four arguments"
/// | _ -> "more arguments"
/// </code>
/// </example>
[<AutoOpen>]
module FSharp.Azure.Cosmos.Sql.ImmutableArrayPatterns

open System.Collections.Immutable

/// <summary>
/// Matches an array without elements. A default (uninitialized) array counts as empty.
/// </summary>
/// <param name="items">The array to match.</param>
let (|Arr0|_|) (items : ImmutableArray<'T>) = items.IsDefaultOrEmpty

/// <summary>
/// Matches an array of exactly one element and returns that element.
/// </summary>
/// <param name="items">The array to match.</param>
[<return : Struct>]
let (|Arr1|_|) (items : ImmutableArray<'T>) =
    if not items.IsDefault && items.Length = 1 then
        ValueSome items[0]
    else
        ValueNone

/// <summary>
/// Matches an array of exactly two elements and returns them as a struct tuple, which the pattern binds with or
/// without the <see langword="struct"/> keyword.
/// </summary>
/// <param name="items">The array to match.</param>
[<return : Struct>]
let (|Arr2|_|) (items : ImmutableArray<'T>) =
    if not items.IsDefault && items.Length = 2 then
        ValueSome (struct (items[0], items[1]))
    else
        ValueNone

/// <summary>
/// Matches an array of exactly three elements and returns them as a struct tuple.
/// </summary>
/// <param name="items">The array to match.</param>
[<return : Struct>]
let (|Arr3|_|) (items : ImmutableArray<'T>) =
    if not items.IsDefault && items.Length = 3 then
        ValueSome (struct (items[0], items[1], items[2]))
    else
        ValueNone

/// <summary>
/// Matches an array of exactly <paramref name="length"/> elements, for the lengths that have no pattern of their own;
/// index the array to read its elements.
/// </summary>
/// <param name="length">The number of elements to match, written as the argument of the pattern.</param>
/// <param name="items">The array to match.</param>
let (|ArrN|_|) (length : int) (items : ImmutableArray<'T>) = (if items.IsDefault then 0 else items.Length) = length
