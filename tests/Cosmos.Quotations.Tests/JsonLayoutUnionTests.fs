namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Reflection

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says about unions, compared with what the
/// serializer writes for them. A value of every case of the model of unions is written with every way to set the
/// serializer up that the layout supports. The document has to hold the mark that the layout gives the case, and no
/// mark of another case, and the value of every field at the place that the layout gives it.
/// </summary>
[<TestClass; JsonLayoutUnitTestCategory>]
type JsonLayoutUnionTests () =

    /// The cases of a union that a layout describes although it should describe none, as texts for a failure message.
    static let describedCasesOf
        (layout : IJsonLayout)
        (memberOfDocument : DocumentMember voption)
        (unionType : Type)
        (reason : string)
        : string seq = seq {
        for case in FSharpType.GetUnionCases (unionType, true) do
            if not (layout.UnionCaseEncoding (memberOfDocument, case)).IsNotDescribed then
                $"%s{unionType.Name}.%s{case.Name}: %s{reason}, and the layout describes the case"
    }

    /// <summary>
    /// Everything the layout says about the unions of the model that a way to set the serializer up does not confirm,
    /// when each value is written by itself; empty when the layout is right.
    /// </summary>
    static let disagreementsAlone (profile : SerializerProfile) : string array =
        let options = profile.Create ()
        let layout = LayoutAssert.WantLayout options

        seq {
            for sample in StoredUnionSamples.all do
                match WrittenUnions.writtenAll options sample.UnionType sample.Values with
                | ValueSome documents ->
                    for struct (value, document) in documents do
                        yield! WrittenUnions.disagreementsAbout layout options ValueNone sample.UnionType value document
                | ValueNone -> yield! describedCasesOf layout ValueNone sample.UnionType "the serializer refuses the union"
        }
        |> Seq.toArray

    /// <summary>
    /// The same for the values as a member of a record holds them: the layout is asked about the member, and the
    /// value is looked for under the name that the layout gives the member.
    /// </summary>
    static let disagreementsHeld (profile : SerializerProfile) : string array =
        let options = profile.Create ()
        let layout = LayoutAssert.WantLayout options

        seq {
            for sample in StoredUnionSamples.all do
                let holderType = typedefof<StoredHolder<_>>.MakeGenericType sample.UnionType

                let held = {
                    Holder = holderType
                    Member = nonNull (holderType.GetProperty (nameof Unchecked.defaultof<StoredHolder<int>>.Held))
                }

                let holders =
                    sample.Values
                    |> Seq.map (fun value -> FSharpValue.MakeRecord (holderType, [| value |], true))
                    |> Seq.cast<objnull>

                match WrittenUnions.writtenAll options holderType holders, layout.MemberName held with
                | ValueSome documents, ValueSome name ->
                    for struct (holder, document) in documents do
                        let value = held.Member :?> PropertyInfo |> _.GetValue(holder)

                        match document.TryGetProperty name with
                        | true, found ->
                            yield! WrittenUnions.disagreementsAbout layout options (ValueSome held) sample.UnionType value found
                        | false, _ ->
                            let struct (case, _) = WrittenUnions.caseOf sample.UnionType value
                            $"%s{sample.UnionType.Name}.%s{case.Name}: the document does not hold the member"
                | ValueNone, _ ->
                    yield! describedCasesOf layout (ValueSome held) sample.UnionType "the serializer refuses the union"
                | _, ValueNone ->
                    yield! describedCasesOf layout (ValueSome held) sample.UnionType "the serializer never writes the member"
        }
        |> Seq.toArray

    /// What a layout says of a case, as a text that two answers with the same steps share.
    static let described (encoding : UnionCaseEncoding) : string =
        match encoding with
        | UnionCaseEncoding.NotDescribed -> "not described"
        | UnionCaseEncoding.Written (mark, places) ->
            let markShown =
                match mark with
                | UnionCaseMark.Tag (path, tag) -> $"tag %s{tag} at value%s{WrittenSteps.shown path}"
                | UnionCaseMark.Unmarked -> "unmarked"

            let placesShown =
                places
                |> Seq.map (fun place ->
                    match place with
                    | UnionFieldPlace.Value path -> $"value%s{WrittenSteps.shown path}"
                    | UnionFieldPlace.Members path -> $"members of value%s{WrittenSteps.shown path}"
                )
                |> String.concat ", "

            $"%s{markShown}; fields: %s{placesShown}"

    /// The layout of FSharp.SystemTextJson with options, as a change of the common options.
    static let layoutOf (configure : JsonFSharpOptions -> JsonFSharpOptions) : struct (JsonSerializerOptions * IJsonLayout) =
        let options = (configure (JsonFSharpOptions.Default ())).ToJsonSerializerOptions()
        struct (options, LayoutAssert.WantLayout options)

    /// Every way to have the name of the tag written twice, as a row of a data-driven test.
    static member TagTwiceRows : IEnumerable<TestDataRow<StoredTagTwice>> =
        StoredUnionSamples.tagsTwice
        |> Seq.map (fun twice ->
            TestDataRow<StoredTagTwice>(twice, DisplayName = $"a union must not be described when %s{twice.Reason}")
        )

    /// Every way to set the serializer up that the layout supports.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        SerializerProfiles.supported
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"the unions must be laid out as they are written by %s{profile.Name}"
            )
        )

    /// Every union that the layout does not describe.
    static member NotDescribedRows : IEnumerable<TestDataRow<struct (string * Type)>> =
        StoredUnionSamples.notDescribed
        |> Seq.map (fun (struct (reason, unionType)) ->
            TestDataRow<struct (string * Type)>(
                struct (reason, unionType),
                DisplayName = $"a union must not be described when %s{reason}"
            )
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionTests.ProfileRows)>]
    member _.``The cases of a union must be laid out as the serializer writes them`` (profile : SerializerProfile) =
        let found = disagreementsAlone profile
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should give every case a mark that the document of a value of the case has and the document of no other case, and every field the place of its value.\n%s{listed}"
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionTests.ProfileRows)>]
    member _.``The cases of a union that a member holds must be laid out as the serializer writes them``
        (profile : SerializerProfile)
        =
        let found = disagreementsHeld profile
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of a union in a member what it says of the union alone, and nothing of a member that is not written.\n%s{listed}"
        )

    [<TestMethod>]
    member _.``The model of unions must be written in every way that the layout describes`` () =
        // The tests above compare the layout with the documents they find. This one checks that the documents are of
        // every kind: a model that the serializer wrote one way only would agree with a layout that knows one way.
        let said =
            SerializerProfiles.supported
            |> Seq.collect (fun profile ->
                let layout = LayoutAssert.WantLayout (profile.Create ())

                seq {
                    for sample in StoredUnionSamples.all do
                        for case in FSharpType.GetUnionCases (sample.UnionType, true) do
                            match layout.UnionCaseEncoding (ValueNone, case) with
                            | UnionCaseEncoding.Written (mark, places) ->
                                match mark with
                                | UnionCaseMark.Tag (path, _) ->
                                    let steps =
                                        path
                                        |> Seq.map (fun step -> if step.IsProperty then "property" else "item")
                                        |> String.concat ", "

                                    $"mark: a tag at [%s{steps}]"
                                | UnionCaseMark.Unmarked -> "mark: none"

                                for place in places do
                                    match place with
                                    | UnionFieldPlace.Value path ->
                                        let steps =
                                            path
                                            |> Seq.map (fun step -> if step.IsProperty then "property" else "item")
                                            |> String.concat ", "

                                        $"field: a value at [%s{steps}]"
                                    | UnionFieldPlace.Members path -> $"field: members at a path of %d{path.Length}"
                            | UnionCaseEncoding.NotDescribed -> "not described"
                }
            )
            |> Seq.distinct
            |> Seq.sort
            |> Seq.toArray

        CollectionAssert.AreEqual (
            [|
                "field: a value at []"
                "field: a value at [item]"
                "field: a value at [property, item]"
                "field: a value at [property, property]"
                "field: a value at [property]"
                "field: members at a path of 0"
                "mark: a tag at []"
                "mark: a tag at [item]"
                "mark: a tag at [property]"
                "mark: none"
            |],
            said,
            $"The ways to set the serializer up should have the model written with every kind of mark and with a field at every kind of place: %A{said}"
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionTests.NotDescribedRows)>]
    member _.``A union that the layout does not support must not be described`` (reason : string, unionType : Type) =
        for profile in
            seq {
                SerializerProfiles.fsharpDefault
                SerializerProfiles.production
            } do
            let found =
                describedCasesOf (LayoutAssert.WantLayout (profile.Create ())) ValueNone unionType reason
                |> Seq.toArray

            let listed = String.Join ("\n", found)

            Assert.IsEmpty (found, $"A union is described as a whole, and no case of this one is (%s{profile.Name}).\n%s{listed}")

    [<TestMethod>]
    member _.``A union with a case that declares the name of a field must be one that the serializer writes`` () =
        // The test above would pass for a union that the serializer cannot write at all. This one checks the union.
        let options = (JsonFSharpOptions.Default().WithUnionNamedFields()).ToJsonSerializerOptions()

        Assert.AreEqual<string>(
            "{\"Case\":\"Pair\",\"Fields\":{\"first\":1,\"Item2\":2}}",
            JsonSerializer.Serialize (Pair (1, 2), options),
            "The serializer should write the field under the name that the case declares for it, or the test checks nothing."
        )

    [<TestMethod>]
    member _.``A union that the serializer refuses must not be described`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        Assert.Throws<TargetInvocationException>(
            Action (fun () -> JsonSerializer.Serialize (Plain, options) |> ignore),
            "FSharp.SystemTextJson should refuse a union with a case that has two attributes with names, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredTwice> "Plain")),
            "No case of a union that the serializer cannot write is described."
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionTests.TagTwiceRows)>]
    member _.``A union with a case that is written with the name of its tag twice must not be described``
        (twice : StoredTagTwice)
        =
        let options = twice.Settings.ToJsonSerializerOptions ()
        options.PropertyNamingPolicy <- twice.PropertyNaming
        let layout = LayoutAssert.WantLayout options
        use document = JsonDocument.Parse (JsonSerializer.Serialize (twice.Value, options))

        Assert.HasCount (
            2,
            document.RootElement.EnumerateObject ()
            |> Seq.where _.NameEquals(twice.TagName),
            "FSharp.SystemTextJson should write the name of the tag twice for the case, or the test checks nothing."
        )

        let found =
            describedCasesOf layout ValueNone typeof<StoredShape> "a case has two properties of the name of the tag"
            |> Seq.toArray

        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"A document of that case can hold the tag of another case in its second property, so no case can be told.\n%s{listed}"
        )

    [<TestMethod>]
    member _.``A case with two fields that a naming policy spells alike must not be described`` () =
        let struct (options, layout) =
            layoutOf (fun settings -> settings.WithUnionNamedFields().WithUnionFieldNamingPolicy JsonNamingPolicy.SnakeCaseLower)

        Assert.AreEqual<string>(
            "{\"Case\":\"Both\",\"Fields\":{\"foo_bar\":1,\"foo_bar\":2}}",
            JsonSerializer.Serialize (StoredFieldsAlike.Both (1, 2), options),
            "The policy should give the two fields one name, or the test checks nothing."
        )

        let caseOf = WrittenUnions.caseNamed typeof<StoredFieldsAlike> "Both"

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, caseOf)),
            "A document cannot tell the two fields apart, so the case is not described."
        )

        // What tells the cases apart is the same for both, so the other case of the union is described.
        Assert.AreEqual<string>(
            "tag Alone at value[\"Case\"]; fields: value[\"Fields\"][\"item\"]",
            described (layout.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredFieldsAlike> "Alone")),
            "The case with one field is written as it is, whatever the other case is."
        )

        // Without the policy the names differ, and fields that are not named have no names to be spelled alike.
        let struct (_, unspelled) = layoutOf _.WithUnionNamedFields()

        Assert.AreEqual<string>(
            "tag Both at value[\"Case\"]; fields: value[\"Fields\"][\"FooBar\"], value[\"Fields\"][\"Foo_Bar\"]",
            described (unspelled.UnionCaseEncoding (ValueNone, caseOf)),
            "Without the policy the two fields are two properties."
        )

        let struct (_, unnamed) = layoutOf _.WithUnionFieldNamingPolicy(JsonNamingPolicy.SnakeCaseLower)

        Assert.AreEqual<string>(
            "tag Both at value[\"Case\"]; fields: value[\"Fields\"][0], value[\"Fields\"][1]",
            described (unnamed.UnionCaseEncoding (ValueNone, caseOf)),
            "Fields that are not named are the items of an array, whatever the policy spells."
        )

    [<TestMethod>]
    member _.``A union whose cases are written with one tag by a naming policy must not be described`` () =
        let struct (options, layout) = layoutOf _.WithUnionTagNamingPolicy(JsonNamingPolicy.SnakeCaseLower)

        Assert.AreEqual<string>(
            "{\"Case\":\"foo_bar\"}",
            JsonSerializer.Serialize (FooBar, options),
            "The policy should give the case a tag that the other case has too, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"Case\":\"foo_bar\",\"Fields\":[3]}",
            JsonSerializer.Serialize (Foo_Bar 3, options),
            "The other case has that tag."
        )

        let found =
            describedCasesOf layout ValueNone typeof<StoredFoldedTag> "two cases have one tag"
            |> Seq.toArray

        let listed = String.Join ("\n", found)

        Assert.IsEmpty (found, $"A document cannot tell the two cases apart, so none is described.\n%s{listed}")

        // Without the policy the tags differ.
        let struct (_, plain) = layoutOf id

        Assert.AreEqual<string>(
            "tag FooBar at value[\"Case\"]; fields: ",
            described (plain.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredFoldedTag> "FooBar")),
            "The names of the cases are two tags."
        )

    [<TestMethod>]
    member _.``A union must not be described when a case writes the members of a record that the layout does not know next to an internal tag``
        ()
        =
        let name = "Name"

        let options =
            (JsonFSharpOptions
                .Default()
                .WithUnionInternalTag()
                .WithUnionNamedFields()
                .WithUnionUnwrapRecordCases()
                .WithUnionTagName
                name)
                .ToJsonSerializerOptions()

        // A converter of the options writes the record as text, and the union still writes its members.
        options.Converters.Insert (0, StoredAsTextConverter<StoredAuthor>())
        let layout = LayoutAssert.WantLayout options
        use document = JsonDocument.Parse (JsonSerializer.Serialize (Signed { Name = "signer" }, options))

        Assert.HasCount (
            2,
            document.RootElement.EnumerateObject ()
            |> Seq.where _.NameEquals(name),
            "The union should write the name of the tag twice, or the test checks nothing."
        )

        let found =
            describedCasesOf layout ValueNone typeof<StoredShape> "a case holds members whose names are not known"
            |> Seq.toArray

        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"One of the members of the record can have the name of the tag, so no case can be told.\n%s{listed}"
        )

        // Next to an adjacent tag the names cannot meet: the other cases are described.
        let adjacent =
            (JsonFSharpOptions.Default().WithUnionNamedFields().WithUnionUnwrapRecordCases()).ToJsonSerializerOptions()

        adjacent.Converters.Insert (0, StoredAsTextConverter<StoredAuthor>())

        Assert.AreEqual<string>(
            "not described",
            described (
                (LayoutAssert.WantLayout adjacent)
                    .UnionCaseEncoding(ValueNone, WrittenUnions.caseNamed typeof<StoredShape> "Signed")
            ),
            "The case that holds the record is not described, whatever the tag is."
        )

        Assert.IsFalse (
            (LayoutAssert.WantLayout adjacent)
                .UnionCaseEncoding(ValueNone, WrittenUnions.caseNamed typeof<StoredShape> "Circle")
                .IsNotDescribed,
            "Another case is described, because the tag has a property of its own."
        )

    [<TestMethod>]
    member _.``A union of a member with a converter of its own must not be described`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options
        let circle = WrittenUnions.caseNamed typeof<StoredShape> "Circle"
        let rect = WrittenUnions.caseNamed typeof<StoredShape> "Rect"

        let memberOf (name : string) : DocumentMember voption =
            ValueSome {
                Holder = typeof<StoredDrawing>
                Member = nonNull (typeof<StoredDrawing>.GetProperty name)
            }

        let document =
            JsonSerializer.SerializeToElement (
                StoredDrawing (Shape = Circle 3, Loud = Rect (4, "rect"), Hidden = Circle 5),
                options
            )

        Assert.AreEqual<string>(
            "{\"Case\":\"RECT\",\"Fields\":[4,\"rect\"]}",
            document.GetProperty("Loud").GetRawText(),
            "The converter of the member should write the union with tags in capital letters, which the options do not say, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (memberOf "Loud", rect)),
            "Nothing shows the options that the converter of a member writes a union with, also when it is a converter of FSharp.SystemTextJson."
        )

        Assert.AreEqual<string>(
            described (layout.UnionCaseEncoding (ValueNone, circle)),
            described (layout.UnionCaseEncoding (memberOf "Shape", circle)),
            "A member without a converter of its own holds the union as it is written anywhere."
        )

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (memberOf "Hidden", circle)),
            "A member that the serializer never writes is not described: something else writes its property."
        )

    [<TestMethod>]
    member _.``An option must not be described as a union`` () =
        let layout = LayoutAssert.WantLayout (SerializerProfiles.fsharpDefault.Create ())

        for name in
            seq {
                "Some"
                "None"
            } do
            Assert.AreEqual<string>(
                "not described",
                described (layout.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<int option> name)),
                $"An option is written as its value, and a layout says nothing of it as a union (%s{name})."
            )

    [<TestMethod>]
    member _.``A union that has options of its own must not be described`` () =
        // The options of the attribute are the union's own when the common options let an attribute give them.
        let struct (options, layout) = layoutOf _.WithAllowOverride()
        let stamped = WrittenUnions.caseNamed typeof<StoredStamp> "Stamped"
        let labelled = WrittenUnions.caseNamed typeof<StoredLabel> "Labelled"

        Assert.AreEqual<string>(
            "{\"Stamped\":{\"text\":\"stamp\",\"size\":13}}",
            JsonSerializer.Serialize (Stamped ("stamp", 13), options),
            "The serializer should write the union with the options of the attribute, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, stamped)),
            "The options of the attribute are not the common ones that the layout reads."
        )

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, labelled)),
            "A part of the encoding in an attribute makes the options of the union its own as well."
        )

        // The options of the union are given in the overrides.
        let struct (_, overridden) =
            layoutOf (fun common ->
                common.WithOverrides (fun _ ->
                    let overrides = Dictionary<Type, JsonFSharpOptions>()
                    overrides.Add (typeof<StoredShape>, common.WithUnionExternalTag ())
                    // For every type that is made of the generic union.
                    overrides.Add (typedefof<StoredOutcome<_>>, common.WithUnionExternalTag ())
                    overrides :> IDictionary<Type, JsonFSharpOptions>
                )
            )

        Assert.AreEqual<string>(
            "not described",
            described (overridden.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredShape> "Circle")),
            "The options in the overrides are the union's own."
        )

        Assert.AreEqual<string>(
            "not described",
            described (overridden.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredOutcome<int>> "Fine")),
            "The options are given for every type that is made of the generic union."
        )

        Assert.AreEqual<string>(
            "tag Front at value[\"Case\"]; fields: ",
            described (overridden.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredSide> "Front")),
            "A union that has none is described."
        )

    [<TestMethod>]
    member _.``A record in a case must not be described when it is not written as the record is by itself`` () =
        let signed = WrittenUnions.caseNamed typeof<StoredShape> "Signed"
        let dealt = WrittenUnions.caseNamed typeof<StoredDeal> "Dealt"
        let undealt = WrittenUnions.caseNamed typeof<StoredDeal> "Undealt"

        // A converter of the options writes the record as text, so what the layout says of its members is not about
        // the record in the case, whose members the union writes itself.
        let asText = (JsonFSharpOptions.Default().WithUnionUnwrapRecordCases()).ToJsonSerializerOptions()
        asText.Converters.Insert (0, StoredAsTextConverter<StoredAuthor>())

        Assert.AreEqual<string>(
            "{\"Case\":\"Signed\",\"Fields\":{\"Name\":\"signer\"}}",
            JsonSerializer.Serialize (Signed { Name = "signer" }, asText),
            "The union should write the members of the record in place of the fields, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described ((LayoutAssert.WantLayout asText).UnionCaseEncoding(ValueNone, signed)),
            "The record by itself is written by another converter."
        )

        // The record has options of its own in an attribute, which the common options let it have.
        let struct (ofAttribute, withAttribute) =
            layoutOf (fun common -> common.WithAllowOverride().WithUnionUnwrapRecordCases())

        Assert.AreEqual<string>(
            "{\"Case\":\"Dealt\",\"Fields\":{\"SignedAs\":\"signer\",\"Alias\":null}}",
            JsonSerializer.Serialize (Dealt { SignedAs = "signer"; Alias = None }, ofAttribute),
            "The union should write the members of the record with its own options, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (withAttribute.UnionCaseEncoding (ValueNone, dealt)),
            "The record is written by itself with the options of its attribute, which are not the ones that the layout describes it with."
        )

        Assert.AreEqual<string>(
            "tag Undealt at value[\"Case\"]; fields: ",
            described (withAttribute.UnionCaseEncoding (ValueNone, undealt)),
            "A case without such a record is described."
        )

    [<TestMethod>]
    member _.``A union whose contract a resolver of the caller makes must not be described`` () =
        let front = WrittenUnions.caseNamed typeof<StoredSide> "Front"

        // The resolver makes the contract from the converter of FSharp.SystemTextJson for the union, which it has made
        // with a naming policy that the options do not have.
        let options = SerializerProfiles.fsharpDefault.Create ()

        options.TypeInfoResolver <-
            StoredContractResolver<StoredSide>(fun options ->
                JsonMetadataServices.CreateValueInfo<StoredSide>(
                    options,
                    nonNull (StoredLoudUnionConverterFactory().CreateConverter(typeof<StoredSide>, options))
                )
            )

        Assert.AreEqual<string>(
            "{\"Case\":\"FRONT\"}",
            JsonSerializer.Serialize (Front, options),
            "The resolver should have the union written with the converter that it made, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described ((LayoutAssert.WantLayout options).UnionCaseEncoding(ValueNone, front)),
            "The serializer did not make the contract of the union, so the converter among the options does not tell how the union is written."
        )

    [<TestMethod>]
    member _.``A layout must work out how a case is written once`` () =
        let struct (_, layout) = layoutOf _.WithUnionTagNamingPolicy(StoredCountingNamingPolicy<StoredSide>())

        let front = WrittenUnions.caseNamed typeof<StoredSide> "Front"
        let first = layout.UnionCaseEncoding (ValueNone, front)
        let spelledForFirst = StoredCountingNamingPolicy<StoredSide>.Spelled
        let second = layout.UnionCaseEncoding (ValueNone, front)

        Assert.AreEqual<string>(
            "tag Front at value[\"Case\"]; fields: ",
            described first,
            "The naming policy of the tags spells the name of the case."
        )

        Assert.AreEqual<string>(described first, described second, "The answer is the same every time.")
        Assert.IsGreaterThan (0, spelledForFirst, "The layout has the policy spell the name to find the answer.")

        Assert.AreEqual<int>(
            spelledForFirst,
            StoredCountingNamingPolicy<StoredSide>.Spelled,
            "The layout remembers the answer: the policy spelled nothing for the second one."
        )
