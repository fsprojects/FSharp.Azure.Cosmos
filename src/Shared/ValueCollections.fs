/// <summary>
/// The functions of <see cref="T:Microsoft.FSharp.Collections.SeqModule"/>,
/// <see cref="T:Microsoft.FSharp.Collections.ListModule"/> and <see cref="T:Microsoft.FSharp.Collections.ArrayModule"/>
/// that return an <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/> or reference tuples, shadowed by functions that
/// return a <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/> or struct tuples, so that looking an element up or
/// pairing two collections allocates no object per result.
/// </summary>
/// <remarks>
/// <para>
/// Every F# project under <c>src</c> and <c>tests</c> compiles this file first, through the root
/// <c>Directory.Build.props</c>, and gets its own internal copy. The module opens itself in the namespace
/// <c>FSharp.Azure.Cosmos</c>: the shadows are in effect in the code of that namespace and in every file that opens it.
/// A file in another namespace needs <c>open FSharp.Azure.Cosmos</c>, and so does one in a child namespace such as
/// <c>FSharp.Azure.Cosmos.Tests</c>, because a child namespace does not open the modules of its parent.
/// </para>
/// <para>
/// A shadow means what the FSharp.Core function of the same name means, and that function stays reachable by its full
/// name: <see cref="M:Microsoft.FSharp.Collections.SeqModule.TryHead``1(System.Collections.Generic.IEnumerable{``0})"/>
/// is written <c>Microsoft.FSharp.Collections.Seq.tryHead</c>. Functions that only FSharp.Core has, such as
/// <see cref="M:Microsoft.FSharp.Collections.SeqModule.Map``2(Microsoft.FSharp.Core.FSharpFunc{``0,``1},System.Collections.Generic.IEnumerable{``0})"/>,
/// resolve to FSharp.Core as before. A function that is missing here is added here, not converted at its call site.
/// </para>
/// </remarks>
[<AutoOpen>]
module internal FSharp.Azure.Cosmos.ValueCollections

open System.Linq

