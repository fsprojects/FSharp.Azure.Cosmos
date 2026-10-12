// What a layout says, compared with a document that the serializer wrote: the functions that the tests of the JSON
// layout share. The serializer is the oracle in all of them, so each gives the disagreements that it finds as texts
// for a failure message, and none when the layout is right.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open FSharp.Reflection

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations

/// The steps that a layout gives from one JSON value to another one inside it.
[<RequireQualifiedAccess>]
module WrittenSteps =

    /// The steps of a path as a query would write them, for a failure message.
    let shown (path : ImmutableArray<JsonStep>) : string =
        path
        |> Seq.map (fun step ->
            match step with
            | JsonStep.Property name -> $"[\"%s{name}\"]"
            | JsonStep.Item index -> $"[%d{index}]"
        )
        |> String.concat ""

    /// The JSON value at the end of the steps that start at a value; none when a step leads nowhere.
    let valueAt (path : ImmutableArray<JsonStep>) (start : JsonElement) : JsonElement voption =
        path
        |> Seq.fold
            (fun (reached : JsonElement voption) (step : JsonStep) ->
                reached
                |> ValueOption.bind (fun value ->
                    match step with
                    | JsonStep.Property name when value.ValueKind = JsonValueKind.Object ->
                        match value.TryGetProperty name with
                        | true, found -> ValueSome found
                        | false, _ -> ValueNone
                    | JsonStep.Item index when
                        value.ValueKind = JsonValueKind.Array
                        && index < value.GetArrayLength ()
                        ->
                        ValueSome value[index]
                    | _ -> ValueNone
                )
            )
            (ValueSome start)

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says of a value that is no object, compared with
/// the JSON value that the serializer wrote for it.
/// </summary>
[<RequireQualifiedAccess>]
module WrittenValues =

    /// The type arguments of the first of the generic types that a type is or implements.
    let private argumentsOf (definitions : Type seq) (valueType : Type) : Type array voption =
        seq {
            valueType
            yield! valueType.GetInterfaces ()
        }
        |> Seq.tryPick (fun candidate ->
            if
                candidate.IsGenericType
                && definitions
                   |> Seq.exists (fun definition -> Type.(=) (candidate.GetGenericTypeDefinition (), definition))
            then
                ValueSome (candidate.GetGenericArguments ())
            else
                ValueNone
        )

    /// The types of the keys and values of a map or a dictionary; no value for any other type.
    let private keyAndValueOf (valueType : Type) : struct (Type * Type) voption =
        argumentsOf
            (seq {
                typedefof<Map<_, _>>
                typedefof<IDictionary<_, _>>
                typedefof<IReadOnlyDictionary<_, _>>
            })
            valueType
        |> ValueOption.map (fun arguments -> struct (arguments[0], arguments[1]))

    /// The type of the items of a collection; no value for a text and for a type that is no collection.
    let private elementOf (valueType : Type) : Type voption =
        if Type.(=) (valueType, typeof<string>) then
            ValueNone
        else
            argumentsOf (Seq.singleton typedefof<IEnumerable<_>>) valueType
            |> ValueOption.map (fun arguments -> arguments[0])

    /// Whether a type is an enumeration or a nullable of one.
    let private isEnumeration (valueType : Type) : bool =
        match Nullable.GetUnderlyingType valueType with
        | null -> valueType.IsEnum
        | underlying -> underlying.IsEnum

    /// The names that the layout has for the members of a type, in the order of the names.
    let namesOf (layout : IJsonLayout) (holderType : Type) : string array =
        seq {
            for dataMember in ModelMembers.ofType holderType do
                match layout.MemberName { Holder = holderType; Member = dataMember } with
                | ValueSome name -> name
                | ValueNone -> ()
        }
        |> Seq.sort
        |> Seq.toArray

    /// The names of the properties of a JSON object, in the order of the names.
    let propertiesOf (written : JsonElement) : string array =
        written.EnumerateObject ()
        |> Seq.map _.Name
        |> Seq.sort
        |> Seq.toArray

    /// <summary>
    /// Where what the layout says of a value differs from what the serializer wrote for it, as texts for a failure
    /// message. The kind of the written JSON value says what the layout should say: a number or a string for an
    /// enumeration, an array or an object for a tuple, an array for a collection, an object or an array for a map.
    /// A value that was not written has no kind, and the layout should not describe it.
    /// </summary>
    let disagreementsAbout
        (layout : IJsonLayout)
        (named : string)
        (memberOfDocument : DocumentMember voption)
        (valueType : Type)
        (written : JsonElement)
        : string seq = seq {
        if isEnumeration valueType then
            let expected =
                match written.ValueKind with
                | JsonValueKind.Number -> EnumEncoding.Number
                | JsonValueKind.String -> EnumEncoding.String
                | _ -> EnumEncoding.NotDescribed

            let said = layout.EnumEncoding (memberOfDocument, valueType)

            if said <> expected then
                $"%s{named}: the layout says %A{said} of the enumeration, the document %A{expected}"
        elif FSharpType.IsTuple valueType then
            let expected =
                match written.ValueKind with
                | JsonValueKind.Array -> TupleEncoding.Array
                | JsonValueKind.Object -> TupleEncoding.Object
                | _ -> TupleEncoding.NotDescribed

            let said = layout.TupleEncoding (memberOfDocument, valueType)

            if said <> expected then
                $"%s{named}: the layout says %A{said} of the tuple, the document %A{expected}"
            elif expected.IsObject then
                // The properties of the object are the members of the tuple that the layout names.
                let writtenNames = propertiesOf written
                let saidNames = namesOf layout valueType

                if saidNames <> writtenNames then
                    $"%s{named}: the layout names the items of the tuple %A{saidNames}, the document %A{writtenNames}"
        else
            match keyAndValueOf valueType, elementOf valueType with
            | ValueSome (struct (key, value)), _ ->
                let expected =
                    match written.ValueKind with
                    | JsonValueKind.Object -> MapEncoding.Object (key, value)
                    | JsonValueKind.Array -> MapEncoding.ArrayOfPairs (key, value)
                    | _ -> MapEncoding.NotDescribed

                let said = layout.MapEncoding (memberOfDocument, valueType)

                if said <> expected then
                    $"%s{named}: the layout says %A{said} of the map, the document %A{expected}"
            | ValueNone, ValueSome element ->
                let expected =
                    match written.ValueKind with
                    | JsonValueKind.Array -> CollectionEncoding.Array element
                    | _ -> CollectionEncoding.NotDescribed

                let said = layout.CollectionEncoding (memberOfDocument, valueType)

                if said <> expected then
                    $"%s{named}: the layout says %A{said} of the collection, the document %A{expected}"
            | ValueNone, ValueNone -> ()
    }

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says of a union, compared with the JSON value that
/// the serializer wrote for a value of one of its cases.
/// </summary>
[<RequireQualifiedAccess>]
module WrittenUnions =

    /// Whether a JSON value is a tag.
    let private isTag (tag : UnionTag) (value : JsonElement) : bool =
        match tag, value.ValueKind with
        | UnionTag.String text, JsonValueKind.String -> String.Equals (value.GetString (), text, StringComparison.Ordinal)
        | UnionTag.Number number, JsonValueKind.Number -> value.GetInt32 () = number
        | UnionTag.Boolean true, JsonValueKind.True
        | UnionTag.Boolean false, JsonValueKind.False -> true
        | _ -> false

    /// Whether a written value has a mark; no answer for the mark that nothing is written for.
    let private hasMark (mark : UnionCaseMark) (written : JsonElement) : bool voption =
        match mark with
        | UnionCaseMark.Tag (path, tag) ->
            ValueSome (
                WrittenSteps.valueAt path written
                |> ValueOption.exists (isTag tag)
            )
        | UnionCaseMark.Property name ->
            let isHeld =
                written.ValueKind = JsonValueKind.Object
                && (
                    match written.TryGetProperty name with
                    | found, _ -> found
                )

            ValueSome isHeld
        | UnionCaseMark.Null -> ValueSome (written.ValueKind = JsonValueKind.Null)
        | UnionCaseMark.Unmarked -> ValueNone

    /// The case and the values of the fields of a value of a union.
    let caseOf (unionType : Type) (value : objnull) : struct (UnionCaseInfo * objnull array) =
        let case, values = FSharpValue.GetUnionFields (value, unionType, true)
        struct (case, values)

    /// <summary>
    /// Where what the layout says of the case of a value differs from what the serializer wrote for the value, as
    /// texts for a failure message. The mark of the case has to be in the document and the mark of no other case; the
    /// value of every field has to be at its place, written as the serializer writes a value of its type. A field
    /// without a value that the serializer left out is not looked for.
    /// </summary>
    let disagreementsAbout
        (layout : IJsonLayout)
        (options : JsonSerializerOptions)
        (memberOfDocument : DocumentMember voption)
        (unionType : Type)
        (value : objnull)
        (written : JsonElement)
        : string seq = seq {
        let struct (case, values) = caseOf unionType value
        let named = $"%s{unionType.Name}.%s{case.Name}, written as %s{written.GetRawText ()}"

        match layout.UnionCaseEncoding (memberOfDocument, case) with
        | UnionCaseEncoding.NotDescribed -> $"%s{named}: the layout does not describe the case"
        | UnionCaseEncoding.Written (mark, places) ->
            if hasMark mark written = ValueSome false then
                $"%s{named}: the mark %A{mark} is not in the document"

            for other in FSharpType.GetUnionCases (unionType, true) do
                if other.Tag <> case.Tag then
                    match layout.UnionCaseEncoding (memberOfDocument, other) with
                    | UnionCaseEncoding.Written (otherMark, _) when hasMark otherMark written = ValueSome true ->
                        $"%s{named}: the mark of the case %s{other.Name} is in the document too"
                    | _ -> ()

            let fields = case.GetFields ()

            if places.Length <> fields.Length then
                $"%s{named}: the layout has %d{places.Length} places for %d{fields.Length} fields"
            else
                for index in 0 .. fields.Length - 1 do
                    let field = fields[index]
                    let expected = JsonSerializer.SerializeToElement (values[index], field.PropertyType, options)

                    match places[index] with
                    | UnionFieldPlace.Value path ->
                        match WrittenSteps.valueAt path written with
                        | ValueSome found when JsonElement.DeepEquals (found, expected) -> ()
                        // A field without a value can be left out, as a field of a record can.
                        | ValueNone when expected.ValueKind = JsonValueKind.Null -> ()
                        | _ -> $"%s{named}: the field %s{field.Name} is not at %s{WrittenSteps.shown path}"
                    | UnionFieldPlace.Members path ->
                        match WrittenSteps.valueAt path written with
                        | ValueSome holder when holder.ValueKind = JsonValueKind.Object ->
                            for dataMember in FSharpType.GetRecordFields (field.PropertyType, true) do
                                let ofRecord = { Holder = field.PropertyType; Member = dataMember }

                                let expected =
                                    JsonSerializer.SerializeToElement (
                                        dataMember.GetValue values[index],
                                        dataMember.PropertyType,
                                        options
                                    )

                                match layout.MemberName ofRecord with
                                | ValueSome name ->
                                    match holder.TryGetProperty name with
                                    | true, found when JsonElement.DeepEquals (found, expected) -> ()
                                    | _ ->
                                        $"%s{named}: the member %s{dataMember.Name} of the record is not at %s{WrittenSteps.shown path}[\"%s{name}\"]"
                                | ValueNone ->
                                    $"%s{named}: the layout has no name for the member %s{dataMember.Name} of the record"
                        | _ -> $"%s{named}: no object is at %s{WrittenSteps.shown path} for the members of the record"
    }

    /// What the serializer writes for a value; none when it refuses the type.
    let written (options : JsonSerializerOptions) (valueType : Type) (value : objnull) : JsonElement voption =
        try
            ValueSome (JsonSerializer.SerializeToElement (value, valueType, options))
        with :? NotSupportedException ->
            ValueNone

    /// <summary>
    /// What the serializer writes for each of some values of one type; none when it refuses one of them. System.Text.Json
    /// refuses a union when it is to write a value of it, and writes a null without asking what it is a null of, so
    /// the union with a case that is null is refused as the other unions are.
    /// </summary>
    let writtenAll
        (options : JsonSerializerOptions)
        (valueType : Type)
        (values : objnull seq)
        : struct (objnull * JsonElement) array voption =
        let documents =
            values
            |> Seq.map (fun value -> struct (value, written options valueType value))
            |> Seq.toArray

        if
            documents
            |> Array.forall (fun (struct (_, document)) -> document.IsSome)
        then
            documents
            |> Array.map (fun (struct (value, document)) -> struct (value, document.Value))
            |> ValueSome
        else
            ValueNone

    /// A union case by its name.
    let caseNamed (unionType : Type) (name : string) : UnionCaseInfo =
        FSharpType.GetUnionCases (unionType, true)
        |> Array.find (fun case -> String.Equals (case.Name, name, StringComparison.Ordinal))
