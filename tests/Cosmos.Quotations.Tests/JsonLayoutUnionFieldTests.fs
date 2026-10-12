namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Reflection

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// A value of a union case as it was written: the values of its fields, and the document.
[<Struct>]
type WrittenCase = {
    /// The values of the fields of the case.
    Values : objnull array
    /// What the serializer wrote for the value.
    Document : JsonElement
}

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> says about a field of a union case as a member of
/// a document, compared with what the serializer writes. A case is written twice with every way to set the serializer
/// up, once with every field that can be without a value holding one and once with none of them, and the two documents
/// show how each field is written without a value. A value in a field that is written in more than one way has to be
/// described as the document has it.
/// </summary>
[<TestClass; JsonLayoutUnitTestCategory>]
type JsonLayoutUnionFieldTests () =

    /// A field of a union case as a member of the union.
    static let fieldOf (unionType : Type) (field : PropertyInfo) : DocumentMember = { Holder = unionType; Member = field }

    /// The kind of the last step to the place of a field: where the field stands among what its case writes.
    static let standsAs (path : ImmutableArray<JsonStep>) : string =
        if path.IsEmpty then
            "the union"
        else
            match path[path.Length - 1] with
            | JsonStep.Property _ -> "a property"
            | JsonStep.Item _ -> "an item"

    /// <summary>
    /// What the two documents of a pair show of every field that has a value in one and none in the other: where the
    /// field stands, and how it is written without a value. The value has to be written as itself in the first
    /// document, or nothing is shown: null where the second document has null at the place of the field, left out
    /// where it has nothing there.
    /// </summary>
    static let shownBy
        (options : JsonSerializerOptions)
        (case : UnionCaseInfo)
        (places : ImmutableArray<UnionFieldPlace>)
        (present : WrittenCase)
        (absent : WrittenCase)
        : struct (PropertyInfo * string * OptionalEncoding) seq = seq {
        let fields = case.GetFields ()

        for index in 0 .. fields.Length - 1 do
            let field = fields[index]

            match
                ModelMembers.unwrapped present.Values[index] field.PropertyType,
                ModelMembers.unwrapped absent.Values[index] field.PropertyType,
                places[index]
            with
            | ValueSome (struct (inner, innerType)), ValueNone, UnionFieldPlace.Value path ->
                let alone = JsonSerializer.SerializeToElement (inner, innerType, options)

                let isItself =
                    WrittenSteps.valueAt path present.Document
                    |> ValueOption.exists (fun found -> found.GetRawText () = alone.GetRawText ())

                let shown =
                    match isItself, WrittenSteps.valueAt path absent.Document with
                    | true, ValueNone -> OptionalEncoding.AlwaysUndefined
                    | true, ValueSome left when left.ValueKind = JsonValueKind.Null -> OptionalEncoding.AlwaysNull
                    | _ -> OptionalEncoding.NotDescribed

                struct (field, standsAs path, shown)
            | _ -> ()
    }

    /// <summary>
    /// Everything the layout says about the fields of the pairs without a value that the documents do not confirm;
    /// empty when the layout is right. A field of a case that the layout does not describe, or of a union that the
    /// serializer refuses, has to be not described.
    /// </summary>
    static let disagreementsAboutAbsence (profile : SerializerProfile) : string array =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options

        seq {
            for pair in StoredUnionSamples.pairs do
                let struct (case, presentValues) = WrittenUnions.caseOf pair.UnionType pair.Present
                let struct (_, absentValues) = WrittenUnions.caseOf pair.UnionType pair.Absent
                let named = $"%s{pair.UnionType.Name}.%s{case.Name}"

                let said (field : PropertyInfo) : OptionalEncoding =
                    layout.OptionalEncoding (ValueSome (fieldOf pair.UnionType field), field.PropertyType)

                let written =
                    WrittenUnions.writtenAll
                        options
                        pair.UnionType
                        (seq {
                            pair.Present
                            pair.Absent
                        })

                match written, layout.UnionCaseEncoding (ValueNone, case) with
                | ValueSome documents, UnionCaseEncoding.Written (_, places) when places.Length = presentValues.Length ->
                    let struct (_, presentDocument) = documents[0]
                    let struct (_, absentDocument) = documents[1]

                    for struct (field, _, shown) in
                        shownBy options case places { Values = presentValues; Document = presentDocument } {
                            Values = absentValues
                            Document = absentDocument
                        } do
                        if said field <> shown then
                            $"%s{named}.%s{field.Name}: the layout says %A{said field} of it without a value, the documents %A{shown} (%s{absentDocument.GetRawText ()})"
                | _ ->
                    for field in case.GetFields () do
                        if not (said field).IsNotDescribed then
                            $"%s{named}.%s{field.Name}: the layout does not describe the case or the serializer refuses the union, and the layout says %A{said field} of the field"
        }
        |> Seq.toArray

    /// <summary>
    /// Everything the layout says about the values in the fields of the unions that the documents do not confirm;
    /// empty when the layout is right: an enumeration, a tuple, a collection and a map by the kind of JSON value
    /// that the document has at the place of the field, and a union of the model by the mark of its case.
    /// </summary>
    static let disagreementsAboutValues (profile : SerializerProfile) : string array =
        let options = profile.Create ()
        let layout = JsonLayout.ofSerializerOptions options
        let modelAssembly = typeof<StoredParcel>.Assembly

        seq {
            for sample in StoredUnionSamples.all do
                match WrittenUnions.writtenAll options sample.UnionType sample.Values with
                | ValueSome documents ->
                    for struct (value, document) in documents do
                        let struct (case, values) = WrittenUnions.caseOf sample.UnionType value
                        let fields = case.GetFields ()

                        match layout.UnionCaseEncoding (ValueNone, case) with
                        | UnionCaseEncoding.Written (_, places) when places.Length = fields.Length ->
                            for index in 0 .. fields.Length - 1 do
                                let field = fields[index]
                                let held = ValueSome (fieldOf sample.UnionType field)
                                let named = $"%s{sample.UnionType.Name}.%s{case.Name}.%s{field.Name}"

                                match places[index], values[index] with
                                | UnionFieldPlace.Value _, null
                                | UnionFieldPlace.Members _, _ -> ()
                                | UnionFieldPlace.Value path, fieldValue ->
                                    match WrittenSteps.valueAt path document with
                                    | ValueSome found ->
                                        yield! WrittenValues.disagreementsAbout layout named held field.PropertyType found

                                        // A union of the model in a field is written as the union is anywhere.
                                        if
                                            FSharpType.IsUnion (field.PropertyType, true)
                                            && field.PropertyType.Assembly.Equals modelAssembly
                                        then
                                            yield!
                                                WrittenUnions.disagreementsAbout
                                                    layout
                                                    options
                                                    held
                                                    field.PropertyType
                                                    fieldValue
                                                    found
                                    | ValueNone -> ()
                        | _ -> ()
                | ValueNone -> ()
        }
        |> Seq.toArray

    /// Every way to set the serializer up: the ones of the layout tests and the ones that write a union differently.
    static member ProfileRows : IEnumerable<TestDataRow<SerializerProfile>> =
        seq {
            yield! SerializerProfiles.all
            yield! SerializerProfiles.unions
        }
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(
                profile,
                DisplayName = $"the fields of the cases must be laid out as they are written by %s{profile.Name}"
            )
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionFieldTests.ProfileRows)>]
    member _.``A field of a union case without a value must be laid out as the serializer writes it``
        (profile : SerializerProfile)
        =
        let found = disagreementsAboutAbsence profile
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of a field without a value what the two documents of its case show: null, left out, or nothing where the value is not written as itself.\n%s{listed}"
        )

    [<TestMethod; DynamicData(nameof JsonLayoutUnionFieldTests.ProfileRows)>]
    member _.``A value in a field of a union case must be laid out as the serializer writes it`` (profile : SerializerProfile) =
        let found = disagreementsAboutValues profile
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (
            found,
            $"The layout should say of a value in a field what it says of the value anywhere, which is what the document has at the place of the field.\n%s{listed}"
        )

    [<TestMethod>]
    member _.``The fields of the model must be written without a value in every way that the layout describes`` () =
        // The tests above compare the layout with the documents they find. This one checks that the documents are of
        // every kind: a model whose fields the serializer only ever wrote as null would agree with a layout that
        // knows nothing else.
        let seen =
            seq {
                yield! SerializerProfiles.all
                yield! SerializerProfiles.unions
            }
            |> Seq.collect (fun profile ->
                let options = profile.Create ()
                let layout = JsonLayout.ofSerializerOptions options

                seq {
                    for pair in StoredUnionSamples.pairs do
                        let struct (case, presentValues) = WrittenUnions.caseOf pair.UnionType pair.Present
                        let struct (_, absentValues) = WrittenUnions.caseOf pair.UnionType pair.Absent

                        let written =
                            WrittenUnions.writtenAll
                                options
                                pair.UnionType
                                (seq {
                                    pair.Present
                                    pair.Absent
                                })

                        match written, layout.UnionCaseEncoding (ValueNone, case) with
                        | ValueSome documents, UnionCaseEncoding.Written (_, places) when places.Length = presentValues.Length ->
                            let struct (_, presentDocument) = documents[0]
                            let struct (_, absentDocument) = documents[1]

                            for struct (_, stands, shown) in
                                shownBy options case places { Values = presentValues; Document = presentDocument } {
                                    Values = absentValues
                                    Document = absentDocument
                                } do
                                $"%s{stands}: %A{shown}"
                        | _ -> ()
                }
            )
            |> Seq.distinct
            |> Seq.sort
            |> Seq.toArray

        CollectionAssert.AreEqual (
            [|
                "a property: AlwaysNull"
                "a property: AlwaysUndefined"
                "a property: NotDescribed"
                "an item: AlwaysNull"
                "an item: NotDescribed"
                "the union: AlwaysNull"
                "the union: NotDescribed"
            |],
            seen,
            "The ways to set the serializer up should have a field without a value written as null in every place a field can stand, left out where it is a property, and written as something else than itself in each of them."
        )

    [<TestMethod>]
    member _.``A record whose members a case writes must not be described without a value`` () =
        // The case writes the members of the record in place of its fields, and the library fails on a record that
        // is null there, so no document shows one.
        let options = (JsonFSharpOptions.Default().WithUnionUnwrapRecordCases()).ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions options
        let signed = WrittenUnions.caseNamed typeof<StoredShape> "Signed"
        let author = signed.GetFields()[0]

        Assert.AreEqual<string>(
            "{\"Case\":\"Signed\",\"Fields\":{\"Name\":\"signer\"}}",
            JsonSerializer.Serialize (Signed { Name = "signer" }, options),
            "The serializer should write the members of the record in place of the fields, or the test checks nothing."
        )

        Assert.Throws<NullReferenceException>(
            Action (fun () ->
                JsonSerializer.Serialize (Signed (Unchecked.defaultof<StoredAuthor>), options)
                |> ignore
            ),
            "The library should fail to write the record when it is null, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (fieldOf typeof<StoredShape> author), typeof<StoredAuthor>),
            "No document holds the record without a value, so the layout should not say how one is written."
        )

        // Next to a tag that is among the fields, the members of the record are properties of the union itself.
        let amongFields =
            (JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionUnwrapRecordCases()).ToJsonSerializerOptions()

        Assert.AreEqual<string>(
            "{\"Case\":\"Signed\",\"Name\":\"signer\"}",
            JsonSerializer.Serialize (Signed { Name = "signer" }, amongFields),
            "The serializer should write the members of the record next to the tag, or the test checks nothing."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            (JsonLayout.ofSerializerOptions amongFields)
                .OptionalEncoding(ValueSome (fieldOf typeof<StoredShape> author), typeof<StoredAuthor>),
            "The record is no property there that could be null or left out."
        )

    [<TestMethod>]
    member _.``A member of a union that is no field of a case must not be described`` () =
        let options = SerializerProfiles.fsharpSystemTextJson.Create ()
        let layout = JsonLayout.ofSerializerOptions options

        let memberOf (name : string) : DocumentMember = {
            Holder = typeof<StoredParcel>
            Member = nonNull (typeof<StoredParcel>.GetProperty name)
        }

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.AlwaysNull,
            layout.OptionalEncoding (
                ValueSome (
                    fieldOf typeof<StoredParcel> ((WrittenUnions.caseNamed typeof<StoredParcel> "WithSender").GetFields()[0])
                ),
                typeof<string>
            ),
            "A field of a case is described, or the test checks nothing."
        )

        Assert.AreEqual<OptionalEncoding>(
            OptionalEncoding.NotDescribed,
            layout.OptionalEncoding (ValueSome (memberOf "Remark"), typeof<string>),
            "The library writes the fields of a case and no other property of the union."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.NotDescribed,
            layout.EnumEncoding (ValueSome (memberOf "Shade"), typeof<StoredColour>),
            "So a value in such a property is not described either."
        )

        Assert.AreEqual<EnumEncoding>(
            EnumEncoding.Number,
            layout.EnumEncoding (
                ValueSome (fieldOf typeof<StoredParcel> ((WrittenUnions.caseNamed typeof<StoredParcel> "Listed").GetFields()[0])),
                typeof<StoredColour>
            ),
            "While a value in a field of a case is."
        )

    [<TestMethod>]
    member _.``A field of a union case must have no name of its own`` () =
        let options = (JsonFSharpOptions.Default().WithUnionNamedFields()).ToJsonSerializerOptions()

        let layout = JsonLayout.ofSerializerOptions options
        let packed = WrittenUnions.caseNamed typeof<StoredParcel> "Packed"
        let note = packed.GetFields()[0]

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (fieldOf typeof<StoredParcel> note),
            "The place of a field is what the layout says of its case, which is not always a property of the union."
        )

        match layout.UnionCaseEncoding (ValueNone, packed) with
        | UnionCaseEncoding.Written (_, places) ->
            Assert.AreEqual<UnionFieldPlace>(
                UnionFieldPlace.Value (ImmutableArray.Create (JsonStep.Property "Fields", JsonStep.Property "note")),
                places[0],
                "The name of the field is the last step to its place."
            )
        | UnionCaseEncoding.NotDescribed -> Assert.Fail "The case should be described."
