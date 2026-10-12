namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Reflection.Emit
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.Patterns
open FSharp.Reflection

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// A way to set up the serializer, as a function that makes its options anew: a test may change what it gets.
[<Struct>]
type SerializerProfile = {
    /// What is special about the options; the name of a test result holds it.
    Name : string
    /// Makes the options.
    Create : unit -> JsonSerializerOptions
    /// Whether the options hold the converter of FSharp.SystemTextJson, without which no skippable value is written.
    HasFSharpConverter : bool
}

/// The ways to set up the serializer that the tests of the layout write the model with.
module SerializerProfiles =

    /// Options of System.Text.Json alone.
    let private plain (name : string) (configure : JsonSerializerOptions -> unit) : SerializerProfile = {
        Name = name
        Create =
            fun () ->
                let options = JsonSerializerOptions ()
                configure options
                options
        HasFSharpConverter = false
    }

    /// Options with the converter of FSharp.SystemTextJson.
    let private fsharp
        (name : string)
        (configureConverter : JsonFSharpOptions -> JsonFSharpOptions)
        (configure : JsonSerializerOptions -> unit)
        : SerializerProfile = {
        Name = name
        Create =
            fun () ->
                let options = (configureConverter (JsonFSharpOptions.Default ())).ToJsonSerializerOptions()
                configure options
                options
        HasFSharpConverter = true
    }

    // JsonSerializerOptions.IgnoreNullValues is obsolete, and the serializers still follow it: two profiles are about it.
    #nowarn "44"

    /// Sets the setting of old that ignores null.
    let private ignoreNullValues (options : JsonSerializerOptions) : unit = options.IgnoreNullValues <- true

    #warnon "44"

    /// System.Text.Json alone, as it is made.
    let systemTextJson = plain "System.Text.Json with its defaults" ignore

    /// FSharp.SystemTextJson as it is made.
    let fsharpSystemTextJson = fsharp "FSharp.SystemTextJson with its defaults" id ignore

    /// FSharp.SystemTextJson writing an option as a union with the name of its case.
    let withoutUnwrappedOptions =
        fsharp
            "FSharp.SystemTextJson that does not unwrap options"
            _.WithUnionEncoding(
                JsonUnionEncoding.AdjacentTag
                ||| JsonUnionEncoding.UnwrapSingleCaseUnions
            )
            ignore

    /// Every profile.
    let all =
        seq {
            systemTextJson
            plain
                "System.Text.Json with camel case names"
                (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase)
            plain
                "System.Text.Json with snake case names that ignores null"
                (fun options ->
                    options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
                    options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
                )
            plain "System.Text.Json that ignores null with the setting of old" ignoreNullValues
            plain
                "System.Text.Json with a converter that writes null itself"
                (fun options -> options.Converters.Add (StoredNullAsDashConverter ()))
            plain
                "System.Text.Json that ignores default values"
                (fun options -> options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingDefault)
            plain "System.Text.Json that includes fields" (fun options -> options.IncludeFields <- true)
            plain "System.Text.Json that ignores read-only properties" (fun options -> options.IgnoreReadOnlyProperties <- true)
            plain
                "System.Text.Json that includes fields and ignores read-only ones"
                (fun options ->
                    options.IncludeFields <- true
                    options.IgnoreReadOnlyFields <- true
                )

            fsharpSystemTextJson
            fsharp
                "FSharp.SystemTextJson with camel case names that ignores null"
                id
                (fun options ->
                    options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
                    options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
                )
            fsharp "FSharp.SystemTextJson that ignores null with the setting of old" id ignoreNullValues
            fsharp
                "FSharp.SystemTextJson with a converter that writes null itself"
                id
                (fun options -> options.Converters.Insert (0, StoredNullAsDashConverter ()))
            fsharp
                "FSharp.SystemTextJson that ignores default values"
                id
                (fun options -> options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingDefault)
            fsharp
                "FSharp.SystemTextJson that always skips an option without a value"
                _.WithSkippableOptionFields(SkippableOptionFields.Always)
                ignore
            fsharp
                "FSharp.SystemTextJson that never skips an option and ignores null"
                _.WithSkippableOptionFields(SkippableOptionFields.Never)
                (fun options -> options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull)
            withoutUnwrappedOptions
            fsharp
                "FSharp.SystemTextJson without its converters of options"
                _.WithTypes(JsonFSharpTypes.All &&& ~~~JsonFSharpTypes.OptionalTypes)
                ignore
            fsharp
                "FSharp.SystemTextJson without its converter of records"
                _.WithTypes(JsonFSharpTypes.All &&& ~~~JsonFSharpTypes.Records)
                (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase)
            fsharp "FSharp.SystemTextJson that includes the properties of records" _.WithIncludeRecordProperties() ignore
            fsharp
                "FSharp.SystemTextJson with other options for one record type"
                (fun converterOptions ->
                    converterOptions
                        .WithSkippableOptionFields(SkippableOptionFields.Always)
                        .WithOverrides(fun common ->
                            let overrides = Dictionary<Type, JsonFSharpOptions>()
                            overrides.Add (typeof<StoredEntry>, common.WithSkippableOptionFields SkippableOptionFields.Never)
                            // For every type that is made of the generic record.
                            overrides.Add (
                                typedefof<StoredBox<_>>,
                                common.WithSkippableOptionFields SkippableOptionFields.Never
                            )
                            overrides :> IDictionary<Type, JsonFSharpOptions>
                        )
                )
                ignore
            fsharp "FSharp.SystemTextJson that lets an attribute give the options of a type" _.WithAllowOverride() ignore
            fsharp
                "FSharp.SystemTextJson that ignores read-only properties"
                id
                (fun options -> options.IgnoreReadOnlyProperties <- true)
            {
                Name = "FSharp.SystemTextJson with one converter for unions and another one for the other types"
                Create =
                    fun () ->
                        let options = JsonSerializerOptions ()

                        // The first converter takes no record, so the second one writes the records, with its options.
                        options.Converters.Add (
                            JsonFSharpConverter (JsonFSharpOptions.Default().WithTypes JsonFSharpTypes.Unions)
                        )

                        options.Converters.Add (
                            JsonFSharpConverter (
                                JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always
                            )
                        )

                        options
                HasFSharpConverter = true
            }
            fsharp
                "FSharp.SystemTextJson with internal tags, options that are always skipped and snake case names"
                (fun _ ->
                    JsonFSharpOptions(
                        JsonUnionEncoding.InternalTag
                        ||| JsonUnionEncoding.AllowUnorderedTag
                        ||| JsonUnionEncoding.NamedFields
                        ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                        ||| JsonUnionEncoding.UnwrapRecordCases
                        ||| JsonUnionEncoding.UnwrapOption
                        ||| JsonUnionEncoding.UnwrapFieldlessTags,
                        unionTagName = "$type"
                    )
                        .WithSkippableOptionFields(SkippableOptionFields.Always, true)
                        .WithMapFormat(MapFormat.Object)
                )
                (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower)
        }
        |> ImmutableArray.CreateRange

