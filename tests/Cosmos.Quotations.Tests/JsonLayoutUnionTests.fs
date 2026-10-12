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
/// serializer up. The document has to hold the mark that the layout gives the case, and no mark of another case, and
/// the value of every field at the place that the layout gives it.
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
    /// when each value is written by itself; empty when the layout is right. A union that the serializer refuses has
    /// to be not described.
    /// </summary>
    static let disagreementsAlone (profile : SerializerProfile) : string array =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options

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
        let layout = JsonLayout.ofSerializerOptions options

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
                            // A null that the serializer left out: only the case that is null is written that way.
                            let struct (case, _) = WrittenUnions.caseOf sample.UnionType value

                            match layout.UnionCaseEncoding (ValueSome held, case) with
                            | UnionCaseEncoding.Written (UnionCaseMark.Null, _) -> ()
                            | said ->
                                $"%s{sample.UnionType.Name}.%s{case.Name}: the document does not hold the member, and the layout says %A{said}"
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
                | UnionCaseMark.Tag (path, tag) -> $"tag %A{tag} at value%s{WrittenSteps.shown path}"
                | UnionCaseMark.Property name -> $"property %s{name}"
                | UnionCaseMark.Null -> "null"
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

    /// Every way to have the name of the tag written twice, as a row of a data-driven test.
    static member TagTwiceRows : IEnumerable<TestDataRow<StoredTagTwice>> =
        StoredUnionSamples.tagsTwice
        |> Seq.map (fun twice ->
            TestDataRow<StoredTagTwice>(twice, DisplayName = $"a union must not be described when %s{twice.Reason}")
        )

    /// Every way to set the serializer up: the ones of the layout tests and the ones that write a union differently.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        seq {
            yield! SerializerProfiles.all
            yield! SerializerProfiles.unions
        }
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"the unions must be laid out as they are written by %s{profile.Name}"
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
            seq {
                yield! SerializerProfiles.all
                yield! SerializerProfiles.unions
            }
            |> Seq.collect (fun profile ->
                let layout = JsonLayout.ofSerializerOptions (profile.Create ())

                seq {
                    for sample in StoredUnionSamples.all do
                        for case in FSharpType.GetUnionCases (sample.UnionType, true) do
                            match layout.UnionCaseEncoding (ValueNone, case) with
                            | UnionCaseEncoding.Written (mark, places) ->
                                match mark with
                                | UnionCaseMark.Tag (path, tag) ->
                                    let steps =
                                        path
                                        |> Seq.map (fun step -> if step.IsProperty then "property" else "item")
                                        |> String.concat ", "

                                    let kind =
                                        match tag with
                                        | UnionTag.String _ -> "a string"
                                        | UnionTag.Number _ -> "a number"
                                        | UnionTag.Boolean _ -> "a boolean"

                                    $"mark: %s{kind} at [%s{steps}]"
                                | UnionCaseMark.Property _ -> "mark: a property"
                                | UnionCaseMark.Null -> "mark: null"
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
                "mark: a boolean at []"
                "mark: a boolean at [item]"
                "mark: a boolean at [property]"
                "mark: a number at []"
                "mark: a number at [item]"
                "mark: a number at [property]"
                "mark: a property"
                "mark: a string at []"
                "mark: a string at [item]"
                "mark: a string at [property]"
                "mark: none"
                "mark: null"
                "not described"
            |],
            said,
            "The ways to set the serializer up should have the model written with every kind of mark and with a field at every kind of place."
        )

    [<TestMethod>]
    member _.``A part of the encoding in the attribute of a union must give way to the common encoding`` () =
        // The options of the attribute say that they inherit the encoding, and FSharp.SystemTextJson then takes the
        // whole encoding from the common options: the named fields that the attribute asks for are lost.
        let options = (JsonFSharpOptions.Default().WithAllowOverride()).ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions options

        Assert.AreEqual<string>(
            "{\"kind\":\"Labelled\",\"data\":[\"label\",14]}",
            JsonSerializer.Serialize (Labelled ("label", 14), options),
            "The serializer should take the names of the attribute and the encoding of the common options, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "tag String \"Labelled\" at value[\"kind\"]; fields: value[\"data\"][0], value[\"data\"][1]",
            described (layout.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredLabel> "Labelled")),
            "The layout follows the library: the names of the attribute, the encoding of the common options."
        )

    [<TestMethod>]
    member _.``A union that the serializer refuses must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options

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

    [<TestMethod>]
    member _.``A union with two places for its tag must not be described`` () =
        let options =
            (JsonFSharpOptions
                .Default()
                .WithUnionEncoding(
                    JsonUnionEncoding.ExternalTag
                    ||| JsonUnionEncoding.InternalTag
                ))
                .ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions options

        Assert.Throws<JsonException>(
            Action (fun () -> JsonSerializer.Serialize (Circle 3, options) |> ignore),
            "FSharp.SystemTextJson should fail to write a union whose encoding has two places for the tag, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<StoredShape> "Circle")),
            "A case that the serializer fails to write is not described."
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionTests.TagTwiceRows)>]
    member _.``A union with a case that is written with the name of its tag twice must not be described``
        (twice : StoredTagTwice)
        =
        let options = twice.Settings.ToJsonSerializerOptions ()
        let layout = JsonLayout.ofSerializerOptions options
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
    member _.``A union of a member with a converter of its own must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options
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
    member _.``An option must be described as a union only when it is written as one`` () =
        let some = WrittenUnions.caseNamed typeof<int option> "Some"
        let none = WrittenUnions.caseNamed typeof<int option> "None"
        let unwrapped = JsonLayout.ofSerializerOptions (SerializerProfiles.fsharpSystemTextJson.Create ())
        let asUnion = SerializerProfiles.withoutUnwrappedOptions.Create ()
        let layout = JsonLayout.ofSerializerOptions asUnion

        Assert.AreEqual<string>(
            "not described",
            described (unwrapped.UnionCaseEncoding (ValueNone, some)),
            "An option that is written as its value is described by the question about a value that can be absent."
        )

        Assert.AreEqual<string>(
            "not described",
            described (unwrapped.UnionCaseEncoding (ValueNone, WrittenUnions.caseNamed typeof<Skippable<int>> "Include")),
            "A skippable value is always written as its value."
        )

        Assert.AreEqual<string>(
            "{\"Case\":\"Some\",\"Fields\":[1]}",
            JsonSerializer.Serialize (Some 1, asUnion),
            "The serializer should write the option as a union, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "tag String \"Some\" at value[\"Case\"]; fields: value[\"Fields\"][0]",
            described (layout.UnionCaseEncoding (ValueNone, some)),
            "An option that is written as a union is described as any union."
        )

        Assert.AreEqual<string>(
            "null; fields: ",
            described (layout.UnionCaseEncoding (ValueNone, none)),
            "The case without a value is the null of the option."
        )

    [<TestMethod>]
    member _.``A record in a case must not be described when it is not written as the record is by itself`` () =
        let lent = WrittenUnions.caseNamed typeof<StoredLoan> "Lent"
        let returned = WrittenUnions.caseNamed typeof<StoredLoan> "Returned"

        // The attribute of the union gives the options that unwrap the record, and the record is written with them.
        let ofAttribute = (JsonFSharpOptions.Default().WithAllowOverride()).ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions ofAttribute

        Assert.AreEqual<string>(
            "{\"Case\":\"Lent\",\"Fields\":{\"Name\":\"lender\"}}",
            JsonSerializer.Serialize (Lent { Name = "lender" }, ofAttribute),
            "The serializer should write the members of the record in place of the fields, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (layout.UnionCaseEncoding (ValueNone, lent)),
            "The record is written with the options of the union, which are not the ones that the layout describes the record with. The encoding unwraps a case with one field as well, and the record goes first."
        )

        Assert.AreEqual<string>(
            "tag String \"Returned\" at value[\"Case\"]; fields: ",
            described (layout.UnionCaseEncoding (ValueNone, returned)),
            "A case without such a record is described."
        )

        // The converter of FSharp.SystemTextJson takes unions only, so System.Text.Json writes a record by itself.
        let unionsOnly =
            (JsonFSharpOptions.Default().WithTypes(JsonFSharpTypes.Unions).WithUnionUnwrapRecordCases())
                .ToJsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

        Assert.AreEqual<string>(
            "{\"Case\":\"Signed\",\"Fields\":{\"name\":\"signer\"}}",
            JsonSerializer.Serialize (Signed { Name = "signer" }, unionsOnly),
            "The serializer should write the members of the record in place of the fields, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (
                (JsonLayout.ofSerializerOptions unionsOnly)
                    .UnionCaseEncoding(ValueNone, WrittenUnions.caseNamed typeof<StoredShape> "Signed")
            ),
            "The record by itself is written by System.Text.Json, so what the layout says of its members is not about the record in the case."
        )

        // A record that names the converter of the library in an attribute is written by a converter that the
        // attribute makes, where no converter of the options takes records: without the naming policy of the options,
        // which the union writes the members of the record with.
        let signer = { SignedAs = "signer"; Alias = None }
        let dealt = WrittenUnions.caseNamed typeof<StoredDeal> "Dealt"

        Assert.AreEqual<string>(
            "{\"SignedAs\":\"signer\",\"Alias\":null}",
            JsonSerializer.Serialize (signer, unionsOnly),
            "The converter of the attribute should write the record by itself, without the naming policy, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"Case\":\"Dealt\",\"Fields\":{\"signedAs\":\"signer\",\"alias\":null}}",
            JsonSerializer.Serialize (Dealt signer, unionsOnly),
            "The union should write the members of the record with the naming policy of the options, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described ((JsonLayout.ofSerializerOptions unionsOnly).UnionCaseEncoding(ValueNone, dealt)),
            "The layout names the members of the record as the converter of the attribute writes them, which is not how the case writes them."
        )

        // Another converter of the library among the options writes the records, with other options than the one
        // that writes the unions.
        let twoConverters = JsonSerializerOptions ()

        twoConverters.Converters.Add (
            JsonFSharpConverter (JsonFSharpOptions.Default().WithTypes(JsonFSharpTypes.Unions).WithUnionUnwrapRecordCases())
        )

        twoConverters.Converters.Add (
            JsonFSharpConverter (JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always)
        )

        Assert.AreEqual<string>(
            "{\"SignedAs\":\"signer\"}",
            JsonSerializer.Serialize (signer, twoConverters),
            "The second converter should write the record by itself and leave the option out, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"Case\":\"Dealt\",\"Fields\":{\"SignedAs\":\"signer\",\"Alias\":null}}",
            JsonSerializer.Serialize (Dealt signer, twoConverters),
            "The first converter should write the members of the record in the case with its own options, which write the option, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described ((JsonLayout.ofSerializerOptions twoConverters).UnionCaseEncoding(ValueNone, dealt)),
            "What the layout says of the members of the record is about the record as the second converter writes it."
        )

    [<TestMethod>]
    member _.``A union whose contract a resolver of the caller makes must not be described`` () =
        let low = WrittenUnions.caseNamed typeof<StoredLevel> "Low"

        // The resolver makes the contract from the converter of FSharp.SystemTextJson for the union, which it has made
        // with a naming policy that the options do not have.
        let options =
            JsonSerializerOptions (
                SerializerProfiles.fsharpSystemTextJson.Create (),
                TypeInfoResolver =
                    StoredContractResolver<StoredLevel>(fun options ->
                        JsonMetadataServices.CreateValueInfo<StoredLevel>(
                            options,
                            nonNull (StoredLoudUnionConverterFactory().CreateConverter(typeof<StoredLevel>, options))
                        )
                    )
            )

        Assert.AreEqual<string>(
            "{\"Case\":\"LOW\"}",
            JsonSerializer.Serialize (Low, options),
            "The resolver should have the union written with the converter that it made, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described ((JsonLayout.ofSerializerOptions options).UnionCaseEncoding(ValueNone, low)),
            "The serializer did not make the contract of the union, so the converter among the options does not tell how the union is written."
        )

    [<TestMethod>]
    member _.``A union that a factory made of the one of FSharp.SystemTextJson writes must not be described`` () =
        let options = JsonSerializerOptions ()
        options.Converters.Add (StoredQuietFactory ())

        Assert.AreEqual<string>(
            "{\"Case\":\"Low\"}",
            JsonSerializer.Serialize (Low, options),
            "The factory should write the union, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "not described",
            described (
                (JsonLayout.ofSerializerOptions options)
                    .UnionCaseEncoding(ValueNone, WrittenUnions.caseNamed typeof<StoredLevel> "Low")
            ),
            "Only the factory of FSharp.SystemTextJson itself shows the options that a union is written with."
        )

    [<TestMethod>]
    member _.``A layout must work out how a case is written once`` () =
        let options =
            (JsonFSharpOptions.Default().WithUnionTagNamingPolicy(StoredCountingNamingPolicy ())).ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions options
        let low = WrittenUnions.caseNamed typeof<StoredLevel> "Low"
        let first = layout.UnionCaseEncoding (ValueNone, low)
        let spelledForFirst = StoredCountingNamingPolicy.Spelled
        let second = layout.UnionCaseEncoding (ValueNone, low)

        Assert.AreEqual<string>(
            "tag String \"LOW\" at value[\"Case\"]; fields: ",
            described first,
            "The naming policy of the tags spells the name of the case."
        )

        Assert.AreEqual<string>(described first, described second, "The answer is the same every time.")

        Assert.AreEqual<int>(
            spelledForFirst,
            StoredCountingNamingPolicy.Spelled,
            "The layout has the policy spell the name to find the answer, and remembers the answer: the policy spelled nothing for the second one."
        )
