namespace FSharp.Azure.Cosmos.Sql

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable

/// <summary>
/// An immutable array with structural equality: two instances are equal when they hold equal elements in the same
/// order, and the hash code combines the hash codes of the elements.
/// <para>
/// The own equality of <see cref="T:System.Collections.Immutable.ImmutableArray`1"/>, its
/// <see cref="M:System.Collections.Immutable.ImmutableArray`1.Equals(System.Collections.Immutable.ImmutableArray{`0})"/>
/// that <see cref="P:System.Collections.Generic.EqualityComparer`1.Default"/> calls, compares the reference of the array
/// it wraps. Only comparers that go through <see cref="T:System.Collections.IStructuralEquatable"/>, such as the equality
/// the F# compiler generates for records and unions, compare its elements;
/// <see cref="M:System.ValueTuple`2.Equals(System.ValueTuple{`0,`1})"/>, a
/// <see cref="T:System.Collections.Generic.HashSet`1"/> of arrays or a C# caller compares references, and a default
/// array never equals an empty one. The nodes of the syntax tree hold their child sequences in this wrapper instead
/// (the pattern Roslyn incremental generators use), so their equality is structural under every comparer, for
/// round-trip tests, cache keys and the idempotence of passes.
/// </para>
/// <para>
/// The default value holds no array; it behaves as an empty array and is equal to one.
/// </para>
/// </summary>
/// <param name="array">The elements, in order.</param>
[<Struct; CustomEquality; NoComparison>]
type EquatableArray<'T> (array : ImmutableArray<'T>) =

    /// <summary>
    /// The elements; never a default <see cref="T:System.Collections.Immutable.ImmutableArray`1"/>, so it can be
    /// enumerated and indexed without a check.
    /// </summary>
    member _.Items = if array.IsDefault then ImmutableArray<'T>.Empty else array

    /// The number of elements.
    member _.Length = if array.IsDefault then 0 else array.Length

    /// Whether the array has no elements.
    member this.IsEmpty = this.Length = 0

    /// <summary>
    /// The element at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based position of the element.</param>
    member this.Item
        with get (index : int) = this.Items[index]

    /// <summary>
    /// Returns an allocation-free enumerator over the elements, which F# <see langword="for"/> loops use.
    /// </summary>
    member this.GetEnumerator () = this.Items.GetEnumerator ()

    /// <summary>
    /// Whether <paramref name="other"/> holds equal elements in the same order; elements are compared with
    /// <see cref="P:System.Collections.Generic.EqualityComparer`1.Default"/>.
    /// </summary>
    /// <param name="other">The array to compare with.</param>
    member this.Equals (other : EquatableArray<'T>) =
        let left = this.Items
        let right = other.Items

        if left.Length <> right.Length then
            false
        else
            let comparer = EqualityComparer<'T>.Default
            let mutable equal = true
            let mutable index = 0

            while equal && index < left.Length do
                equal <- comparer.Equals (left[index], right[index])
                index <- index + 1

            equal

    /// <inheritdoc />
    override this.Equals (other : objnull) =
        match other with
        | :? EquatableArray<'T> as other -> this.Equals other
        | _ -> false

    /// Combines the hash codes of the elements, in order, so equal arrays have equal hash codes.
    override this.GetHashCode () =
        let mutable hash = HashCode ()

        for item in this.Items do
            hash.Add item

        hash.ToHashCode ()

    /// Shows the elements in square brackets, separated by semicolons, the way F# shows an array.
    override this.ToString () =
        let items = this.Items |> Seq.map (fun item -> $"%A{item}")
        $"""[| {String.Join ("; ", items)} |]"""

    interface IEquatable<EquatableArray<'T>> with
        /// <inheritdoc />
        member this.Equals other = this.Equals other

    interface IReadOnlyList<'T> with
        /// <inheritdoc />
        member this.Count = this.Length

        /// <inheritdoc />
        member this.Item
            with get index = this.Items[index]

        /// <inheritdoc />
        member this.GetEnumerator () : IEnumerator<'T> = (this.Items :> IEnumerable<'T>).GetEnumerator()

        /// <inheritdoc />
        member this.GetEnumerator () : IEnumerator = (this.Items :> IEnumerable).GetEnumerator()

/// <summary>
/// Functions that create and transform <see cref="T:FSharp.Azure.Cosmos.Sql.EquatableArray`1"/> values.
/// </summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module EquatableArray =

    /// The array without elements.
    [<GeneralizableValue>]
    let empty<'T> : EquatableArray<'T> = EquatableArray ImmutableArray<'T>.Empty

    /// <summary>
    /// Wraps <paramref name="items"/> without copying it.
    /// </summary>
    /// <param name="items">The elements.</param>
    let ofImmutableArray (items : ImmutableArray<'T>) = EquatableArray items

    /// <summary>
    /// Copies <paramref name="items"/> into a new array.
    /// </summary>
    /// <param name="items">The elements.</param>
    let ofArray (items : 'T array) = EquatableArray (ImmutableArray.Create<'T> items)

    /// <summary>
    /// Copies <paramref name="items"/> into a new array.
    /// </summary>
    /// <param name="items">The elements.</param>
    let ofSeq (items : 'T seq) = EquatableArray (ImmutableArray.CreateRange<'T> items)

    /// <summary>
    /// An array that holds <paramref name="item"/> only.
    /// </summary>
    /// <param name="item">The element.</param>
    let singleton (item : 'T) = EquatableArray (ImmutableArray.Create<'T> item)

    /// <summary>
    /// The elements of <paramref name="array"/> as an <see cref="T:System.Collections.Immutable.ImmutableArray`1"/>,
    /// without copying them.
    /// </summary>
    /// <param name="array">The array.</param>
    let toImmutableArray (array : EquatableArray<'T>) = array.Items

    /// <summary>
    /// Applies <paramref name="mapping"/> to every element of <paramref name="array"/>, in order.
    /// </summary>
    /// <param name="mapping">The function to apply.</param>
    /// <param name="array">The array.</param>
    let map (mapping : 'T -> 'U) (array : EquatableArray<'T>) =
        let items = array.Items
        let builder = ImmutableArray.CreateBuilder<'U> items.Length

        for item in items do
            builder.Add (mapping item)

        EquatableArray (builder.MoveToImmutable ())
