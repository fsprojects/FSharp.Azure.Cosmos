namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Reflection
open System.Reflection.Emit
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Reflection

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says about values that are no objects, compared
/// with what the serializer writes for them: every member of the model of values is written with every way to set the
/// serializer up, and the kind of JSON value that the document holds for it says what the layout should say.
/// </summary>
[<TestClass; UnitTestCategory; JsonLayoutTestCategory>]
type JsonLayoutValueTests () =

    /// <summary>
    /// Everything the layout says about the members of a sample that the document of the sample does not confirm;
    /// empty when the layout is right.
    /// </summary>
    static let disagreements (profile : SerializerProfile) (sample : StoredValueSample) : string array =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let holderType = sample.Value.GetType ()
        let document = JsonSerializer.SerializeToElement (sample.Value, holderType, options)

        seq {
            // Without this a layout that names no member would have nothing to disagree about.
            let saidNames = WrittenValues.namesOf layout holderType
            let writtenNames = WrittenValues.propertiesOf document

            if saidNames <> writtenNames then
                $"The layout names the members %A{saidNames}, the document %A{writtenNames}"

            for dataMember in ModelMembers.ofType holderType do
                let memberOfDocument = { Holder = holderType; Member = dataMember }
                let memberType = ModelMembers.typeOf dataMember

                // A member that the document does not hold was not written: every member of a sample holds a value
                // that no setting leaves out. It has the kind of no JSON value.
                let written =
                    match layout.MemberName memberOfDocument with
                    | ValueSome name ->
                        match document.TryGetProperty name with
                        | true, written -> written
                        | false, _ -> JsonElement ()
                    | ValueNone -> JsonElement ()

                yield! WrittenValues.disagreementsAbout layout dataMember.Name (ValueSome memberOfDocument) memberType written

                // The value under an option or a nullable is asked about as well, where the member is written as that
                // value: the serializer writes the same for the member as for the value alone.
                match ModelMembers.unwrapped (ModelMembers.valueOf sample.Value dataMember) memberType with
                | ValueSome (struct (inner, innerType)) when not (Type.(=) (innerType, memberType)) ->
                    let alone = JsonSerializer.SerializeToElement (inner, innerType, options)

                    if
                        written.ValueKind <> JsonValueKind.Undefined
                        && written.GetRawText () = alone.GetRawText ()
                    then
                        yield!
                            WrittenValues.disagreementsAbout
                                layout
                                $"what %s{dataMember.Name} holds"
                                (ValueSome memberOfDocument)
                                innerType
                                written
                | _ -> ()
        }
        |> Seq.toArray

    /// The kind of JSON value that a way to set the serializer up writes for a member of a sample; none when the
    /// document does not hold the member.
    static let kindOf (profile : SerializerProfile) (sample : StoredValueSample) (memberName : string) : JsonValueKind =
        let options = profile.Create ()
        let holderType = sample.Value.GetType ()
        let document = JsonSerializer.SerializeToElement (sample.Value, holderType, options)
        let layout = JsonLayout.ofSerializerOptions options

        match layout.MemberName { Holder = holderType; Member = nonNull (holderType.GetProperty memberName) } with
        | ValueSome name ->
            match document.TryGetProperty name with
            | true, written -> written.ValueKind
            | false, _ -> JsonValueKind.Undefined
        | ValueNone -> JsonValueKind.Undefined

    /// A member of a type, as a layout is asked about it.
    static let memberOf (holderType : Type) (name : string) : DocumentMember voption =
        ValueSome { Holder = holderType; Member = nonNull (holderType.GetProperty name) }

    /// An enumeration without any value, which no F# source declares.
    static let namelessEnumeration (assemblyName : string) : Type =
        AssemblyBuilder
            .DefineDynamicAssembly(AssemblyName assemblyName, AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Enumerations")
            .DefineEnum("Nameless", TypeAttributes.Public, typeof<int>)
            .CreateType()

    /// Every type of the model of values with every way to set the serializer up.
    static member SampleRows : IEnumerable<TestDataRow<struct (SerializerProfile * StoredValueSample)>> = seq {
        for profile in SerializerProfiles.all do
            for sample in StoredValueSamples.all do
                TestDataRow<struct (SerializerProfile * StoredValueSample)>(
                    struct (profile, sample),
                    DisplayName = $"the values of %s{sample.Name} must be laid out as they are written by %s{profile.Name}"
                )
    }

    /// Every way to set the serializer up.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        SerializerProfiles.all
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"a value that is no member must be laid out as it is written by %s{profile.Name}"
            )
        )

    [<TestMethod; DynamicData(nameof JsonLayoutValueTests.SampleRows)>]
    member _.``The values of a type must be laid out as the serializer writes them``
        (profile : SerializerProfile, sample : StoredValueSample)
        =
        let found = disagreements profile sample
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of every member what the kind of its JSON value in the document shows, and nothing of a member that the document does not hold.\n%s{listed}"
        )

    [<TestMethod; DynamicData(nameof JsonLayoutValueTests.ProfileRows)>]
    member _.``A value that is no member must be laid out as the serializer writes it`` (profile : SerializerProfile) =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options

        let found =
            seq {
                for struct (name, value, valueType) in StoredValueSamples.alone do
                    let written = JsonSerializer.SerializeToElement (value, valueType, options)
                    yield! WrittenValues.disagreementsAbout layout name ValueNone valueType written
            }
            |> Seq.toArray

        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of every value what the kind of the JSON value that is written for it shows.\n%s{listed}"
        )

    [<TestMethod>]
    member _.``The model of values must hold every kind of value in its documents`` () =
        // The tests above compare the layout with the kind of JSON value they find. This one checks that they find
        // every kind: a model that the serializer wrote one way only would agree with a layout that knows one way.
        let plain = SerializerProfiles.systemTextJson
        let fsharp = SerializerProfiles.fsharpSystemTextJson
        let catalog = StoredValueSamples.catalog
        let entry = StoredValueSamples.entry

        Assert.AreEqual<JsonValueKind>(JsonValueKind.Number, kindOf plain catalog "Colour", "An enumeration is a number.")

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            kindOf plain catalog "NamedColour",
            "With the converter that its member names an enumeration is a string."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            kindOf SerializerProfiles.enumerationsByName catalog "Colour",
            "With a converter of the options an enumeration is a string."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Number,
            kindOf SerializerProfiles.enumerationsByName catalog "NumberedColour",
            "The converter that a member names wins over the one of the options."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            kindOf plain entry "NamedColour",
            "System.Text.Json follows the converter that a field of a record names."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Number,
            kindOf fsharp entry "NamedColour",
            "FSharp.SystemTextJson does not read the converter that a field of a record names."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Object,
            kindOf plain catalog "Pair",
            "System.Text.Json writes a tuple as an object."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf fsharp catalog "Pair",
            "FSharp.SystemTextJson writes a tuple as an array."
        )

        Assert.AreEqual<JsonValueKind>(JsonValueKind.Array, kindOf plain catalog "Names", "A list is an array.")
        Assert.AreEqual<JsonValueKind>(JsonValueKind.String, kindOf plain catalog "Bytes", "An array of bytes is one string.")

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Object,
            kindOf plain catalog "ById",
            "System.Text.Json writes a map as an object."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf fsharp catalog "ById",
            "FSharp.SystemTextJson writes a map whose keys are no texts as an array of pairs."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            kindOf plain StoredValueSamples.sealedCatalog "Pair",
            "The converter of a member writes a tuple as a string."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Undefined,
            kindOf SerializerProfiles.ignoringReadOnlyProperties StoredValueSamples.readOnlyCatalog "Colour",
            "A read-only member is not written when the options ignore such members."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf SerializerProfiles.ignoringReadOnlyProperties StoredValueSamples.readOnlyCatalog "Numbers",
            "A read-only collection is written even then."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Undefined,
            kindOf fsharp entry "OtherColour",
            "FSharp.SystemTextJson does not write a property of a record that is no field."
        )

    [<TestMethod>]
    member _.``A value of another kind must not be described`` () =
        let layout = JsonLayout.ofSerializerOptions (SerializerProfiles.fsharpSystemTextJson.Create ())
        let plainLayout = JsonLayout.ofSerializerOptions (SerializerProfiles.systemTextJson.Create ())

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueNone, typeof<int>),
            "A number is no enumeration."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            layout.TupleEncoding (ValueNone, typeof<int array>),
            "An array is no tuple, although it is written as one can be."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            plainLayout.TupleEncoding (ValueNone, typeof<StoredCatalog>),
            "A class is no tuple, although it is written as an object like one."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (ValueNone, typeof<string>),
            "A text is no collection of its characters in a document."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (ValueNone, typeof<Map<string, int>>),
            "A map that is written as an object is no array."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            plainLayout.CollectionEncoding (ValueNone, typeof<Dictionary<string, int>>),
            "Neither is a dictionary, of which the serializer writes the entries itself."
        )

        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            layout.MapEncoding (ValueNone, typeof<string list>),
            "A list is no map."
        )

    [<TestMethod>]
    member _.``The extension data must not be described as a map`` () =
        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions (SerializerProfiles.systemTextJson.Create ()))
                .MapEncoding(memberOf typeof<StoredPerson> "Extra", typeof<Dictionary<string, JsonElement>>),
            "Its entries are written as properties of the document itself, so no property of the document holds the dictionary."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a value once`` () =
        let layout = JsonLayout.ofSerializerOptions (SerializerProfiles.systemTextJson.Create ())
        let counted = memberOf typeof<StoredCountedCatalog>

        // A question of every kind: each kind keeps its answers by itself.
        let ask () =
            struct (layout.EnumEncoding (counted "Colour", typeof<StoredColour>),
                    layout.TupleEncoding (counted "Pair", typeof<int * string>),
                    layout.CollectionEncoding (counted "Numbers", typeof<int array>),
                    layout.MapEncoding (counted "Lookup", typeof<Dictionary<string, int>>))

        let first = ask ()
        let madeForFirst = StoredCountingConverterFactory<StoredCountedCatalog>.Made
        let second = ask ()

        Assert.AreEqual<struct (EnumEncoding * TupleEncoding * CollectionEncoding * MapEncoding)>(
            struct (EnumEncoding.Number,
                    TupleEncoding.Object,
                    CollectionEncoding.Array typeof<int>,
                    MapEncoding.Object (typeof<string>, typeof<int>)),
            first,
            "The factory of each member hands its value to the converter of the options, which writes it as any value of its type."
        )

        Assert.AreEqual<struct (EnumEncoding * TupleEncoding * CollectionEncoding * MapEncoding)>(
            first,
            second,
            "The answers are the same every time."
        )

        Assert.AreEqual<int>(
            madeForFirst,
            StoredCountingConverterFactory<StoredCountedCatalog>.Made,
            "The layout has the factory of each member make a converter to find an answer, and remembers the answers of every kind: the factory made no converter when the questions were asked again."
        )

    [<TestMethod>]
    member _.``An enumeration without a value that has a name must be a number`` () =
        let options = JsonSerializerOptions ()
        options.Converters.Add (JsonStringEnumConverter ())

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            (JsonLayout.ofSerializerOptions options).EnumEncoding(ValueNone, namelessEnumeration "JsonLayoutValueTests.Nameless"),
            "A converter of names writes a value without a name as its number, and this enumeration has no other value."
        )

    [<TestMethod>]
    member _.``An enumeration must not be described when its converter refuses the value that is asked`` () =
        // The converter of names can be made to refuse a value without a name, and this enumeration has no other one.
        let options = JsonSerializerOptions ()
        options.Converters.Add (JsonStringEnumConverter (null, false))

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions options).EnumEncoding(ValueNone, namelessEnumeration "JsonLayoutValueTests.Refused"),
            "The serializer writes no value of the enumeration, so there is nothing to describe."
        )

        // A converter of an application can refuse a value with the other exception that the serializer has for it.
        let unsupporting = JsonSerializerOptions ()
        unsupporting.Converters.Add (StoredFirstColourConverter ())

        Assert.Throws<NotSupportedException>(
            Action (fun () ->
                JsonSerializer.Serialize (StoredColour.DarkBlue, unsupporting)
                |> ignore
            ),
            "The converter should refuse the second value as not supported, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions unsupporting).EnumEncoding(ValueNone, typeof<StoredColour>),
            "The serializer does not write every value of the enumeration, so the layout cannot say how they are written."
        )

    [<TestMethod>]
    member _.``An enumeration must not be described when its converter writes its values in more than one way`` () =
        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredHalfNamedColourConverter ())

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions options).EnumEncoding(ValueNone, typeof<StoredColour>),
            "The converter writes the first value as its name and the other as its number, so a query can compare the values neither as numbers nor as texts."
        )

    [<TestMethod>]
    member _.``A nullable with a converter of its member must be described by the type under it as well`` () =
        let layout = JsonLayout.ofSerializerOptions (SerializerProfiles.systemTextJson.Create ())
        let maybeNamed = memberOf typeof<StoredCatalog> "MaybeNamedColour"

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.String,
            layout.EnumEncoding (maybeNamed, typeof<Nullable<StoredColour>>),
            "The converter of the member writes the name of the value."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.String,
            layout.EnumEncoding (maybeNamed, typeof<StoredColour>),
            "The serializer has the converter of the member write the value under the nullable, so the layout describes that value the same way."
        )

    [<TestMethod>]
    member _.``A part of a value that a converter of its member writes must not be described`` () =
        let layout = JsonLayout.ofSerializerOptions (SerializerProfiles.systemTextJson.Create ())
        let chosen = memberOf typeof<StoredSealedCatalog> "Chosen"

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (chosen, typeof<StoredColour>),
            "The converter of the member writes the option, and nothing shows what it writes for the enumeration under it."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            layout.EnumEncoding (memberOf typeof<StoredCatalog> "Preferred", typeof<StoredColour>),
            "Without a converter of the member the enumeration under an option is written as any other one."
        )

    [<TestMethod>]
    member _.``A value that a converter of the options writes must not be described as what its type is`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        options.Converters.Insert (0, StoredAsTextConverter<int * string>())
        options.Converters.Insert (0, StoredAsTextConverter<string list>())
        options.Converters.Insert (0, StoredAsTextConverter<Map<string, int>>())
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            layout.TupleEncoding (ValueNone, typeof<int * string>),
            "The converter writes the tuple, in a way that the layout does not know."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (ValueNone, typeof<string list>),
            "The converter writes the list."
        )

        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            layout.MapEncoding (ValueNone, typeof<Map<string, int>>),
            "The converter writes the map."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.Array,
            layout.TupleEncoding (ValueNone, typeof<int * int>),
            "A tuple of another type is still written by FSharp.SystemTextJson."
        )

    [<TestMethod>]
    member _.``A tuple that a converter derived from the one of FSharp.SystemTextJson writes must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        options.Converters.Insert (0, StoredPairAsTextConverter ())
        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string>(
            "\"(1, a)\"",
            JsonSerializer.Serialize ((1, "a"), options),
            "The converter of the application should write the pair, and as text."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            layout.TupleEncoding (ValueNone, typeof<int * string>),
            "The class of the converter is the application's, whatever it is derived from, so nothing shows what it writes."
        )

    [<TestMethod>]
    member _.``A member of a type that another converter writes must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        options.Converters.Insert (0, StoredAsTextConverter<StoredAlbum>())
        let layout = JsonLayout.ofSerializerOptions options
        let field = memberOf typeof<StoredAlbum>

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (field "Cover", typeof<StoredColour>),
            "The converter writes the album as a text, which has no members."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            layout.TupleEncoding (field "Size", typeof<int * int>),
            "So nothing is known about a tuple in it."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (field "Tracks", typeof<string list>),
            "Nor about a list in it."
        )

        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            layout.MapEncoding (field "Plays", typeof<Map<string, int>>),
            "Nor about a map in it."
        )

    [<TestMethod>]
    member _.``A value must be described by the converter of its member when a resolver of the caller makes the contract of its type``
        ()
        =
        // The resolver makes the contract of the enumeration itself, from a converter that writes text.
        let options =
            JsonSerializerOptions (
                TypeInfoResolver =
                    StoredContractResolver<StoredColour>(fun options ->
                        JsonMetadataServices.CreateValueInfo<StoredColour>(options, StoredAsTextConverter<StoredColour>())
                    )
            )

        let layout = JsonLayout.ofSerializerOptions options
        let written = JsonSerializer.SerializeToElement (StoredCatalog "catalog-1", options)

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            written.GetProperty("Colour").ValueKind,
            "The serializer should write a member without a converter of its own by the contract of the resolver, or the test checks nothing."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Number,
            written.GetProperty("NumberedColour").ValueKind,
            "The serializer should write the member with the converter that the member names, whatever contract the resolver makes for its type, or the test checks nothing."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.String,
            layout.EnumEncoding (memberOf typeof<StoredCatalog> "Colour", typeof<StoredColour>),
            "A member without a converter of its own is described by the contract of the resolver."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            layout.EnumEncoding (memberOf typeof<StoredCatalog> "NumberedColour", typeof<StoredColour>),
            "The converter of the member writes the value, and not the contract that the resolver makes for its type."
        )

    [<TestMethod>]
    member _.``A value must be described as the type that its member is read from is written`` () =
        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredTalkAsTextConverter ())
        let layout = JsonLayout.ofSerializerOptions options

        // The property as a quotation names it, also when it is read from the derived class.
        let day = nonNull (typeof<StoredMeeting>.GetProperty "Day")

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.String,
            layout.EnumEncoding (ValueSome { Holder = typeof<StoredMeeting>; Member = day }, typeof<Nullable<DayOfWeek>>),
            "The serializer writes the class that declares the member as an object, and the member names a converter of names."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueSome { Holder = typeof<StoredTalk>; Member = day }, typeof<Nullable<DayOfWeek>>),
            "A converter writes the class that inherits the member as a text, so no document of that class holds the value."
        )