[<RequireQualifiedAccess>]
module Array =

    /// The first element, or no value when the array is empty.
    let tryHead (array : 'T array) : 'T voption = if array.Length = 0 then ValueNone else ValueSome array[0]

    /// The last element, or no value when the array is empty.
    let tryLast (array : 'T array) : 'T voption =
        if array.Length = 0 then
            ValueNone
        else
            ValueSome array[array.Length - 1]

    /// The element at the index, or no value when the index is outside the array.
    let tryItem (index : int) (array : 'T array) : 'T voption =
        if index < 0 || index >= array.Length then
            ValueNone
        else
            ValueSome array[index]

    /// The only element, or no value when the array is empty or has more than one element.
    let tryExactlyOne (array : 'T array) : 'T voption = if array.Length = 1 then ValueSome array[0] else ValueNone

    /// The index of the first element for which the predicate holds, or no value when it holds for none.
    let tryFindIndex (predicate : 'T -> bool) (array : 'T array) : int voption =
        let mutable index = 0
        let mutable found = ValueNone

        while found.IsNone && index < array.Length do
            if predicate array[index] then
                found <- ValueSome index

            index <- index + 1

        found

    /// The index of the last element for which the predicate holds, or no value when it holds for none.
    let tryFindIndexBack (predicate : 'T -> bool) (array : 'T array) : int voption =
        let mutable index = array.Length - 1
        let mutable found = ValueNone

        while found.IsNone && index >= 0 do
            if predicate array[index] then
                found <- ValueSome index

            index <- index - 1

        found

    /// The first element for which the predicate holds, or no value when it holds for none.
    let tryFind (predicate : 'T -> bool) (array : 'T array) : 'T voption =
        match tryFindIndex predicate array with
        | ValueSome index -> ValueSome array[index]
        | ValueNone -> ValueNone

    /// The last element for which the predicate holds, or no value when it holds for none.
    let tryFindBack (predicate : 'T -> bool) (array : 'T array) : 'T voption =
        match tryFindIndexBack predicate array with
        | ValueSome index -> ValueSome array[index]
        | ValueNone -> ValueNone

    /// The first value that the chooser gives for an element, or no value when it gives none.
    let tryPick (chooser : 'T -> 'U voption) (array : 'T array) : 'U voption =
        let mutable index = 0
        let mutable picked = ValueNone

        while picked.IsNone && index < array.Length do
            picked <- chooser array[index]
            index <- index + 1

        picked

    /// The elements of two arrays of the same length, paired by index; arrays of different lengths are an error, as for
    /// the function of FSharp.Core.
    let zip (first : 'T1 array) (second : 'T2 array) : struct ('T1 * 'T2) array =
        Array.map2 (fun left right -> struct (left, right)) first second

[<RequireQualifiedAccess>]
module List =

    /// The first element, or no value when the list is empty.
    let tryHead (list : 'T list) : 'T voption =
        match list with
        | head :: _ -> ValueSome head
        | [] -> ValueNone

    /// The last element, or no value when the list is empty.
    let rec tryLast (list : 'T list) : 'T voption =
        match list with
        | [] -> ValueNone
        | [ last ] -> ValueSome last
        | _ :: tail -> tryLast tail

    /// The element at the index, or no value when the index is outside the list.
    let rec tryItem (index : int) (list : 'T list) : 'T voption =
        match list with
        | head :: _ when index = 0 -> ValueSome head
        | _ :: tail when index > 0 -> tryItem (index - 1) tail
        | _ -> ValueNone

    /// The only element, or no value when the list is empty or has more than one element.
    let tryExactlyOne (list : 'T list) : 'T voption =
        match list with
        | [ only ] -> ValueSome only
        | _ -> ValueNone

    /// The first element for which the predicate holds, or no value when it holds for none.
    let rec tryFind (predicate : 'T -> bool) (list : 'T list) : 'T voption =
        match list with
        | [] -> ValueNone
        | head :: _ when predicate head -> ValueSome head
        | _ :: tail -> tryFind predicate tail

    /// The last element for which the predicate holds, or no value when it holds for none.
    let tryFindBack (predicate : 'T -> bool) (list : 'T list) : 'T voption = list |> List.toArray |> Array.tryFindBack predicate

    /// The index of the first element for which the predicate holds, or no value when it holds for none.
    let tryFindIndex (predicate : 'T -> bool) (list : 'T list) : int voption =
        let rec search index remaining =
            match remaining with
            | [] -> ValueNone
            | head :: _ when predicate head -> ValueSome index
            | _ :: tail -> search (index + 1) tail

        search 0 list

    /// The index of the last element for which the predicate holds, or no value when it holds for none.
    let tryFindIndexBack (predicate : 'T -> bool) (list : 'T list) : int voption =
        list |> List.toArray |> Array.tryFindIndexBack predicate

    /// The first value that the chooser gives for an element, or no value when it gives none.
    let rec tryPick (chooser : 'T -> 'U voption) (list : 'T list) : 'U voption =
        match list with
        | [] -> ValueNone
        | head :: tail ->
            match chooser head with
            | ValueSome _ as picked -> picked
            | ValueNone -> tryPick chooser tail

    /// The elements of two lists of the same length, paired by position; lists of different lengths are an error, as for
    /// the function of FSharp.Core.
    let zip (first : 'T1 list) (second : 'T2 list) : struct ('T1 * 'T2) list =
        List.map2 (fun left right -> struct (left, right)) first second

[<RequireQualifiedAccess>]
module Seq =

    /// The first element, or no value when the sequence is empty.
    let tryHead (source : 'T seq) : 'T voption =
        use enumerator = source.GetEnumerator ()

        if enumerator.MoveNext () then
            ValueSome enumerator.Current
        else
            ValueNone

    /// The last element, or no value when the sequence is empty.
    let tryLast (source : 'T seq) : 'T voption =
        use enumerator = source.GetEnumerator ()

        if enumerator.MoveNext () then
            let mutable last = enumerator.Current

            while enumerator.MoveNext () do
                last <- enumerator.Current

            ValueSome last
        else
            ValueNone

    /// The element at the index, or no value when the index is negative or the sequence ends before it.
    let tryItem (index : int) (source : 'T seq) : 'T voption =
        if index < 0 then
            ValueNone
        else
            use enumerator = source.GetEnumerator ()
            let mutable remaining = index
            let mutable reached = enumerator.MoveNext ()

            while reached && remaining > 0 do
                remaining <- remaining - 1
                reached <- enumerator.MoveNext ()

            if reached then ValueSome enumerator.Current else ValueNone

    /// The only element, or no value when the sequence is empty or has more than one element.
    let tryExactlyOne (source : 'T seq) : 'T voption =
        use enumerator = source.GetEnumerator ()

        if enumerator.MoveNext () then
            let only = enumerator.Current
            if enumerator.MoveNext () then ValueNone else ValueSome only
        else
            ValueNone

    /// The first element for which the predicate holds, or no value when it holds for none.
    let tryFind (predicate : 'T -> bool) (source : 'T seq) : 'T voption =
        use enumerator = source.GetEnumerator ()
        let mutable found = ValueNone

        while found.IsNone && enumerator.MoveNext () do
            let item = enumerator.Current

            if predicate item then
                found <- ValueSome item

        found

    /// The last element for which the predicate holds, or no value when it holds for none.
    let tryFindBack (predicate : 'T -> bool) (source : 'T seq) : 'T voption = source |> Seq.toArray |> Array.tryFindBack predicate

    /// The index of the first element for which the predicate holds, or no value when it holds for none.
    let tryFindIndex (predicate : 'T -> bool) (source : 'T seq) : int voption =
        use enumerator = source.GetEnumerator ()
        let mutable index = 0
        let mutable found = ValueNone

        while found.IsNone && enumerator.MoveNext () do
            if predicate enumerator.Current then
                found <- ValueSome index

            index <- index + 1

        found

    /// The index of the last element for which the predicate holds, or no value when it holds for none.
    let tryFindIndexBack (predicate : 'T -> bool) (source : 'T seq) : int voption =
        source |> Seq.toArray |> Array.tryFindIndexBack predicate

    /// The first value that the chooser gives for an element, or no value when it gives none.
    let tryPick (chooser : 'T -> 'U voption) (source : 'T seq) : 'U voption =
        use enumerator = source.GetEnumerator ()
        let mutable picked = ValueNone

        while picked.IsNone && enumerator.MoveNext () do
            picked <- chooser enumerator.Current

        picked

    /// The elements of two sequences, paired by position until the shorter one ends, as for the function of FSharp.Core.
    let zip (first : 'T1 seq) (second : 'T2 seq) : struct ('T1 * 'T2) seq = Enumerable.Zip (first, second)