/// <summary>
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> of
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.JsonLayout.ofSerializerOptions(System.Text.Json.JsonSerializerOptions)"/>:
/// what the layout says about a member is what the serializer writes for it. The serializer is the oracle: every type
/// of the model is written with every way to set the serializer up, and the document is searched for each member.
/// </summary>
[<TestClass; UnitTestCategory; JsonLayoutTestCategory>]
type JsonLayoutTests () =

    /// The members of a type that hold data: its properties and fields, without what the compiler adds.
    static let dataMembersOf (documentType : Type) : MemberInfo seq = seq {
        for found in
            documentType.GetMembers (
                BindingFlags.Instance
                ||| BindingFlags.Public
                ||| BindingFlags.NonPublic
            ) do
            match found with
            | :? PropertyInfo as property when property.GetIndexParameters().Length = 0 && property.CanRead -> found
            // The field that the compiler makes for a property has a name that no source can write.
            | :? FieldInfo as field when
                not (
                    field.Name.Contains ('@', StringComparison.Ordinal)
                    || field.Name.Contains ('<', StringComparison.Ordinal)
                )
                ->
                found
            | _ -> ()
    }

    /// The value that a member holds in a value of the model.
    static let valueOf (holder : obj) (dataMember : MemberInfo) : objnull =
        match dataMember with
        | :? PropertyInfo as property -> property.GetValue holder
        | :? FieldInfo as field -> field.GetValue holder
        | other -> invalidOp $"The member is neither a property nor a field: %s{other.Name}"

    /// The type of the value of a member.
    static let typeOf (dataMember : MemberInfo) : Type =
        match dataMember with
        | :? PropertyInfo as property -> property.PropertyType
        | :? FieldInfo as field -> field.FieldType
        | other -> invalidOp $"The member is neither a property nor a field: %s{other.Name}"

    /// <summary>
    /// The text or the number that a value comes down to when the options, value options and skippable values around
    /// it are taken off; no value for a value that is absent at any level, and for a value of another kind.
    /// </summary>
    static let rec scalarOf (value : objnull) : objnull voption =
        match value with
        | null -> ValueNone
        | :? string
        | :? int -> ValueSome value
        // A collection of one item is found by that item. A list of F# is a union, so this comes first.
        | :? IEnumerable as items when (items |> Seq.cast<objnull> |> Seq.truncate 2 |> Seq.length) = 1 ->
            items |> Seq.cast<objnull> |> Seq.head |> scalarOf
        | value ->
            let valueType = value.GetType ()

            // A union is found by what the one field of its case holds: an option, a value option and a skippable value
            // are such unions.
            if FSharpType.IsUnion valueType then
                match FSharpValue.GetUnionFields (value, valueType) with
                | _, [| inner |] -> scalarOf inner
                | _ -> ValueNone
            elif FSharpType.IsRecord valueType then
                // A record is found by what its first field holds.
                let fields = FSharpValue.GetRecordFields value
                scalarOf fields[0]
            else
                ValueNone

    /// Whether a JSON value is a text or a number, or holds it somewhere inside.
    static let rec holds (scalar : objnull) (json : JsonElement) : bool =
        match json.ValueKind, scalar with
        | JsonValueKind.String, (:? string as text) -> json.GetString () = text
        | JsonValueKind.Number, (:? int as number) -> json.GetInt32 () = number
        | JsonValueKind.Object, _ ->
            json.EnumerateObject ()
            |> Seq.exists (fun property -> holds scalar property.Value)
        | JsonValueKind.Array, _ -> json.EnumerateArray () |> Seq.exists (holds scalar)
        | _ -> false

    /// The name of the property of a document that holds a text or a number.
    static let propertyHolding (scalar : objnull) (document : JsonElement) : string voption =
        document.EnumerateObject ()
        |> Seq.tryFind (fun property -> holds scalar property.Value)
        |> ValueOption.map _.Name

    /// <summary>
    /// The value under the outer layer of an optional value, with its type: what an option, a value option or a
    /// skippable value holds, and a nullable or a reference itself. No value when the outer layer has none.
    /// </summary>
    static let unwrapped (value : objnull) (valueType : Type) : struct (objnull * Type) voption =
        let isMadeOf (definition : Type) =
            valueType.IsGenericType
            && Type.(=) (valueType.GetGenericTypeDefinition (), definition)

        if
            isMadeOf typedefof<_ option>
            || isMadeOf typedefof<_ voption>
            || isMadeOf typedefof<Skippable<_>>
        then
            match value with
            | null -> ValueNone
            | value ->
                match FSharpValue.GetUnionFields (value, valueType) with
                | _, [| inner |] -> ValueSome (struct (inner, valueType.GetGenericArguments()[0]))
                | _ -> ValueNone
        else
            match value, Nullable.GetUnderlyingType valueType with
            | null, _ -> ValueNone
            | value, null -> ValueSome (struct (value, valueType))
            | value, underlying -> ValueSome (struct (value, underlying))

    /// <summary>
    /// Everything the layout says about the members of a sample that the documents of the sample do not confirm; empty
    /// when the layout is right.
    /// </summary>
    static let disagreements (profile : SerializerProfile) (sample : StoredSample) : string array =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let documentType = sample.Present.GetType ()
        let present = JsonSerializer.SerializeToElement (sample.Present, documentType, options)
        let absent = JsonSerializer.SerializeToElement (sample.Absent, documentType, options)

        let scalars =
            dataMembersOf documentType
            |> Seq.map (fun dataMember -> struct (dataMember, scalarOf (valueOf sample.Present dataMember)))
            |> Seq.toArray

        // A member can be found in the document only by a value that no other member holds.
        let isUnique (scalar : objnull) =
            let holders =
                scalars
                |> Seq.filter (fun (struct (_, other)) -> other = ValueSome scalar)
                |> Seq.length

            holders = 1

        seq {
            for struct (dataMember, scalar) in scalars do
                match scalar with
                | ValueSome scalar when isUnique scalar ->
                    let writtenName = propertyHolding scalar present

                    // A member that is never written and declares a name stands for a property that something else
                    // writes. Its name is no fact of these documents, and tests of their own state it.
                    let isDeclaredOnly =
                        writtenName.IsNone
                        && dataMember.IsDefined (typeof<JsonPropertyNameAttribute>, false)

                    let memberOfDocument = { Holder = documentType; Member = dataMember }
                    let name = layout.MemberName memberOfDocument

                    if not isDeclaredOnly && name <> writtenName then
                        $"%s{dataMember.Name}: the layout names it %A{name}, the document %A{writtenName}"

                    // What the member is without a value shows where it has one in one document and none in the other.
                    let memberType = typeOf dataMember

                    match
                        writtenName,
                        unwrapped (valueOf sample.Present dataMember) memberType,
                        unwrapped (valueOf sample.Absent dataMember) memberType
                    with
                    | ValueSome writtenAs, ValueSome (struct (inner, innerType)), ValueNone ->
                        // The value is written as itself when the property holds what the serializer writes for the
                        // value alone, whatever that is.
                        let alone = JsonSerializer.SerializeToElement (inner, innerType, options)
                        let isItself = present.GetProperty(writtenAs).GetRawText() = alone.GetRawText ()

                        let expected =
                            match isItself, absent.TryGetProperty writtenAs with
                            | true, (false, _) -> OptionalEncoding.AlwaysUndefined
                            | true, (true, left) when left.ValueKind = JsonValueKind.Null -> OptionalEncoding.AlwaysNull
                            | _ -> OptionalEncoding.NotDescribed

                        let encoding = layout.OptionalEncoding (ValueSome memberOfDocument, memberType)

                        if encoding <> expected then
                            $"%s{dataMember.Name}: the layout says %A{encoding} of it without a value, the documents %A{expected}"
                    | _ -> ()
                | _ -> ()
        }
        |> Seq.toArray

    /// The layout of the options of a profile.
    static let layoutOf (profile : SerializerProfile) : IJsonLayout = JsonLayout.ofSerializerOptions (profile.Create ())

    /// A property of a type of the model, also one that is not public, as a value of that type has it.
    static let propertyOf (documentType : Type) (name : string) : DocumentMember =
        match
            documentType.GetProperty (
                name,
                BindingFlags.Instance
                ||| BindingFlags.Public
                ||| BindingFlags.NonPublic
            )
        with
        | null -> invalidOp $"%s{documentType.Name} has no property %s{name}."
        | property -> { Holder = documentType; Member = property }

    /// <summary>
    /// The property that a quotation reads from its parameter, as the quotation names it, with the type of the
    /// parameter: what a translator has in its hands when it asks the layout.
    /// </summary>
    static let memberReadBy (reading : Expr) : DocumentMember =
        match reading with
        | Lambda (_, PropertyGet (Some receiver, property, [])) -> { Holder = receiver.Type; Member = property }
        | other -> invalidArg (nameof reading) $"The quotation should read a property of its parameter: %A{other}"

    /// <summary>
    /// What the serializer shows of how it writes a value without a value in a member: nothing when it fails to
    /// write the holder, that it leaves the member out, or that it writes <c>null</c>.
    /// </summary>
    static let shownAbsent (options : JsonSerializerOptions) (held : DocumentMember) (holder : objnull) (name : string voption) =
        try
            let document = JsonSerializer.SerializeToElement (holder, held.Holder, options)

            match name with
            | ValueSome name ->
                match document.TryGetProperty name with
                | true, written when written.ValueKind = JsonValueKind.Null -> OptionalEncoding.AlwaysNull
                | true, _ -> OptionalEncoding.NotDescribed
                | false, _ -> OptionalEncoding.AlwaysUndefined
            | ValueNone -> OptionalEncoding.NotDescribed
        with :? NullReferenceException ->
            // A converter of FSharp.SystemTextJson that asks for the null and cannot write it.
            OptionalEncoding.NotDescribed

    /// <summary>
    /// Where what the layout says of a null of a type differs from what the serializer does with one, as texts for
    /// a failure message: by itself, in a field of a record and in a property of a class.
    /// </summary>
    static let disagreementsAboutNull (options : JsonSerializerOptions) (valueType : Type) : string array =
        let layout = JsonLayout.ofSerializerOptions options
        let boxType = typedefof<StoredBox<_>>.MakeGenericType valueType
        let slotType = typedefof<StoredSlot<_>>.MakeGenericType valueType

        let ofBox = {
            Holder = boxType
            Member = nonNull (boxType.GetProperty (nameof Unchecked.defaultof<StoredBox<int>>.Item))
        }

        let ofSlot = {
            Holder = slotType
            Member = nonNull (slotType.GetProperty (nameof Unchecked.defaultof<StoredSlot<int>>.Item))
        }

        seq {
            let alone =
                try
                    if JsonSerializer.SerializeToElement(null, valueType, options).ValueKind = JsonValueKind.Null then
                        OptionalEncoding.AlwaysNull
                    else
                        OptionalEncoding.NotDescribed
                with :? NullReferenceException ->
                    OptionalEncoding.NotDescribed

            let saidAlone = layout.OptionalEncoding (ValueNone, valueType)

            if saidAlone <> alone then
                $"by itself: the layout says %A{saidAlone}, the serializer shows %A{alone}"

            for struct (place, held, holder) in
                seq {
                    struct ("in a record", ofBox, FSharpValue.MakeRecord (boxType, [| null; box (Some "label") |], true))
                    struct ("in a class", ofSlot, nonNull (Activator.CreateInstance slotType))
                } do
                let shown = shownAbsent options held holder (layout.MemberName held)
                let said = layout.OptionalEncoding (ValueSome held, valueType)

                if said <> shown then
                    $"%s{place}: the layout says %A{said}, the serializer shows %A{shown}"
        }
        |> Seq.toArray

    /// The types of F# whose null the library writes in a way of its own, leaves out or fails to write.
    static let typesWithNull =
        seq {
            struct ("a tuple", typeof<int * string>)
            struct ("a map with texts as keys", typeof<Map<string, int>>)
            struct ("a map with numbers as keys", typeof<Map<int, int>>)
            struct ("a union without fields", typeof<StoredSide>)
            struct ("a union", typeof<StoredMark>)
            struct ("a union with a case that is null", typeof<StoredGrade>)
            struct ("a union that wraps an option", typeof<StoredNote>)
            struct ("a union that wraps a text", typeof<StoredCaption>)
            struct ("a union that wraps a number", typeof<StoredNumber>)
            struct ("a union that wraps a value option", typeof<StoredCount>)
            struct ("a union that wraps a skippable option", typeof<StoredHint>)
            struct ("a union that wraps a skippable text", typeof<StoredRemark>)
            struct ("a union that wraps a value option of an option", typeof<StoredDeep>)
            struct ("a union that wraps such a union", typeof<StoredEnvelope>)
            struct ("a union that wraps the unit value", typeof<StoredNothing>)
            struct ("a record", typeof<StoredAuthor>)
            struct ("a list", typeof<string list>)
            struct ("a set", typeof<Set<string>>)
        }
        |> ImmutableArray.CreateRange

    /// The ways to set FSharp.SystemTextJson up that decide what becomes of such a null.
    static let profilesForNull =
        let profile
            (name : string)
            (configure : JsonFSharpOptions -> JsonFSharpOptions)
            (ignoresNull : bool)
            : SerializerProfile = {
            Name = name
            Create =
                fun () ->
                    let options = (configure (JsonFSharpOptions.Default ())).ToJsonSerializerOptions()

                    if ignoresNull then
                        options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull

                    options
            HasFSharpConverter = true
        }

        seq {
            profile "FSharp.SystemTextJson as it is made" id false
            profile "FSharp.SystemTextJson that ignores null" id true
            profile "FSharp.SystemTextJson that lets a field of any type be null and ignores null" _.WithAllowNullFields() true
            profile "FSharp.SystemTextJson that writes a case without fields as its tag" _.WithUnionUnwrapFieldlessTags() false
            profile
                "FSharp.SystemTextJson that writes a case without fields as its tag and ignores null"
                _.WithUnionUnwrapFieldlessTags()
                true
            profile
                "FSharp.SystemTextJson that does not write a union of one case as its value and ignores null"
                _.WithUnionEncoding(
                    JsonUnionEncoding.AdjacentTag
                    ||| JsonUnionEncoding.UnwrapOption
                )
                true
            profile
                "FSharp.SystemTextJson that does not write an option as its value and ignores null"
                _.WithUnionEncoding(
                    JsonUnionEncoding.AdjacentTag
                    ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                )
                true
            profile
                "FSharp.SystemTextJson that always skips an option without a value and ignores null"
                _.WithSkippableOptionFields(SkippableOptionFields.Always)
                true
            // The setting that always skips options makes them written as their values. Setting the encoding after
            // it takes that back, and the library still skips them.
            profile
                "FSharp.SystemTextJson that always skips an option, does not write it as its value and ignores null"
                (fun converterOptions ->
                    converterOptions
                        .WithSkippableOptionFields(SkippableOptionFields.Always)
                        .WithUnionEncoding(
                            JsonUnionEncoding.AdjacentTag
                            ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                        )
                )
                true
        }
        |> ImmutableArray.CreateRange

    /// Every such type with every such way to set the serializer up.
    static member NullRows : IEnumerable<TestDataRow<struct (Type * SerializerProfile)>> = seq {
        for struct (name, valueType) in typesWithNull do
            for profile in profilesForNull do
                TestDataRow<struct (Type * SerializerProfile)>(
                    struct (valueType, profile),
                    DisplayName = $"a null of %s{name} must be laid out as it is written by %s{profile.Name}"
                )
    }

    /// Every type of the model with every way to set the serializer up that can write it.
    static member SampleRows : IEnumerable<TestDataRow<struct (SerializerProfile * StoredSample)>> = seq {
        for profile in SerializerProfiles.all do
            for sample in StoredSamples.all do
                if profile.HasFSharpConverter || sample.IsPlain then
                    TestDataRow<struct (SerializerProfile * StoredSample)>(
                        struct (profile, sample),
                        DisplayName = $"%s{sample.Name} must be laid out as it is written by %s{profile.Name}"
                    )
    }

    [<TestMethod; DynamicData(nameof JsonLayoutTests.SampleRows)>]
    member _.``A type must be laid out as the serializer writes it`` (profile : SerializerProfile, sample : StoredSample) =
        let found = disagreements profile sample
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should name every member as the document does, and say of a member without a value what the documents show.\n%s{listed}"
        )

    [<TestMethod; DynamicData(nameof JsonLayoutTests.NullRows)>]
    member _.``A null of a type of F# must be laid out as the serializer writes it``
        (valueType : Type, profile : SerializerProfile)
        =
        let found = disagreementsAboutNull (profile.Create ()) valueType
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of a null what the serializer does with it: null where it writes null, left out where it leaves the member out, and nothing where it fails.\n%s{listed}"
        )

    [<TestMethod>]
    member _.``The nulls of the types of F# must be written, left out and refused`` () =
        // The test above compares the layout with what the serializer does. This one checks that the serializer does
        // every one of the three things: a comparison with one outcome only would agree with a layout that knows one.
        let ignoring = profilesForNull[1].Create()
        let box = typeof<StoredBox<Map<string, int>>>
        let ofBox = propertyOf box "Item"
        let emptyBox : StoredBox<Map<string, int>> = { Item = Unchecked.defaultof<Map<string, int>>; Label = Some "label" }

        Assert.Throws<NullReferenceException>(
            Action (fun () ->
                JsonSerializer.Serialize (Unchecked.defaultof<Map<string, int>>, profilesForNull[0].Create())
                |> ignore
            ),
            "FSharp.SystemTextJson should fail to write a map that is null, or the tests check nothing."
        )
        |> ignore

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions (profilesForNull[0].Create()))
                .OptionalEncoding(ValueSome ofBox, typeof<Map<string, int>>),
            "No document holds a map that is null where null is written, so nothing is described."
        )

        Assert.AreEqual<string>(
            "{\"Label\":\"label\"}",
            JsonSerializer.Serialize (emptyBox, ignoring),
            "The library should leave a map that is null out where null is ignored: a map is a class to it."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysUndefined,
            (JsonLayout.ofSerializerOptions ignoring).OptionalEncoding(ValueSome ofBox, typeof<Map<string, int>>),
            "So the member is left out."
        )

        let noteBox = typeof<StoredBox<StoredNote>>
        let captionBox = typeof<StoredBox<StoredCaption>>

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysUndefined,
            (JsonLayout.ofSerializerOptions ignoring).OptionalEncoding(ValueSome (propertyOf noteBox "Item"), typeof<StoredNote>),
            "A union that is written as the option it wraps is null where the option has no value, so its null is a value, which is left out."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            (JsonLayout.ofSerializerOptions ignoring)
                .OptionalEncoding(ValueSome (propertyOf captionBox "Item"), typeof<StoredCaption>),
            "A union that is written as the text it wraps has no value that is null, so its null is written."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysUndefined,
            (JsonLayout.ofSerializerOptions (profilesForNull[2].Create()))
                .OptionalEncoding(ValueSome (propertyOf captionBox "Item"), typeof<StoredCaption>),
            "Where the options let a field of any type be null, the text can be null, and so the null of the union is left out."
        )

    [<TestMethod>]
    member _.``A union in a record with options of its own must be null or left out by the encoding that the record inherits``
        ()
        =
        // The options that the attribute of the record makes say that they inherit the encoding of unions. So the
        // common options decide whether a union of one case is written as the value it wraps, and only then is its
        // null a value, which is left out where null is ignored.
        let withEncoding (encoding : JsonUnionEncoding) : JsonSerializerOptions =
            let options =
                JsonFSharpOptions.Default().WithUnionEncoding(encoding).WithAllowOverride().ToJsonSerializerOptions()

            options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
            options

        let wrapped = propertyOf typeof<StoredInherited> "Wrapped"
        let empty = { Wrapped = Unchecked.defaultof<StoredNote>; Stated = "stated" }

        let asUnions =
            withEncoding (
                JsonUnionEncoding.AdjacentTag
                ||| JsonUnionEncoding.UnwrapOption
            )

        Assert.AreEqual<string>(
            "{\"Wrapped\":null,\"Stated\":\"stated\"}",
            JsonSerializer.Serialize (empty, asUnions),
            "The library should write the union as a union, as the common encoding says, and so write its null, or the test checks nothing."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            (JsonLayout.ofSerializerOptions asUnions).OptionalEncoding(ValueSome wrapped, typeof<StoredNote>),
            "The encoding of the attribute would write the union as the option it wraps, and the record does not follow it."
        )

        let asValues =
            withEncoding (
                JsonUnionEncoding.AdjacentTag
                ||| JsonUnionEncoding.UnwrapOption
                ||| JsonUnionEncoding.UnwrapSingleCaseUnions
            )

        Assert.AreEqual<string>(
            "{\"Stated\":\"stated\"}",
            JsonSerializer.Serialize (empty, asValues),
            "The library should leave the union out where the common encoding writes it as the option it wraps, or the test checks nothing."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysUndefined,
            (JsonLayout.ofSerializerOptions asValues).OptionalEncoding(ValueSome wrapped, typeof<StoredNote>),
            "The null of the union is the option without a value there, which is left out."
        )

    [<TestMethod>]
    member _.``A skippable value must not be described where System.Text.Json alone would write it`` () =
        // System.Text.Json has converters of its own for an option and for a value option, and none for a skippable
        // value: it refuses one as it refuses any union. Where default values are ignored it leaves a skipped value
        // out all the same, so the member is written as long as it has no value, and never with one.
        let options = JsonSerializerOptions (DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)

        Assert.AreEqual<string>(
            "{\"Count\":1}",
            JsonSerializer.Serialize (StoredSlot<Skippable<int>>(), options),
            "System.Text.Json should leave a skipped value out where default values are ignored, or the test checks nothing."
        )

        Assert.Throws<NotSupportedException>(
            Action (fun () ->
                JsonSerializer.Serialize (StoredSlot<Skippable<int>>(Item = Include 1), options)
                |> ignore
            ),
            "System.Text.Json should refuse a skippable value that has a value, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions options)
                .OptionalEncoding(ValueSome (propertyOf typeof<StoredSlot<Skippable<int>>> "Item"), typeof<Skippable<int>>),
            "A member that the serializer cannot write with a value is not described, also where it is left out without one."
        )

    [<TestMethod>]
    member _.``A null of a type that the serializer has no contract for must not be described`` () =
        // The serializer needs the contract of a type also to write a null of it, alone or in a member. So it writes
        // no document with a member of such a type, whatever the member holds.
        let options = JsonSerializerOptions ()
        let layout = JsonLayout.ofSerializerOptions options

        Assert.Throws<InvalidOperationException>(
            Action (fun () ->
                JsonSerializer.Serialize (StoredSlot<StoredRefused>(), options)
                |> ignore
            ),
            "System.Text.Json should refuse to write an object with a member of the type, also when the member is null, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredSlot<StoredRefused>> "Item"), typeof<StoredRefused>),
            "No document holds a member of the type, so the layout should not say how its null is written."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueNone, typeof<StoredRefused>),
            "The serializer writes no null of the type by itself either."
        )

    [<TestMethod>]
    member _.``The model must be found in its documents member by member`` () =
        // The test above compares the layout with what it finds in a document. This one checks that it finds
        // something: a search that found nothing would agree with a layout that says nothing.
        let written (profile : SerializerProfile) =
            JsonSerializer.SerializeToElement (StoredSamples.entry.Present, typeof<StoredEntry>, profile.Create ())

        let document = written SerializerProfiles.fsharpSystemTextJson

        Assert.AreEqual<string voption>(ValueSome "Title", propertyHolding "title-1" document, "A text is found by its value.")
        Assert.AreEqual<string voption>(ValueSome "Count", propertyHolding 31 document, "A number is found by its value.")
        Assert.AreEqual<string voption>(
            ValueNone,
            propertyHolding "draft-1" document,
            "A field that is never written is not found."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Choice",
            propertyHolding 32 (written SerializerProfiles.withoutUnwrappedOptions),
            "The value of an option that is written as a union is found inside the property."
        )

        Assert.AreEqual<struct (objnull * Type) voption>(
            ValueSome (struct (box (Some "nested-1"), typeof<string option>)),
            unwrapped (box (Some (Some "nested-1"))) typeof<string option option>,
            "One layer of an optional value is taken off at a time."
        )

        Assert.AreEqual<struct (objnull * Type) voption>(
            ValueNone,
            unwrapped (box (Skip : Skippable<int>)) typeof<Skippable<int>>,
            "A skipped value has nothing under it."
        )

    [<TestMethod>]
    member _.``A member that is never written must have the name it declares`` () =
        for profile in
            seq {
                SerializerProfiles.systemTextJson
                SerializerProfiles.fsharpSystemTextJson
            } do
            let layout = layoutOf profile

            Assert.AreEqual<string voption>(
                ValueSome "$type",
                layout.MemberName (propertyOf typeof<StoredPerson> "Discriminator"),
                $"A property of a class that declares its name stands for a property that something else writes (%s{profile.Name})."
            )

            Assert.AreEqual<string voption>(
                ValueSome "$type",
                layout.MemberName (propertyOf typeof<StoredEntry> "Discriminator"),
                $"So does a field of a record (%s{profile.Name})."
            )

            Assert.AreEqual<string voption>(
                ValueNone,
                layout.MemberName (propertyOf typeof<StoredPerson> "Secret"),
                $"A property that is never written and declares no name is in no document (%s{profile.Name})."
            )

            Assert.AreEqual<OptionalEncoding>(
                OptionalEncoding.NullOrUndefined,
                layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredPerson> "Discriminator"), typeof<string>),
                $"The serializer does not write the property, so it cannot say how it is written without a value (%s{profile.Name})."
            )

            Assert.AreEqual<OptionalEncoding>(
                OptionalEncoding.NullOrUndefined,
                layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredEntry> "Discriminator"), typeof<string>),
                $"Nor does it write the field of the record (%s{profile.Name})."
            )

    [<TestMethod>]
    member _.``A member that a derived class overrides must have the name of each declaration`` () =
        let options = SerializerProfiles.systemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let document =
            JsonSerializer.SerializeToElement (StoredSamples.person.Present, typeof<StoredPerson>, options)

        Assert.AreEqual<string voption>(
            ValueSome "pk",
            layout.MemberName (propertyOf typeof<StoredDocument> "Tenant"),
            "The declaration of the base class declares the name."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Tenant",
            layout.MemberName (propertyOf typeof<StoredPerson> "Tenant"),
            "The override declares none, and the serializer does not take the name of the base class for it."
        )

        Assert.AreEqual<string | null>(
            "tenant of id-1",
            document.GetProperty("pk").GetString(),
            "The document holds the value under the declared name."
        )
        Assert.AreEqual<string | null>(
            "tenant of id-1",
            document.GetProperty("Tenant").GetString(),
            "It holds it under the name of the override too."
        )

    [<TestMethod>]
    member _.``A member of a base class must have one name whichever class it is asked through`` () =
        let layout = layoutOf SerializerProfiles.systemTextJson

        Assert.AreEqual<string voption>(
            layout.MemberName (propertyOf typeof<StoredDocument> "Note"),
            layout.MemberName (propertyOf typeof<StoredPerson> "Note"),
            "Reflection gives the same property through the derived class, with another reflected type."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Note",
            layout.MemberName (propertyOf typeof<StoredPerson> "Note"),
            "It is named by the policy."
        )

    [<TestMethod>]
    member _.``The property that holds what no member stands for must have no name`` () =
        let layout = layoutOf SerializerProfiles.systemTextJson
        let extra = propertyOf typeof<StoredPerson> "Extra"

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName extra,
            "Its entries are written as properties of the document itself, and it is not."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome extra, typeof<Dictionary<string, JsonElement>>),
            "Nothing is written for it when it is null, so no property of its name is null or missing."
        )

    [<TestMethod>]
    member _.``A member of a record that is not written must be laid out as one that something else writes`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let written =
            JsonSerializer.SerializeToElement (StoredSamples.entry.Present, typeof<StoredEntry>, options)

        for writtenName in
            seq {
                "Summary"
                "Initial"
                "first_letter"
                "Whisper"
            } do
            Assert.IsFalse (
                fst (written.TryGetProperty writtenName),
                $"FSharp.SystemTextJson should write no property %s{writtenName}: its member is no field and is not included."
            )

        for name in
            seq {
                "Summary"
                "Initial"
                "Whisper"
            } do
            Assert.AreEqual<OptionalEncoding>(
                OptionalEncoding.NullOrUndefined,
                layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredEntry> name), typeof<string>),
                $"Whatever writes a property for %s{name} decides how it is written without a value."
            )

        Assert.AreEqual<string voption>(
            ValueSome "first_letter",
            layout.MemberName (propertyOf typeof<StoredEntry> "Initial"),
            "A member that is never written has the name it declares."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredEntry> "Summary"),
            "And no name when it declares none."
        )

    [<TestMethod>]
    member _.``A member that code decides about must be null or undefined`` () =
        let decided (configure : JsonSerializerOptions -> unit) : OptionalEncoding =
            let options = JsonSerializerOptions ()

            options.TypeInfoResolver <-
                DefaultJsonTypeInfoResolver()
                    .WithAddedModifier(fun info ->
                        for property in info.Properties do
                            if property.Name = "Note" then
                                property.ShouldSerialize <- Func<obj, objnull, bool>(fun _ value -> not (isNull value))
                    )

            configure options

            (JsonLayout.ofSerializerOptions options)
                .OptionalEncoding(ValueSome (propertyOf typeof<StoredPerson> "Note"), typeof<string>)

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NullOrUndefined,
            decided ignore,
            "A function decides whether the member is written, and a function says nothing before it is called."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            decided (fun options -> options.Converters.Add (StoredNullAsDashConverter ())),
            "When the function has the member written, a converter that asks for the null writes it, as anything."
        )

    [<TestMethod>]
    member _.``A member of a type that the serializer refuses must have no name`` () =
        let layout = layoutOf SerializerProfiles.systemTextJson

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredRefused> "Id"),
            "The serializer writes no value of the type, so no document holds a member of it."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredRefused> "Choice"), typeof<int voption>),
            "Nothing is known about how a member of it would be written."
        )

    [<TestMethod>]
    member _.``A member with a converter of its own must be null or left out as the serializer decides before it asks the converter``
        ()
        =
        let memberOf (name : string) : DocumentMember voption = ValueSome (propertyOf typeof<StoredMeeting> name)
        let plain = JsonSerializerOptions ()
        let ignoringNull = JsonSerializerOptions (DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)
        let written = JsonLayout.ofSerializerOptions plain
        let leftOut = JsonLayout.ofSerializerOptions ignoringNull

        Assert.AreEqual<string>(
            "{\"Day\":null,\"Topic\":null,\"Host\":\"-\",\"Alias\":null,\"Room\":null}",
            JsonSerializer.Serialize (StoredMeeting (), plain),
            "The serializer should write a null itself and ask only the converter that asks for it, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{}",
            JsonSerializer.Serialize (StoredMeeting (), ignoringNull),
            "A null that the options leave out should get to no converter, or the test checks nothing."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            written.OptionalEncoding (memberOf "Day", typeof<Nullable<DayOfWeek>>),
            "The converter of the member writes the day under the nullable; the serializer writes the null."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            written.OptionalEncoding (memberOf "Topic", typeof<string>),
            "A converter that does not ask for the null leaves it to the serializer, which writes null."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            written.OptionalEncoding (memberOf "Host", typeof<string>),
            "The converter of the member asks for the null and can write anything for it."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            written.OptionalEncoding (memberOf "Alias", typeof<string option>),
            "The converter of the member writes the whole option, and nothing shows what it writes for a value of it."
        )

        for struct (name, valueType) in
            seq {
                struct ("Day", typeof<Nullable<DayOfWeek>>)
                struct ("Topic", typeof<string>)
                struct ("Host", typeof<string>)
            } do
            Assert.AreEqual<OptionalEncoding>(
                OptionalEncoding.AlwaysUndefined,
                leftOut.OptionalEncoding (memberOf name, valueType),
                $"A null that the options leave out never gets to the converter of %s{name}."
            )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            leftOut.OptionalEncoding (memberOf "Alias", typeof<string option>),
            "An option of a member with a converter is not described, also where its null is left out: with a value it is what the converter writes."
        )

    [<TestMethod>]
    member _.``A member of a type that another converter writes must have no name`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        options.Converters.Insert (0, StoredPointAsTextConverter ())
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPoint> "X"),
            "The converter writes the point as a text, which has no members."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredPoint> "Label"), typeof<string option>),
            "So nothing is known about a member of it without a value."
        )

    [<TestMethod>]
    member _.``A member that a quotation reads must be laid out as the type it is read from is written`` () =
        let inherited = memberReadBy <@ fun (talk : StoredTalk) -> talk.Room @>

        Assert.AreEqual<Type | null>(
            typeof<StoredMeeting>,
            inherited.Member.ReflectedType,
            "A quotation names an inherited property as the class that declares it has it, so the member does not tell which type it is read from."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Room",
            (layoutOf SerializerProfiles.systemTextJson).MemberName inherited,
            "The serializer writes the derived class as an object, with the property that it inherits."
        )

        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredTalkAsTextConverter ())
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string voption>(
            ValueSome "Room",
            layout.MemberName (memberReadBy <@ fun (meeting : StoredMeeting) -> meeting.Room @>),
            "The serializer still writes the class that declares the member as an object."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName inherited,
            "A converter writes the class that inherits the member as a text, which has no members."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome inherited, typeof<string>),
            "So nothing is known about the inherited member without a value."
        )

    [<TestMethod>]
    member _.``A member that a derived class overrides must be laid out as the override when it is read from that class`` () =
        let layout = layoutOf SerializerProfiles.systemTextJson

        // The declaration of the base class, read from a value of the derived class: a quotation that code builds can
        // name a property this way.
        let declared (name : string) : DocumentMember = {
            propertyOf typeof<StoredDocument> name with
                Holder = typeof<StoredPerson>
        }

        Assert.AreEqual<string voption>(
            ValueSome "pk",
            layout.MemberName (declared "Tenant"),
            "The document of the derived class holds the declaration of the base class too, under the name that it declares."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Remark",
            layout.MemberName (declared "Remark"),
            "The document holds this property once, as the override, so the override is what the layout describes."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            layout.OptionalEncoding (ValueSome (declared "Remark"), typeof<string>),
            "The override declares no condition of its own, so it is written as null, whatever the declaration that it overrides says."
        )

        // A quotation that the compiler stores names the override itself.
        let quoted = memberReadBy <@ fun (person : StoredPerson) -> person.Remark @>

        Assert.AreEqual<Type | null>(
            typeof<StoredPerson>,
            quoted.Member.DeclaringType,
            "The quotation of compiled code names the property as the derived class declares it."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            layout.OptionalEncoding (ValueSome quoted, typeof<string>),
            "The override is described the same way when it is named itself."
        )

    [<TestMethod>]
    member _.``A record that names a converter of its own must be described only when a converter of the options writes it`` () =
        let span = { From = 3; To = 5; Note = None }
        let from = propertyOf typeof<StoredSpan> "From"
        let note = propertyOf typeof<StoredSpan> "Note"

        // The serializer asks the converters of the options before the attribute of a type.
        let named = SerializerProfiles.systemTextJson.Create ()

        Assert.AreEqual<string>(
            "\"3-5\"",
            JsonSerializer.Serialize (span, named),
            "The converter that the record names should write it, or the test checks nothing."
        )

        let layout = JsonLayout.ofSerializerOptions named

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName from,
            "The named converter writes the record as a text, which has no members."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome note, typeof<string option>),
            "So nothing is known about a member of it without a value."
        )

        let common = SerializerProfiles.fsharpSystemTextJson.Create ()

        Assert.AreEqual<string>(
            """{"From":3,"To":5,"Note":null}""",
            JsonSerializer.Serialize (span, common),
            "The converter of the options should write the record, or the test checks nothing."
        )

        let layout = JsonLayout.ofSerializerOptions common

        Assert.AreEqual<string voption>(
            ValueSome "From",
            layout.MemberName from,
            "FSharp.SystemTextJson writes the record, whatever converter the record names."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            layout.OptionalEncoding (ValueSome note, typeof<string option>),
            "So an option of it without a value is written as FSharp.SystemTextJson writes it."
        )

    [<TestMethod>]
    member _.``A value whose converter writes null itself must not be described where null is written`` () =
        let withConverter (converter : JsonConverter) (configure : JsonSerializerOptions -> unit) : IJsonLayout =
            let options = JsonSerializerOptions ()
            options.Converters.Add converter
            configure options
            JsonLayout.ofSerializerOptions options

        let note = propertyOf typeof<StoredPerson> "Note"
        let asked = withConverter (StoredNullAsDashConverter ()) ignore

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            asked.OptionalEncoding (ValueSome note, typeof<string>),
            "The converter asks for the null and can write anything for it."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            asked.OptionalEncoding (ValueNone, typeof<string>),
            "So it does for an item of a collection, which is always written."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysUndefined,
            (withConverter
                (StoredNullAsDashConverter ())
                (fun options -> options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull))
                .OptionalEncoding(ValueSome note, typeof<string>),
            "A null that the options leave out never gets to the converter."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            (withConverter (StoredTextConverter ()) ignore).OptionalEncoding(ValueSome note, typeof<string>),
            "A converter that does not ask for the null leaves it to the serializer, which writes null."
        )

    [<TestMethod>]
    member _.``A record that a factory made of the one of FSharp.SystemTextJson writes itself must not be described`` () =
        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredPointFactory ())
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string>(
            "\"51\"",
            JsonSerializer.Serialize (StoredSamples.point.Present, typeof<StoredPoint>, options),
            "The factory should write the point with a converter of its own, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPoint> "X"),
            "The contract names the converter that writes the point, and it is not the one for records."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredBox<int>> "Item"),
            "A record that this factory leaves to FSharp.SystemTextJson is not described either: a factory of an application can make that converter with other options than the ones it shows."
        )

    [<TestMethod>]
    member _.``A record that a factory made of the one of FSharp.SystemTextJson writes with options of its own must not be described``
        ()
        =
        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredQuietFactory ())
        let layout = JsonLayout.ofSerializerOptions options
        let author : StoredAuthor = { Name = "name-1" }

        Assert.AreEqual<string>(
            "{\"name\":\"name-1\"}",
            JsonSerializer.Serialize (author, options),
            "The factory should write the record with the converter of FSharp.SystemTextJson and names in snake case, which neither its own options nor the ones of the serializer say."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredAuthor> "Name"),
            "Only the factory of FSharp.SystemTextJson itself shows the options that a record is written with."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredAuthor> "Name"), typeof<string>),
            "So nothing is known about a field of the record without a value."
        )

    [<TestMethod>]
    member _.``A record whose contract a resolver of the caller makes must not be described`` () =
        let author : StoredAuthor = { Name = "name-1" }
        let name = propertyOf typeof<StoredAuthor> "Name"

        // The converter of FSharp.SystemTextJson for a record, made with options that nothing shows.
        let quiet (recordType : Type) : JsonConverter = nonNull (StoredConverters.quietRecordConverter recordType)

        let options =
            JsonSerializerOptions (
                SerializerProfiles.fsharpSystemTextJson.Create (),
                TypeInfoResolver =
                    StoredContractResolver<StoredAuthor>(fun options ->
                        JsonMetadataServices.CreateValueInfo<StoredAuthor>(options, quiet typeof<StoredAuthor>)
                    )
            )

        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string>(
            "{\"name\":\"name-1\"}",
            JsonSerializer.Serialize (author, options),
            "The resolver should have the record written with the converter of FSharp.SystemTextJson and names in snake case, which the converter among the options does not say, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName name,
            "The serializer did not make the contract of the record, so the converter among the options does not tell how the record is written."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome name, typeof<string>),
            "So nothing is known about a field of the record without a value."
        )

        // The same resolver leaves every other type to the serializer, as a resolver does that an application derives
        // from the one of the serializer to change some contracts.
        let passing =
            JsonSerializerOptions (
                SerializerProfiles.fsharpSystemTextJson.Create (),
                TypeInfoResolver =
                    StoredContractResolver<StoredCard>(fun options ->
                        JsonMetadataServices.CreateValueInfo<StoredCard>(options, quiet typeof<StoredCard>)
                    )
            )

        Assert.AreEqual<string>(
            "{\"Name\":\"name-1\"}",
            JsonSerializer.Serialize (author, passing),
            "The converter among the options should write the record, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Name",
            (JsonLayout.ofSerializerOptions passing).MemberName name,
            "The serializer made the contract of the record, also as the class that the application derives from its resolver, so the converter among the options tells how the record is written."
        )

    [<TestMethod>]
    member _.``A member with a converter of its own must be laid out by that converter when a resolver of the caller makes the contract of its type``
        ()
        =
        // The resolver makes the contract of an array of texts itself, as an array.
        let options =
            JsonSerializerOptions (
                IgnoreReadOnlyProperties = true,
                TypeInfoResolver =
                    StoredContractResolver<string array>(fun options ->
                        JsonMetadataServices.CreateArrayInfo<string>(
                            options,
                            JsonCollectionInfoValues<string array>(ObjectCreator = Func<string array>(fun () -> [||]))
                        )
                    )
            )

        let layout = JsonLayout.ofSerializerOptions options

        let written =
            JsonSerializer.SerializeToElement (StoredSamples.person.Present, typeof<StoredPerson>, options)

        let isWritten (name : string) : bool =
            let found, _ = written.TryGetProperty name
            found

        Assert.IsTrue (
            isWritten "Marks",
            "The serializer should write a read-only array by the contract of the resolver, as a collection, or the test checks nothing."
        )

        Assert.IsFalse (
            isWritten "Labels",
            "The serializer should write the member with the converter that the member names, whatever contract the resolver makes for its type: as one text it is no collection, so it is left out for being read-only."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPerson> "Labels"),
            "The converter of the member decides whether the member is a collection, and not the contract that the resolver makes for its type."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Marks",
            layout.MemberName (propertyOf typeof<StoredPerson> "Marks"),
            "A member without a converter of its own is written by the contract of the resolver."
        )

    [<TestMethod>]
    member _.``A member of a union must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let tag = propertyOf typeof<StoredMark> "Tag"

        Assert.AreEqual<string>(
            "{\"Case\":\"Marked\",\"Fields\":[3]}",
            JsonSerializer.Serialize (Marked 3, options),
            "FSharp.SystemTextJson should write the union in its own way, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName tag,
            "The converter among the options takes a union as it takes a record, and writes it with another converter, which the layout does not describe."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome tag, typeof<int>),
            "So nothing is known about a member of the union without a value."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a member once`` () =
        let layout = JsonLayout.ofSerializerOptions (JsonSerializerOptions (IgnoreReadOnlyProperties = true))
        let stamps = propertyOf typeof<StoredCounted> "Stamps"
        let first = layout.MemberName stamps
        let madeForFirst = StoredCountingConverterFactory.Made
        let second = layout.MemberName stamps

        Assert.AreEqual<string voption>(
            ValueSome "Stamps",
            first,
            "The factory of the member hands the collection to the converter of the options, so the serializer writes it although it is read-only."
        )

        Assert.AreEqual<string voption>(first, second, "The answer is the same every time.")

        Assert.AreEqual<int>(
            madeForFirst,
            StoredCountingConverterFactory.Made,
            "The layout has the factory of the member make a converter to find the answer, and remembers the answer: the factory made no converter for the second one."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a member once when several threads ask at a time`` () : Task = task {
        let layout = JsonLayout.ofSerializerOptions (JsonSerializerOptions (IgnoreReadOnlyProperties = true))
        let stamps = propertyOf typeof<StoredGated> "Stamps"

        // One thread has the contract of the class made first: the serializer asks the factory for it by itself.
        layout.MemberName (propertyOf typeof<StoredGated> "Title")
        |> ignore

        let madeBefore = StoredGatedConverterFactory.Made
        StoredGatedConverterFactory.Gate.Reset ()

        let asking = Array.init 4 (fun _ -> Task.Run (fun () -> layout.MemberName stamps))

        // The first thread waits inside the factory. The others get the time to come there too, which they would do
        // if every thread worked the answer out for itself.
        let entered =
            SpinWait.SpinUntil ((fun () -> StoredGatedConverterFactory.Made > madeBefore), TimeSpan.FromSeconds 10.0)

        do! Task.Delay 200
        StoredGatedConverterFactory.Gate.Set ()
        let! names = Task.WhenAll asking

        Assert.IsTrue (entered, "A thread should have asked the factory of the member.")

        Assert.AreEqual<int>(
            madeBefore + 1,
            StoredGatedConverterFactory.Made,
            "One thread works the answer out, and the others wait for it: the factory made one converter for the four of them."
        )

        Assert.IsTrue (
            names
            |> Array.forall (fun name -> name = ValueSome "Stamps"),
            "Every thread should get the answer."
        )
    }

    [<TestMethod>]
    member _.``A record whose converter an attribute of the caller makes must not be described`` () =
        let card = { FirstName = "a"; NickName = None }
        let firstName = propertyOf typeof<StoredCard> "FirstName"
        let options = SerializerProfiles.systemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string>(
            "{\"first_name\":\"a\"}",
            JsonSerializer.Serialize (card, options),
            "The converter of the attribute should write the record with its own options, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName firstName,
            "Nothing shows the options that the attribute made the converter with, so the layout should not guess a name."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredCard> "NickName"), typeof<string option>),
            "Nor should it guess how an option of the record is written without a value."
        )

        // The serializer asks the converters of the options before the attribute of a type.
        let common = SerializerProfiles.fsharpSystemTextJson.Create ()

        Assert.AreEqual<string>(
            "{\"FirstName\":\"a\",\"NickName\":null}",
            JsonSerializer.Serialize (card, common),
            "The converter of the options should write the record, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "FirstName",
            (JsonLayout.ofSerializerOptions common).MemberName firstName,
            "The converter of the options writes the record, with options that the layout reads."
        )

    [<TestMethod>]
    member _.``A read-only member with a rule that code has set must have its name when the options ignore read-only members``
        ()
        =
        let options = JsonSerializerOptions (IgnoreReadOnlyProperties = true)

        options.TypeInfoResolver <-
            DefaultJsonTypeInfoResolver()
                .WithAddedModifier(fun info ->
                    for property in info.Properties do
                        if property.Name = "Name" then
                            property.ShouldSerialize <- Func<obj, objnull, bool>(fun _ _ -> true)
                )

        let layout = JsonLayout.ofSerializerOptions options
        let written =
            JsonSerializer.SerializeToElement (StoredSamples.person.Present, typeof<StoredPerson>, options)

        Assert.IsTrue (
            fst (written.TryGetProperty "Name"),
            "The serializer should ask the rule and write the read-only property, or the test checks nothing."
        )

        Assert.IsFalse (fst (written.TryGetProperty "Id"), "And it should leave a read-only property without a rule out.")

        Assert.AreEqual<string voption>(
            ValueSome "Name",
            layout.MemberName (propertyOf typeof<StoredPerson> "Name"),
            "The rule decides, and not the option that ignores read-only members, so the member has its name."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NullOrUndefined,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredPerson> "Name"), typeof<string>),
            "A function decides whether the member is written, and a function says nothing before it is called."
        )

    [<TestMethod>]
    member _.``A value that is no member must be null without a value`` () =
        for profile in
            seq {
                SerializerProfiles.systemTextJson
                SerializerProfiles.fsharpSystemTextJson
            } do
            let layout = layoutOf profile

            for valueType in
                seq {
                    typeof<int option>
                    typeof<int voption>
                    typeof<Nullable<int>>
                    typeof<string>
                } do
                Assert.AreEqual<OptionalEncoding>(
                    OptionalEncoding.AlwaysNull,
                    layout.OptionalEncoding (ValueNone, valueType),
                    $"An item of a collection is always written, so %s{valueType.Name} without a value is null (%s{profile.Name})."
                )

            Assert.AreEqual<OptionalEncoding>(
                OptionalEncoding.NotDescribed,
                layout.OptionalEncoding (ValueNone, typeof<int>),
                $"A number always has a value (%s{profile.Name})."
            )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            (layoutOf SerializerProfiles.fsharpSystemTextJson).OptionalEncoding(ValueNone, typeof<Skippable<int>>),
            "FSharp.SystemTextJson writes a skipped value that it cannot leave out as null."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            (layoutOf SerializerProfiles.systemTextJson).OptionalEncoding(ValueNone, typeof<Skippable<int>>),
            "System.Text.Json alone cannot write a skippable value at all."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            (layoutOf SerializerProfiles.withoutUnwrappedOptions).OptionalEncoding(ValueNone, typeof<int option>),
            "An option that is written as a union with the name of its case is not described yet."
        )

    [<TestMethod>]
    member _.``The layout must leave the options it is made of as they are, and not follow a later change of them`` () =
        for profile in
            seq {
                SerializerProfiles.systemTextJson
                SerializerProfiles.fsharpSystemTextJson
            } do
            let options = profile.Create ()
            let layout = JsonLayout.ofSerializerOptions options
            let comment = propertyOf typeof<StoredEntry> "Comment"

            Assert.IsFalse (options.IsReadOnly, $"The caller may still have to finish the options (%s{profile.Name}).")
            Assert.IsNull (
                options.TypeInfoResolver,
                $"The layout should give the options of the caller no resolver (%s{profile.Name})."
            )

            options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

            Assert.AreEqual<string voption>(
                ValueSome "Comment",
                layout.MemberName comment,
                $"A change of the options after the layout is made does not reach it, also for a member that it is asked about for the first time (%s{profile.Name})."
            )

            Assert.AreEqual<string voption>(
                ValueSome "comment",
                (JsonLayout.ofSerializerOptions options).MemberName comment,
                $"A layout that is made after the change has it (%s{profile.Name})."
            )

    [<TestMethod>]
    member _.``The layout must ask the resolver that the options have, and leave it and the options as they are`` () =
        // The resolver gives one property another name, which no attribute and no policy says.
        let resolver =
            DefaultJsonTypeInfoResolver()
                .WithAddedModifier(fun (info : JsonTypeInfo) ->
                    for property in info.Properties do
                        if
                            Type.(=) (info.Type, typeof<StoredEntry>)
                            && String.Equals (property.Name, "Comment", StringComparison.Ordinal)
                        then
                            property.Name <- "remark"
                )

        let options = JsonSerializerOptions (TypeInfoResolver = resolver)
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string voption>(
            ValueSome "remark",
            layout.MemberName (propertyOf typeof<StoredEntry> "Comment"),
            "The layout should give the name that the resolver of the caller gives, as the serializer does."
        )

        Assert.IsFalse (options.IsReadOnly, "The caller may still have to finish the options.")
        Assert.AreSame<IJsonTypeInfoResolver | null>(
            resolver,
            options.TypeInfoResolver,
            "The options of the caller should keep the resolver they were given."
        )

    [<TestMethod>]
    member _.``The resolver that the layout shares with the serializer must take no change once the layout has asked it`` () =
        // A resolver cannot be copied, so the layout asks the one that the serializer asks.
        let rename =
            Action<JsonTypeInfo>(fun info ->
                if Type.(=) (info.Type, typeof<StoredEntry>) then
                    for property in info.Properties do
                        if String.Equals (property.Name, "Comment", StringComparison.Ordinal) then
                            property.Name <- "remark"
            )

        let resolver = DefaultJsonTypeInfoResolver ()
        let options = JsonSerializerOptions (TypeInfoResolver = resolver)
        let layout = JsonLayout.ofSerializerOptions options

        resolver.Modifiers.Add rename

        Assert.AreEqual<string voption>(
            ValueSome "remark",
            layout.MemberName (propertyOf typeof<StoredEntry> "Comment"),
            "A change of the resolver before the layout is asked should reach the layout, as it reaches the serializer."
        )

        Assert.Throws<InvalidOperationException>(
            Action (fun () -> resolver.Modifiers.Add rename),
            "The resolver should take no change after the layout has asked it: a change that reached only the serializer would go unnoticed."
        )
        |> ignore

        let written =
            JsonSerializer.SerializeToElement (StoredSamples.entry.Present, typeof<StoredEntry>, options)

        Assert.IsTrue (
            fst (written.TryGetProperty "remark"),
            "The serializer should write the name that the layout gives: both asked one resolver."
        )

    [<TestMethod>]
    member _.``A member that is left out because it is read-only must have the name it declares, and be null or undefined`` () =
        // System.Text.Json writes the record as an object here, and every field of a record is read-only.
        let options = JsonSerializerOptions (IgnoreReadOnlyProperties = true)
        let layout = JsonLayout.ofSerializerOptions options
        let written =
            JsonSerializer.SerializeToElement (StoredSamples.entry.Present, typeof<StoredEntry>, options)

        Assert.IsFalse (
            fst (written.TryGetProperty "Title"),
            "The serializer should leave a read-only property out, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredEntry> "Title"),
            "A member that is never written and declares no name has none."
        )

        Assert.AreEqual<string voption>(
            ValueSome "body_text",
            layout.MemberName (propertyOf typeof<StoredEntry> "Body"),
            "A member that is never written has the name it declares: it stands for a property that something else writes."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NullOrUndefined,
            layout.OptionalEncoding (ValueSome (propertyOf typeof<StoredEntry> "Choice"), typeof<int option>),
            "The serializer does not write the property, so it cannot say how it is written without a value."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Dropped",
            layout.MemberName (propertyOf typeof<StoredEntry> "Dropped"),
            "A member that says itself when it is ignored is not left out for being read-only."
        )

    [<TestMethod>]
    member _.``A field that is read-only must have no name when the options ignore read-only fields`` () =
        // F# declares a field that is not mutable as a property, so a type with a public read-only field is made here.
        let documentType =
            let assembly =
                AssemblyBuilder.DefineDynamicAssembly (AssemblyName "StoredFields", AssemblyBuilderAccess.Run)

            let declared =
                assembly
                    .DefineDynamicModule("StoredFields")
                    .DefineType("StoredFields", TypeAttributes.Public ||| TypeAttributes.Class)

            declared.DefineField ("Fixed", typeof<string>, FieldAttributes.Public ||| FieldAttributes.InitOnly)
            |> ignore

            declared.DefineField ("Free", typeof<string>, FieldAttributes.Public)
            |> ignore

            declared.DefineDefaultConstructor MethodAttributes.Public
            |> ignore

            declared.CreateType ()

        let fieldOf (name : string) : FieldInfo =
            match documentType.GetField name with
            | null -> invalidOp $"The type has no field %s{name}."
            | field -> field

        let document = Activator.CreateInstance documentType
        (fieldOf "Fixed").SetValue(document, "fixed")
        (fieldOf "Free").SetValue(document, "free")

        let namesWith (ignoresReadOnly : bool) =
            let options = JsonSerializerOptions (IncludeFields = true, IgnoreReadOnlyFields = ignoresReadOnly)
            let layout = JsonLayout.ofSerializerOptions options
            let written = JsonSerializer.SerializeToElement (document, documentType, options)

            struct {|
                Written = written.EnumerateObject () |> Seq.map _.Name |> Seq.toArray
                Fixed = layout.MemberName { Holder = documentType; Member = fieldOf "Fixed" }
                Free = layout.MemberName { Holder = documentType; Member = fieldOf "Free" }
            |}

        let kept = namesWith false
        let ignored = namesWith true

        CollectionAssert.AreEquivalent (
            [| "Fixed"; "Free" |],
            kept.Written,
            "The serializer should write both fields, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Fixed",
            kept.Fixed,
            "A read-only field is written unless the options say otherwise."
        )

        CollectionAssert.AreEqual (
            [| "Free" |],
            ignored.Written,
            "The serializer should leave the read-only field out, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(ValueNone, ignored.Fixed, "A field that is never written and declares no name has none.")
        Assert.AreEqual<string voption>(ValueSome "Free", ignored.Free, "A field that can be assigned is written.")

    [<TestMethod>]
    member _.``A record whose converter stands among the options by itself must not be described`` () =
        // The converter is made with options of its own, which nothing lets a layout read.
        let options = JsonSerializerOptions ()

        options.Converters.Add (
            JsonRecordConverter<StoredBox<int>>(
                options,
                JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always
            )
        )

        let layout = JsonLayout.ofSerializerOptions options
        let label = propertyOf typeof<StoredBox<int>> "Label"

        Assert.AreEqual<string>(
            """{"Item":72}""",
            JsonSerializer.Serialize (StoredSamples.box.Absent, typeof<StoredBox<int>>, options),
            "The converter should write the record with its own options, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName label,
            "The rules of the converter are not known, so the layout should not guess the name."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome label, typeof<string option>),
            "Nor should it guess how the field is written without a value."
        )
