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
/// serializer up that the layout supports, and the kind of JSON value that the document holds for it says what the
/// layout should say.
/// </summary>
[<TestClass; JsonLayoutUnitTestCategory>]
type JsonLayoutValueTests () =

    /// <summary>
    /// Everything the layout says about the members of a sample that the document of the sample does not confirm;
    /// empty when the layout is right.
    /// </summary>
    static let disagreements (profile : SerializerProfile) (sample : StoredValueSample) : string array =
        let options = profile.Create ()
        let layout = LayoutAssert.WantLayout options
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
        }
        |> Seq.toArray

    /// The kind of JSON value that a way to set the serializer up writes for a member of a sample; none when the
    /// document does not hold the member.
    static let kindOf (profile : SerializerProfile) (sample : StoredValueSample) (memberName : string) : JsonValueKind =
        let options = profile.Create ()
        let holderType = sample.Value.GetType ()
        let document = JsonSerializer.SerializeToElement (sample.Value, holderType, options)
        let layout = LayoutAssert.WantLayout options

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

    /// The layout of FSharp.SystemTextJson as it is made, with a change of the options.
    static let layoutWith (configure : JsonSerializerOptions -> unit) : IJsonLayout =
        let options = SerializerProfiles.fsharpDefault.Create ()
        configure options
        LayoutAssert.WantLayout options

    /// Every type of the model of values with every way to set the serializer up that the layout supports.
    static member SampleRows : IEnumerable<TestDataRow<struct (SerializerProfile * StoredValueSample)>> = seq {
        for profile in SerializerProfiles.supported do
            for sample in StoredValueSamples.all do
                TestDataRow<struct (SerializerProfile * StoredValueSample)>(
                    struct (profile, sample),
                    DisplayName = $"the values of %s{sample.Name} must be laid out as they are written by %s{profile.Name}"
                )
    }

    /// Every way to set the serializer up that the layout supports.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        SerializerProfiles.supported
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"a value that is no member must be laid out as it is written by %s{profile.Name}"
            )
        )

    /// <summary>
    /// Converters that write the values of an enumeration, some of them in a way of their own, with what the layout
    /// should say of the enumeration. The values that have no name are the ones that the converters differ in: zero,
    /// the number after the last constant and a combination of two constants, none of which a name stands for.
    /// </summary>
    static member EnumerationRows : IEnumerable<TestDataRow<StoredEnumerationCase>> =
        let caseOf (enumType : Type) (name : string) (expected : EnumEncoding) (converter : JsonConverter) = {
            Name = name
            EnumType = enumType
            Converter = converter
            Expected = expected
        }

        let hasName (value : 'Enum) = Enum.IsDefined (typeof<'Enum>, value)

        let cases = [|
            caseOf
                typeof<StoredColour>
                "numbers for the values with a name and a text for any other"
                EnumEncoding.NotDescribed
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if hasName value then
                        writer.WriteNumberValue (int value)
                    else
                        writer.WriteStringValue "other"
                ))
            caseOf
                typeof<StoredColour>
                "numbers, and a text for zero only"
                EnumEncoding.NotDescribed
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if int value = 0 then
                        writer.WriteStringValue "none"
                    else
                        writer.WriteNumberValue (int value)
                ))
            caseOf
                typeof<StoredDense>
                "numbers, and a text for the number after the last constant only"
                EnumEncoding.NotDescribed
                (StoredRuleConverter<StoredDense>(fun writer value ->
                    if int value = 4 then
                        writer.WriteStringValue "four"
                    else
                        writer.WriteNumberValue (int value)
                ))
            caseOf
                typeof<StoredWide>
                "numbers, and a text for a combination of two constants that is no constant only"
                EnumEncoding.NotDescribed
                (StoredRuleConverter<StoredWide>(fun writer value ->
                    if int value > 8 then
                        writer.WriteStringValue "combined"
                    else
                        writer.WriteNumberValue (int value)
                ))
            caseOf
                typeof<StoredColour>
                "names for the values with a name and null for any other"
                EnumEncoding.NotDescribed
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if hasName value then
                        writer.WriteStringValue (string value)
                    else
                        writer.WriteNullValue ()
                ))
            caseOf
                typeof<StoredColour>
                "names for the values with a name and numbers for any other"
                EnumEncoding.String
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if hasName value then
                        writer.WriteStringValue (string value)
                    else
                        writer.WriteNumberValue (int value)
                ))
            caseOf
                typeof<StoredColour>
                "numbers for the values with a name and a refusal for any other"
                EnumEncoding.Number
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if hasName value then
                        writer.WriteNumberValue (int value)
                    else
                        raise (JsonException "The converter of the test refuses the value.")
                ))
            caseOf
                typeof<StoredColour>
                "numbers for the values with a name and the exception for an unsupported value for any other"
                EnumEncoding.Number
                (StoredRuleConverter<StoredColour>(fun writer value ->
                    if hasName value then
                        writer.WriteNumberValue (int value)
                    else
                        raise (NotSupportedException "The converter of the test does not support the value.")
                ))
            caseOf
                typeof<StoredColour>
                "names and a refusal of any number"
                EnumEncoding.String
                (JsonStringEnumConverter (null, false))
            caseOf
                typeof<StoredDense>
                "numbers only"
                EnumEncoding.Number
                (StoredRuleConverter<StoredDense>(fun writer value -> writer.WriteNumberValue (int value)))
            caseOf
                typeof<StoredWide>
                "numbers only"
                EnumEncoding.Number
                (StoredRuleConverter<StoredWide>(fun writer value -> writer.WriteNumberValue (int value)))
            caseOf
                typeof<StoredDense>
                "names, and numbers for any value without one"
                EnumEncoding.String
                (JsonStringEnumConverter ())
            caseOf
                typeof<StoredWide>
                "names, and numbers for any value without one"
                EnumEncoding.String
                (JsonStringEnumConverter ())
        |]

        cases
        |> Seq.map (fun case ->
            TestDataRow<StoredEnumerationCase>(
                case,
                DisplayName =
                    $"the enumeration %s{case.EnumType.Name} must be described as %s{string case.Expected} when its converter writes %s{case.Name}"
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
        let layout = LayoutAssert.WantLayout options

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
        let catalog = StoredValueSamples.catalog
        let entry = StoredValueSamples.entry
        let fsharp = SerializerProfiles.fsharpDefault
        let byName = SerializerProfiles.named "enumerations by name"

        Assert.AreEqual<JsonValueKind>(JsonValueKind.Number, kindOf fsharp catalog "Colour", "An enumeration is a number.")

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            kindOf byName catalog "Colour",
            "With a converter of the options an enumeration is a string."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Number,
            kindOf fsharp entry "NamedColour",
            "FSharp.SystemTextJson does not read the converter that a field of a record names."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf fsharp catalog "Pair",
            "FSharp.SystemTextJson writes a tuple as an array."
        )
        Assert.AreEqual<JsonValueKind>(JsonValueKind.Array, kindOf fsharp catalog "Names", "A list is an array.")
        Assert.AreEqual<JsonValueKind>(JsonValueKind.String, kindOf fsharp catalog "Bytes", "An array of bytes is one string.")

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf fsharp catalog "ById",
            "FSharp.SystemTextJson writes a map whose keys are no texts as an array of pairs."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Object,
            kindOf fsharp catalog "Scores",
            "FSharp.SystemTextJson writes a map whose keys are texts as an object."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Array,
            kindOf (SerializerProfiles.named "every map as an array of pairs") catalog "Scores",
            "With the setting that writes every map as an array of pairs, a map of texts is an array too."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Undefined,
            kindOf fsharp entry "OtherColour",
            "FSharp.SystemTextJson does not write a property of a record that is no field."
        )

    [<TestMethod>]
    member _.``A value of another kind must not be described`` () =
        let layout = layoutWith ignore

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
            layout.TupleEncoding (ValueNone, typeof<StoredCatalog>),
            "A class is no tuple, although it is written as an object like one."
        )

        for valueType in
            seq {
                typeof<StoredAuthor>
                typeof<StoredShape>
                typeof<int list>
                typeof<int option>
            } do
            Assert.AreEqual<TupleEncoding>(
                TupleEncoding.NotDescribed,
                layout.TupleEncoding (ValueNone, valueType),
                $"%s{valueType.Name} is written by a converter of the same library as a tuple is, and is no tuple."
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
            layout.CollectionEncoding (ValueNone, typeof<Dictionary<string, int>>),
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
            (layoutWith ignore).MapEncoding(memberOf typeof<StoredPerson> "Extra", typeof<Dictionary<string, JsonElement>>),
            "Its entries are written as properties of the document itself, so no property of the document holds the dictionary."
        )

    [<TestMethod>]
    member _.``A collection or a map with derived types must not be described`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        let derivedList = StoredDerivedList ()
        derivedList.Add 1

        let derivedTable = StoredDerivedTable ()
        derivedTable["a"] <- 1

        // A value that is written as a type with derived types is not an array, nor an object with the keys alone.
        Assert.AreEqual<string>(
            "{\"$type\":\"derived\",\"$values\":[1]}",
            JsonSerializer.Serialize<StoredBaseList>(derivedList, options),
            "The list should be written as an object with the discriminator, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"$type\":\"derived\",\"a\":1}",
            JsonSerializer.Serialize<StoredBaseTable>(derivedTable, options),
            "The dictionary should have the discriminator among its properties, or the test checks nothing."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (ValueNone, typeof<StoredBaseList>),
            "A collection that can be written as an object is no array."
        )

        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            layout.MapEncoding (ValueNone, typeof<StoredBaseTable>),
            "A dictionary that can have the discriminator among its properties is no map of its keys alone."
        )

    [<TestMethod>]
    member _.``A policy for the keys of dictionaries must change the properties that a map is written as`` () =
        // This is why the layout refuses the policy: it would have to say how a key is spelled as a property.
        let propertiesOf (policy : JsonNamingPolicy | null) : string array =
            let options = SerializerProfiles.fsharpDefault.Create ()
            options.DictionaryKeyPolicy <- policy

            JsonSerializer.SerializeToElement(Map [ ("MyKey", 1) ], options).EnumerateObject()
            |> Seq.map _.Name
            |> Seq.toArray

        CollectionAssert.AreEqual ([| "MyKey" |], propertiesOf null, "Without a policy a key is the name of the property.")

        CollectionAssert.AreEqual (
            [| "myKey" |],
            propertiesOf JsonNamingPolicy.CamelCase,
            "With a policy a key is spelled by it, and a query that names the key has to spell it the same way."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a value once`` () =
        let layout = layoutWith (fun options -> options.Converters.Insert (0, StoredCountingColourConverter ()))

        let first = layout.EnumEncoding (ValueNone, typeof<StoredColour>)
        let writtenForFirst = StoredCountingColourConverter.Written
        let second = layout.EnumEncoding (ValueNone, typeof<StoredColour>)

        Assert.AreEqual<EnumEncoding>(EnumEncoding.Number, first, "The converter writes numbers.")

        Assert.AreEqual<EnumEncoding>(first, second, "The answer is the same every time.")

        Assert.IsGreaterThan (0, writtenForFirst, "The layout has the converter write the values to find the answer.")

        Assert.AreEqual<int>(
            writtenForFirst,
            StoredCountingColourConverter.Written,
            "The layout remembers the answer: the converter wrote nothing when the question was asked again."
        )

    [<TestMethod>]
    member _.``An enumeration without a value that has a name must be a number`` () =
        let layout = layoutWith (fun options -> options.Converters.Add (JsonStringEnumConverter ()))

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            layout.EnumEncoding (ValueNone, namelessEnumeration "JsonLayoutValueTests.Nameless"),
            "A converter of names writes a value without a name as its number, and this enumeration has no other value."
        )

    [<TestMethod>]
    member _.``An enumeration must not be described when its converter refuses the value that is asked`` () =
        // The converter of names can be made to refuse a value without a name, and this enumeration has no other one.
        let layout = layoutWith (fun options -> options.Converters.Add (JsonStringEnumConverter (null, false)))

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueNone, namelessEnumeration "JsonLayoutValueTests.Refused"),
            "The serializer writes no value of the enumeration, so there is nothing to describe."
        )

        // A converter of an application can refuse a value with the other exception that the serializer has for it.
        let unsupporting = SerializerProfiles.fsharpDefault.Create ()
        unsupporting.Converters.Insert (0, StoredFirstColourConverter ())

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
            (LayoutAssert.WantLayout unsupporting).EnumEncoding(ValueNone, typeof<StoredColour>),
            "The serializer does not write every value of the enumeration, so the layout cannot say how they are written."
        )

    [<TestMethod>]
    member _.``An enumeration must not be described when its converter writes its values in more than one way`` () =
        let layout =
            layoutWith (fun options -> options.Converters.Insert (0, StoredHalfNamedColourConverter ()))

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueNone, typeof<StoredColour>),
            "The converter writes the first value as its name and the other as its number, so a query can compare the values neither as numbers nor as texts."
        )

    [<TestMethod; DynamicData(nameof JsonLayoutValueTests.EnumerationRows)>]
    member _.``An enumeration must be described by what its converter writes for a value that has no name``
        (case : StoredEnumerationCase)
        =
        let layout = layoutWith (fun options -> options.Converters.Insert (0, case.Converter))

        Assert.AreEqual<EnumEncoding>(
            case.Expected,
            layout.EnumEncoding (ValueNone, case.EnumType),
            $"The converter writes %s{case.Name}, so the layout should say %s{string case.Expected}."
        )

    [<TestMethod>]
    member _.``A value of a member with a converter of its own must not be described`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options
        let sample = StoredValueSamples.ownConverterCatalog
        let holderType = sample.Value.GetType ()
        let document = JsonSerializer.SerializeToElement (sample.Value, holderType, options)

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            document.GetProperty("NamedColour").ValueKind,
            "The converter of the member should write the name of the value, or the test checks nothing."
        )

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.Number,
            document.GetProperty("NumberedColour").ValueKind,
            "The converter of the member should write the number, or the test checks nothing."
        )

        let of' = memberOf holderType

        for name in
            seq {
                "NamedColour"
                "NumberedColour"
                "TextColour"
            } do
            Assert.AreEqual<EnumEncoding>(
                EnumEncoding.NotDescribed,
                layout.EnumEncoding (of' name, typeof<StoredColour>),
                $"The converter of %s{name} writes the value, and nothing shows how."
            )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (of' "MaybeNamedColour", typeof<Nullable<StoredColour>>),
            "The same holds for a nullable, and for the value under it."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (of' "MaybeNamedColour", typeof<StoredColour>),
            "The converter of the member writes the value under the nullable."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (of' "Chosen", typeof<StoredColour>),
            "The converter of the member writes the option, and nothing shows what it writes for the enumeration under it."
        )

        Assert.AreEqual<TupleEncoding>(
            TupleEncoding.NotDescribed,
            layout.TupleEncoding (of' "Pair", typeof<int * string>),
            "A tuple that the converter of the member writes is not described."
        )

        Assert.AreEqual<CollectionEncoding>(
            CollectionEncoding.NotDescribed,
            layout.CollectionEncoding (of' "Names", typeof<string list>),
            "A list that the converter of the member writes is not described."
        )

        Assert.AreEqual<MapEncoding>(
            MapEncoding.NotDescribed,
            layout.MapEncoding (of' "Scores", typeof<Map<string, int>>),
            "A map that the converter of the member writes is not described."
        )

    [<TestMethod>]
    member _.``A value that a converter of the options writes must not be described as what its type is`` () =
        let layout =
            layoutWith (fun options ->
                options.Converters.Insert (0, StoredAsTextConverter<int * string>())
                options.Converters.Insert (0, StoredAsTextConverter<string list>())
                options.Converters.Insert (0, StoredAsTextConverter<Map<string, int>>())
            )

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
        let options = SerializerProfiles.fsharpDefault.Create ()
        options.Converters.Insert (0, StoredPairAsTextConverter ())
        let layout = LayoutAssert.WantLayout options

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
        let options = SerializerProfiles.fsharpDefault.Create ()
        options.Converters.Insert (0, StoredAsTextConverter<StoredAlbum>())
        let layout = LayoutAssert.WantLayout options
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
    member _.``A value must be described by the contract that a resolver of the caller makes for its type`` () =
        // The resolver makes the contract of the enumeration itself, from a converter that writes text.
        let options = SerializerProfiles.fsharpDefault.Create ()

        options.TypeInfoResolver <-
            StoredContractResolver<StoredColour>(fun options ->
                JsonMetadataServices.CreateValueInfo<StoredColour>(options, StoredAsTextConverter<StoredColour>())
            )

        let layout = LayoutAssert.WantLayout options
        let written = JsonSerializer.SerializeToElement (StoredCatalog "catalog-1", options)

        Assert.AreEqual<JsonValueKind>(
            JsonValueKind.String,
            written.GetProperty("Colour").ValueKind,
            "The serializer should write a member without a converter of its own by the contract of the resolver, or the test checks nothing."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.String,
            layout.EnumEncoding (memberOf typeof<StoredCatalog> "Colour", typeof<StoredColour>),
            "A member without a converter of its own is described by the contract of the resolver."
        )

    [<TestMethod>]
    member _.``A value must be described as the type that its member is read from is written`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        options.Converters.Add (StoredTalkAsTextConverter ())
        let layout = LayoutAssert.WantLayout options

        // The property as a quotation names it, also when it is read from the derived class.
        let colour = nonNull (typeof<StoredMeeting>.GetProperty "Colour")

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            layout.EnumEncoding (ValueSome { Holder = typeof<StoredMeeting>; Member = colour }, typeof<StoredColour>),
            "The serializer writes the class that declares the member as an object."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueSome { Holder = typeof<StoredTalk>; Member = colour }, typeof<StoredColour>),
            "A converter writes the class that inherits the member as a text, so no document of that class holds the value."
        )
