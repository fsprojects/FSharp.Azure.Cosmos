namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
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
}

/// <summary>
/// The ways to set up the serializer that the layout supports, and some that it refuses. The tests write the model
/// with every supported way and compare the layout with the documents.
/// </summary>
module SerializerProfiles =

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
    }

    /// FSharp.SystemTextJson as it is made.
    let fsharpDefault = fsharp "FSharp.SystemTextJson with its defaults" id ignore

    /// <summary>
    /// The options of FSharp.SystemTextJson that the application of the maintainer writes its documents with: an
    /// internal tag named <c>$type</c>, named fields, a record in a case unwrapped, options that are always skipped,
    /// and maps as objects.
    /// </summary>
    let private productionOptions : JsonFSharpOptions =
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

    /// The set-up of that application, with camel case names and enumerations by name.
    let production =
        fsharp
            "the options of the application: an internal tag, camel case names and enumerations by name"
            (fun _ -> productionOptions)
            (fun options ->
                options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
                options.Converters.Add (JsonStringEnumConverter ())
            )

    /// Every way to set the serializer up that the layout supports.
    let supported =
        seq {
            fsharpDefault
            fsharp
                "FSharp.SystemTextJson with camel case names"
                id
                (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase)
            fsharp
                "FSharp.SystemTextJson with snake case names that includes fields"
                id
                (fun options ->
                    options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
                    options.IncludeFields <- true
                )
            fsharp "FSharp.SystemTextJson that includes the properties of records" _.WithIncludeRecordProperties() ignore
            fsharp
                "FSharp.SystemTextJson that reads numbers from texts"
                id
                (fun options -> options.NumberHandling <- JsonNumberHandling.AllowReadingFromString)
            fsharp
                "FSharp.SystemTextJson that writes enumerations by name"
                id
                (fun options -> options.Converters.Add (JsonStringEnumConverter ()))
            fsharp "FSharp.SystemTextJson that writes every map as an object" _.WithMapFormat(MapFormat.Object) ignore
            fsharp
                "FSharp.SystemTextJson that writes every map as an array of pairs"
                _.WithMapFormat(MapFormat.ArrayOfPairs)
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag next to the fields, which have names"
                _.WithUnionNamedFields()
                ignore
            fsharp
                "FSharp.SystemTextJson that writes a case without fields as its tag, with a naming policy for the tags"
                _.WithUnionTagNamingPolicy(JsonNamingPolicy.SnakeCaseUpper).WithUnionUnwrapFieldlessTags()
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the members of a record in place of the fields"
                _.WithUnionEncoding(
                    JsonUnionEncoding.AdjacentTag
                    ||| JsonUnionEncoding.UnwrapRecordCases
                    ||| JsonUnionEncoding.UnwrapOption
                    ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                )
                ignore
            fsharp
                "FSharp.SystemTextJson with names of its own for the tag and the fields of a union"
                _.WithUnionTagName("kind").WithUnionFieldsName("data").WithUnionNamedFields()
                ignore
            fsharp
                "FSharp.SystemTextJson with a naming policy for the fields of a union and camel case names"
                _.WithUnionNamedFields().WithUnionFieldNamingPolicy(JsonNamingPolicy.SnakeCaseUpper)
                (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase)
            fsharp
                "FSharp.SystemTextJson that writes the tag before the fields"
                _.WithUnionEncoding(
                    JsonUnionEncoding.InternalTag
                    ||| JsonUnionEncoding.UnwrapOption
                    ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                )
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag among the fields, which have names"
                _.WithUnionInternalTag().WithUnionNamedFields()
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag among the fields under the name that the fields would have"
                _.WithUnionInternalTag().WithUnionNamedFields().WithUnionTagName("Fields")
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag next to the fields under the name of a field"
                _.WithUnionNamedFields().WithUnionTagName("radius")
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag before the fields under the name of a field"
                _
                    .WithUnionEncoding(
                        JsonUnionEncoding.InternalTag
                        ||| JsonUnionEncoding.UnwrapOption
                        ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                    )
                    .WithUnionTagName("radius")
                ignore
            fsharp
                "FSharp.SystemTextJson that writes the tag among the members of a record"
                _.WithUnionEncoding(
                    JsonUnionEncoding.InternalTag
                    ||| JsonUnionEncoding.UnwrapRecordCases
                    ||| JsonUnionEncoding.UnwrapOption
                    ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                )
                ignore
            fsharp
                "FSharp.SystemTextJson that does not write a union of one case as its value"
                _.WithUnionEncoding(
                    JsonUnionEncoding.AdjacentTag
                    ||| JsonUnionEncoding.UnwrapOption
                )
                ignore
            production
            fsharp
                "the options of the application with snake case names"
                (fun _ -> productionOptions)
                (fun options ->
                    options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
                    options.Converters.Add (JsonStringEnumConverter ())
                )
        }
        |> ImmutableArray.CreateRange

    /// The supported way to set the serializer up whose name has a part.
    let named (part : string) : SerializerProfile =
        supported
        |> Seq.find (fun profile -> profile.Name.Contains (part, StringComparison.Ordinal))

    /// <summary>
    /// A way to set the serializer up that the layout refuses, with a part of the reason that the layout gives. The
    /// options come from a function, because a test may change what it gets.
    /// </summary>
    [<Struct>]
    type Refusal = {
        /// What is special about the options; the name of a test result holds it.
        Name : string
        /// Makes the options.
        Create : unit -> JsonSerializerOptions
        /// A part of the sentence that says why.
        Reason : string
    }

    /// FSharp.SystemTextJson with a change of the encoding of its unions.
    let private encoded (encoding : JsonUnionEncoding) : unit -> JsonSerializerOptions =
        fun () -> (JsonFSharpOptions.Default().WithUnionEncoding encoding).ToJsonSerializerOptions()

    /// Every way to set the serializer up that the layout refuses.
    let refused =
        seq {
            {
                Name = "System.Text.Json alone"
                Create = fun () -> JsonSerializerOptions ()
                Reason = "FSharp.SystemTextJson is not among the converters"
            }
            {
                Name = "two converters of FSharp.SystemTextJson"
                Create =
                    fun () ->
                        let options = JsonSerializerOptions ()
                        options.Converters.Add (
                            JsonFSharpConverter (JsonFSharpOptions.Default().WithTypes JsonFSharpTypes.Unions)
                        )
                        options.Converters.Add (JsonFSharpConverter (JsonFSharpOptions.Default ()))
                        options
                Reason = "More than one converter of FSharp.SystemTextJson"
            }
            {
                Name = "a converter that is derived from the one of FSharp.SystemTextJson"
                Create =
                    fun () ->
                        let options = JsonSerializerOptions ()
                        options.Converters.Add (StoredDerivedFactory ())
                        options
                Reason = "an application derived from it"
            }
            {
                Name = "FSharp.SystemTextJson without its converters of tuples"
                Create =
                    fun () ->
                        (JsonFSharpOptions.Default().WithTypes(JsonFSharpTypes.All &&& ~~~JsonFSharpTypes.Tuples))
                            .ToJsonSerializerOptions()
                Reason = "JsonFSharpTypes.All"
            }
            {
                Name = "FSharp.SystemTextJson that does not write an option as its value"
                Create = encoded JsonUnionEncoding.AdjacentTag
                Reason = "UnwrapOption is not set"
            }
            {
                Name = "FSharp.SystemTextJson that writes the tag as the name of the fields"
                Create =
                    encoded (
                        JsonUnionEncoding.ExternalTag
                        ||| JsonUnionEncoding.UnwrapOption
                    )
                Reason = "ExternalTag and Untagged"
            }
            {
                Name = "FSharp.SystemTextJson that writes a union without a tag"
                Create =
                    encoded (
                        JsonUnionEncoding.Untagged
                        ||| JsonUnionEncoding.UnwrapOption
                    )
                Reason = "ExternalTag and Untagged"
            }
            {
                Name = "FSharp.SystemTextJson with two places for the tag"
                Create =
                    encoded (
                        JsonUnionEncoding.ExternalTag
                        ||| JsonUnionEncoding.InternalTag
                        ||| JsonUnionEncoding.UnwrapOption
                    )
                Reason = "ExternalTag and Untagged"
            }
            {
                Name = "FSharp.SystemTextJson that writes the one field of a case in place of the fields"
                Create =
                    encoded (
                        JsonUnionEncoding.AdjacentTag
                        ||| JsonUnionEncoding.UnwrapOption
                        ||| JsonUnionEncoding.UnwrapSingleFieldCases
                    )
                Reason = "UnwrapSingleFieldCases"
            }
            {
                Name = "FSharp.SystemTextJson that names the fields of a union after their types"
                Create =
                    encoded (
                        JsonUnionEncoding.AdjacentTag
                        ||| JsonUnionEncoding.UnwrapOption
                        ||| JsonUnionEncoding.NamedFields
                        ||| JsonUnionEncoding.UnionFieldNamesFromTypes
                    )
                Reason = "UnionFieldNamesFromTypes"
            }
            {
                Name = "FSharp.SystemTextJson that writes the tag and the fields under one name"
                Create =
                    fun () ->
                        (JsonFSharpOptions.Default().WithUnionTagName("data").WithUnionFieldsName "data")
                            .ToJsonSerializerOptions()
                Reason = "under one name"
            }
            {
                Name = "options that handle references"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.ReferenceHandler <- ReferenceHandler.Preserve
                        options
                Reason = "handle references"
            }
            {
                Name = "options that write a cycle as null"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.ReferenceHandler <- ReferenceHandler.IgnoreCycles
                        options
                Reason = "handle references"
            }
            {
                Name = "options that write numbers as texts"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.NumberHandling <- JsonNumberHandling.WriteAsString
                        options
                Reason = "numbers as texts"
            }
            {
                Name = "options that write the numbers that are not finite as texts"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.NumberHandling <- JsonNumberHandling.AllowNamedFloatingPointLiterals
                        options
                Reason = "not finite"
            }
            {
                Name = "options that ignore read-only properties"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.IgnoreReadOnlyProperties <- true
                        options
                Reason = "read-only properties"
            }
            {
                Name = "options that ignore read-only fields"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.IgnoreReadOnlyFields <- true
                        options
                Reason = "read-only fields"
            }
            {
                Name = "options with a policy for the keys of dictionaries"
                Create =
                    fun () ->
                        let options = JsonFSharpOptions.Default().ToJsonSerializerOptions()
                        options.DictionaryKeyPolicy <- JsonNamingPolicy.CamelCase
                        options
                Reason = "keys of dictionaries"
            }
        }
        |> ImmutableArray.CreateRange

/// <summary>
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> of
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.JsonLayout.ofSerializerOptions(System.Text.Json.JsonSerializerOptions)"/>:
/// what the layout says about a member is what the serializer writes for it. The serializer is the oracle: every type
/// of the model is written with every way to set the serializer up that the layout supports, and the document is
/// searched for each member.
/// </summary>
[<TestClass; JsonLayoutUnitTestCategory>]
type JsonLayoutTests () =

    /// <summary>
    /// The text or the number that a value comes down to when the options and value options around it are taken off;
    /// no value for a value that is absent at any level, and for a value of another kind.
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

            // A union is found by what the one field of its case holds: an option and a value option are such unions.
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
    /// Everything the layout says about the names of the members of a sample that the document of the sample does not
    /// confirm; empty when the layout is right.
    /// </summary>
    static let disagreements (profile : SerializerProfile) (sample : StoredSample) : string array =
        let options = profile.Create ()
        let layout = LayoutAssert.WantLayout options
        let documentType = sample.Value.GetType ()
        let written = JsonSerializer.SerializeToElement (sample.Value, documentType, options)

        let scalars =
            ModelMembers.ofType documentType
            |> Seq.map (fun dataMember -> struct (dataMember, scalarOf (ModelMembers.valueOf sample.Value dataMember)))
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
                    let writtenName = propertyHolding scalar written

                    // A member that is never written and declares a name stands for a property that something else
                    // writes. Its name is no fact of these documents, and tests of their own state it.
                    let isDeclaredOnly =
                        writtenName.IsNone
                        && dataMember.IsDefined (typeof<JsonPropertyNameAttribute>, false)

                    let name = layout.MemberName { Holder = documentType; Member = dataMember }

                    if not isDeclaredOnly && name <> writtenName then
                        $"%s{dataMember.Name}: the layout names it %A{name}, the document %A{writtenName}"
                | _ -> ()
        }
        |> Seq.toArray

    /// The layout of the options of a profile.
    static let layoutOf (profile : SerializerProfile) : IJsonLayout = LayoutAssert.WantLayout (profile.Create ())

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

    /// The options of FSharp.SystemTextJson as it is made, with a change.
    static let fsharpOptions (configure : JsonSerializerOptions -> unit) : JsonSerializerOptions =
        let options = SerializerProfiles.fsharpDefault.Create ()
        configure options
        options

    /// Every type of the model with every way to set the serializer up that the layout supports.
    static member SampleRows : IEnumerable<TestDataRow<struct (SerializerProfile * StoredSample)>> = seq {
        for profile in SerializerProfiles.supported do
            for sample in StoredSamples.all do
                TestDataRow<struct (SerializerProfile * StoredSample)>(
                    struct (profile, sample),
                    DisplayName = $"%s{sample.Name} must be laid out as it is written by %s{profile.Name}"
                )
    }

    /// Every way to set the serializer up that the layout supports.
    static member SupportedRows : IEnumerable<TestDataRow<SerializerProfile>> =
        SerializerProfiles.supported
        |> Seq.map (fun profile ->
            TestDataRow<SerializerProfile>(profile, DisplayName = $"the layout must support %s{profile.Name}")
        )

    /// Every way to set the serializer up that the layout refuses.
    static member RefusalRows : IEnumerable<TestDataRow<SerializerProfiles.Refusal>> =
        SerializerProfiles.refused
        |> Seq.map (fun refusal ->
            TestDataRow<SerializerProfiles.Refusal>(refusal, DisplayName = $"the layout must refuse %s{refusal.Name}")
        )

    [<TestMethod; DynamicData(nameof JsonLayoutTests.SampleRows)>]
    member _.``A type must be laid out as the serializer writes it`` (profile : SerializerProfile, sample : StoredSample) =
        let found = disagreements profile sample
        let listed = String.Join ("\n", found)

        Assert.IsEmpty (found, $"The layout should name every member as the document does.\n%s{listed}")

    [<TestMethod; DynamicData(nameof JsonLayoutTests.SupportedRows)>]
    member _.``Options that the layout supports must give a layout`` (profile : SerializerProfile) =
        match JsonLayout.ofSerializerOptions (profile.Create ()) with
        | Result.Ok _ -> ()
        | Result.Error reasons ->
            Assert.Fail $"The layout should support the options, but it refuses them:\n%s{String.Join ('\n', reasons)}"

    [<TestMethod; DynamicData(nameof JsonLayoutTests.RefusalRows)>]
    member _.``Options that the layout does not support must be refused with the reason`` (refusal : SerializerProfiles.Refusal) =
        let reasons = LayoutAssert.WantRefusals (refusal.Create ())

        Assert.IsTrue (
            reasons
            |> Seq.exists (fun reason -> reason.Contains (refusal.Reason, StringComparison.Ordinal)),
            $"A reason should say \"%s{refusal.Reason}\", but the reasons are:\n%s{String.Join ('\n', reasons)}"
        )

    [<TestMethod>]
    member _.``A set-up with one setting that is not supported must be refused for that setting only`` () =
        let refusal =
            SerializerProfiles.refused
            |> Seq.find (fun refusal -> refusal.Name.Contains ("tuples", StringComparison.Ordinal))

        let reasons = LayoutAssert.WantRefusals (refusal.Create ())

        Assert.HasCount (1, reasons, $"Only the setting of the options that is not supported should be named: %A{reasons}")

    [<TestMethod>]
    member _.``A set-up with several settings that are not supported must be refused for each of them`` () =
        // Nothing is left to find out one at a time.
        let options = JsonSerializerOptions (ReferenceHandler = ReferenceHandler.Preserve)

        options.NumberHandling <-
            JsonNumberHandling.WriteAsString
            ||| JsonNumberHandling.AllowNamedFloatingPointLiterals

        options.IgnoreReadOnlyProperties <- true
        options.IgnoreReadOnlyFields <- true
        options.DictionaryKeyPolicy <- JsonNamingPolicy.CamelCase

        let reasons = LayoutAssert.WantRefusals options

        Assert.HasCount (
            7,
            reasons,
            $"The layout should name every setting that it does not support:\n%s{String.Join ('\n', reasons)}"
        )

    [<TestMethod>]
    member _.``Options that handle references or write numbers that are not finite as texts must change the document`` () =
        // These are the premises of the refusals of those settings: the documents are not what the model says.
        let written (configure : JsonSerializerOptions -> unit) (value : StoredCycle) : string =
            let options = SerializerProfiles.fsharpDefault.Create ()
            configure options
            JsonSerializer.Serialize (value, options)

        let cycle = StoredCycle (Value = 7.5)
        cycle.Next <- cycle

        Assert.AreEqual<string>(
            "{\"Value\":7.5,\"Next\":null}",
            written (fun options -> options.ReferenceHandler <- ReferenceHandler.IgnoreCycles) cycle,
            "A cycle should be written as null, which no member of the model is."
        )

        Assert.AreEqual<string>(
            "{\"$id\":\"1\",\"Value\":7.5,\"Next\":{\"$ref\":\"1\"}}",
            written (fun options -> options.ReferenceHandler <- ReferenceHandler.Preserve) cycle,
            "An object should get the property $id, and a repeated one should be written as $ref."
        )

        let notFinite = StoredCycle (Value = Double.PositiveInfinity)

        Assert.AreEqual<string>(
            "{\"Value\":\"Infinity\",\"Next\":null}",
            written (fun options -> options.NumberHandling <- JsonNumberHandling.AllowNamedFloatingPointLiterals) notFinite,
            "A number that is not finite should be written as a text, which a number of the model never is."
        )

    [<TestMethod>]
    member _.``The model must be found in its documents member by member`` () =
        // The test above compares the layout with what it finds in a document. This one checks that it finds
        // something: a search that found nothing would agree with a layout that says nothing.
        let document =
            JsonSerializer.SerializeToElement (
                StoredSamples.entry.Value,
                typeof<StoredEntry>,
                SerializerProfiles.fsharpDefault.Create ()
            )

        Assert.AreEqual<string voption>(ValueSome "Title", propertyHolding "title-1" document, "A text is found by its value.")
        Assert.AreEqual<string voption>(ValueSome "Count", propertyHolding 31 document, "A number is found by its value.")

        Assert.AreEqual<string voption>(
            ValueSome "Choice",
            propertyHolding 32 document,
            "The value of an option is found where the option is written as its value."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            propertyHolding "draft-1" document,
            "A field that is never written is not found."
        )

    [<TestMethod>]
    member _.``A member that is never written must have the name it declares`` () =
        let layout = layoutOf SerializerProfiles.fsharpDefault

        Assert.AreEqual<string voption>(
            ValueSome "$type",
            layout.MemberName (propertyOf typeof<StoredPerson> "Discriminator"),
            "A property of a class that declares its name stands for a property that something else writes."
        )

        Assert.AreEqual<string voption>(
            ValueSome "$type",
            layout.MemberName (propertyOf typeof<StoredEntry> "Discriminator"),
            "So does a field of a record."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPerson> "Secret"),
            "A property that is never written and declares no name is in no document."
        )

    [<TestMethod>]
    member _.``A member that a derived class overrides must have the name of each declaration`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        let document =
            JsonSerializer.SerializeToElement (StoredSamples.person.Value, typeof<StoredPerson>, options)

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
        let layout = layoutOf SerializerProfiles.fsharpDefault

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
        Assert.AreEqual<string voption>(
            ValueNone,
            (layoutOf SerializerProfiles.fsharpDefault).MemberName(propertyOf typeof<StoredPerson> "Extra"),
            "Its entries are written as properties of the document itself, and it is not."
        )

    [<TestMethod>]
    member _.``A member of a record that is not written must be laid out as one that something else writes`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        let written =
            JsonSerializer.SerializeToElement (StoredSamples.entry.Value, typeof<StoredEntry>, options)

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
    member _.``A member of a type that the serializer refuses must have no name`` () =
        Assert.AreEqual<string voption>(
            ValueNone,
            (layoutOf SerializerProfiles.fsharpDefault).MemberName(propertyOf typeof<StoredRefused> "Id"),
            "The serializer writes no value of the type, so no document holds a member of it."
        )

    [<TestMethod>]
    member _.``A type that the serializer has no contract for must not be described`` () =
        // The serializer refuses a ref struct, and a type that the resolver of the application has no contract for.
        let refStruct = nonNull (Type.GetType "System.TypedReference")

        Assert.AreEqual<PolymorphismEncoding>(
            PolymorphismEncoding.NotDescribed,
            (layoutOf SerializerProfiles.fsharpDefault).Polymorphism refStruct,
            "The serializer makes no contract of a ref struct."
        )

        let bare = SerializerProfiles.fsharpDefault.Create ()
        bare.TypeInfoResolver <- StoredBareResolver ()
        let layout = LayoutAssert.WantLayout bare

        Assert.Throws<NotSupportedException>(
            Action (fun () ->
                JsonSerializer.Serialize (StoredSamples.person.Value, typeof<StoredPerson>, bare)
                |> ignore
            ),
            "The serializer should refuse a type that the resolver has no contract for, or the test checks nothing."
        )
        |> ignore

        Assert.AreEqual<PolymorphismEncoding>(
            PolymorphismEncoding.NotDescribed,
            layout.Polymorphism typeof<StoredPerson>,
            "The serializer has no contract of the type, so nothing is known about it."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPerson> "Name"),
            "So no document holds a member of it."
        )

    [<TestMethod>]
    member _.``A member with a converter of its own must have its name`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        let written = JsonSerializer.SerializeToElement (StoredMeeting (Topic = "topic-1"), options)

        Assert.AreEqual<string>("topic-1", written.GetProperty("Topic").GetString() |> nonNull, "The member should be written.")

        Assert.AreEqual<string voption>(
            ValueSome "Topic",
            layout.MemberName (propertyOf typeof<StoredMeeting> "Topic"),
            "The converter of the member decides what is written under the name, and not the name."
        )

    [<TestMethod>]
    member _.``A member of a type that another converter writes must have no name`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        options.Converters.Insert (0, StoredPointAsTextConverter ())
        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredPoint> "X"),
            "The converter writes the point as a text, which has no members, also when the field declares a name."
        )

    [<TestMethod>]
    member _.``A member of a type that is no object and no record must have no name`` () =
        let layout = layoutOf SerializerProfiles.fsharpDefault

        for struct (holder, name) in
            seq {
                struct (typeof<StoredShape>, "Tag")
                struct (typeof<int list>, "Length")
                struct (typeof<string option>, "Value")
            } do
            Assert.AreEqual<string voption>(
                ValueNone,
                layout.MemberName (propertyOf holder name),
                $"%s{holder.Name} is written by a converter of FSharp.SystemTextJson as a union or a list, and its property %s{name} is in no document."
            )

    [<TestMethod>]
    member _.``A record whose representation is private must be laid out as the serializer writes it`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string>(
            "{\"Code\":\"key-1\"}",
            JsonSerializer.Serialize (StoredKey.Of "key-1", options),
            "The library should write the field of the record, whatever its accessibility, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Code",
            layout.MemberName (propertyOf typeof<StoredKey> "Code"),
            "The field is written under its name, although the representation of the record is private."
        )

    [<TestMethod>]
    member _.``A record whose members are written under one name must not be described`` () =
        let camel = fsharpOptions (fun options -> options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase)
        let plain = SerializerProfiles.fsharpDefault.Create ()
        let layoutPlain = LayoutAssert.WantLayout plain
        let layoutCamel = LayoutAssert.WantLayout camel

        // The library writes both members, and a document holds the property twice.
        Assert.AreEqual<string>(
            "{\"title\":\"a\",\"title\":\"b\"}",
            JsonSerializer.Serialize (({ Title = "a"; title = "b" } : StoredTwin), camel),
            "The policy should make one name of two, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"same\":1,\"same\":2}",
            JsonSerializer.Serialize (({ First = 1; Second = 2 } : StoredNamedTwice), plain),
            "Two declared names should be one, or the test checks nothing."
        )

        Assert.AreEqual<string>(
            "{\"Total\":4,\"Total\":5}",
            JsonSerializer.Serialize (({ Total = 4 } : StoredClash), plain),
            "A property that asks to be written should have the name of a field, or the test checks nothing."
        )

        for struct (holder, name, layout) in
            seq {
                struct (typeof<StoredTwin>, "Title", layoutCamel)
                struct (typeof<StoredTwin>, "title", layoutCamel)
                struct (typeof<StoredNamedTwice>, "First", layoutPlain)
                struct (typeof<StoredNamedTwice>, "Second", layoutPlain)
                struct (typeof<StoredClash>, "Total", layoutPlain)
                struct (typeof<StoredClash>, "Sum", layoutPlain)
            } do
            Assert.AreEqual<string voption>(
                ValueNone,
                layout.MemberName (propertyOf holder name),
                $"A query cannot tell which of two properties of one name it reads, so %s{holder.Name}.%s{name} has no name."
            )

        // Names that differ are told apart, also when they differ by the case of a letter only.
        Assert.AreEqual<string voption>(
            ValueSome "Title",
            layoutPlain.MemberName (propertyOf typeof<StoredTwin> "Title"),
            "Without a policy the first field is named as it is."
        )

        Assert.AreEqual<string voption>(
            ValueSome "title",
            layoutPlain.MemberName (propertyOf typeof<StoredTwin> "title"),
            "And so is the second one: a property name is compared exactly."
        )

    [<TestMethod>]
    member _.``A member that is never written must not make the name of another one collide`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string>(
            "{\"Visible\":1}",
            JsonSerializer.Serialize (({ Visible = 1; Hidden = 2 } : StoredHidden), options),
            "The library should write the first field only, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Visible",
            layout.MemberName (propertyOf typeof<StoredHidden> "Visible"),
            "The property is written once, so the record is described."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Visible",
            layout.MemberName (propertyOf typeof<StoredHidden> "Hidden"),
            "A member that is never written has the name it declares."
        )

    [<TestMethod>]
    member _.``A member that an override never writes must have no name when the override declares none`` () =
        let layout = layoutOf SerializerProfiles.fsharpDefault

        Assert.AreEqual<string voption>(
            ValueSome "$tag",
            layout.MemberName (propertyOf typeof<StoredTagged> "Tag"),
            "The declaration of the base class declares the name of a property that something else writes."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredTaggedAgain> "Tag"),
            "The override declares none, and the serializer does not take the name of the base class for it."
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
            (layoutOf SerializerProfiles.fsharpDefault).MemberName inherited,
            "The serializer writes the derived class as an object, with the property that it inherits."
        )

        let options = SerializerProfiles.fsharpDefault.Create ()
        options.Converters.Add (StoredTalkAsTextConverter ())
        let layout = LayoutAssert.WantLayout options

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

    [<TestMethod>]
    member _.``A member that a derived class overrides must be laid out as the override when it is read from that class`` () =
        let layout = layoutOf SerializerProfiles.fsharpDefault

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

        // A quotation that the compiler stores names the override itself.
        let quoted = memberReadBy <@ fun (person : StoredPerson) -> person.Remark @>

        Assert.AreEqual<Type | null>(
            typeof<StoredPerson>,
            quoted.Member.DeclaringType,
            "The quotation of compiled code names the property as the derived class declares it."
        )

        Assert.AreEqual<string voption>(ValueSome "Remark", layout.MemberName quoted, "The override is named the same way.")

    [<TestMethod>]
    member _.``A record that names a converter in an attribute must be described when a converter of the options writes it`` () =
        // The serializer asks the converters of the options before the attribute of a type.
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string>(
            """{"From":3,"To":5,"Note":null}""",
            JsonSerializer.Serialize ({ From = 3; To = 5; Note = None }, options),
            "The converter of the options should write the record, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueSome "From",
            layout.MemberName (propertyOf typeof<StoredSpan> "From"),
            "FSharp.SystemTextJson writes the record, whatever converter the record names."
        )

    [<TestMethod>]
    member _.``A record whose converter stands among the options by itself must not be described`` () =
        // The converter is made with options of its own, which nothing lets a layout read.
        let options = SerializerProfiles.fsharpDefault.Create ()

        options.Converters.Insert (
            0,
            JsonRecordConverter<StoredBox<int>>(
                options,
                JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always
            )
        )

        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string>(
            """{"Item":72}""",
            JsonSerializer.Serialize (({ Item = 72; Label = None } : StoredBox<int>), options),
            "The converter should write the record with its own options, or the test checks nothing."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredBox<int>> "Label"),
            "The rules of the converter are not known, so the layout should not guess the name."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Name",
            layout.MemberName (propertyOf typeof<StoredAuthor> "Name"),
            "A record that the converter of FSharp.SystemTextJson writes is still described."
        )

    [<TestMethod>]
    member _.``A record that has options of its own must not be described`` () =
        let options =
            (JsonFSharpOptions
                .Default()
                .WithAllowOverride()
                .WithOverrides(fun common ->
                    let overrides = Dictionary<Type, JsonFSharpOptions>()
                    overrides.Add (typeof<StoredAuthor>, common.WithSkippableOptionFields SkippableOptionFields.Never)
                    // For every type that is made of the generic record.
                    overrides.Add (typedefof<StoredBox<_>>, common.WithSkippableOptionFields SkippableOptionFields.Never)
                    overrides :> IDictionary<Type, JsonFSharpOptions>
                ))
                .ToJsonSerializerOptions()

        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredAuthor> "Name"),
            "The options of the type are its own, which are not the common ones that the layout reads."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredBox<int>> "Item"),
            "The options are given for every type that is made of the generic record."
        )

        Assert.AreEqual<string voption>(
            ValueNone,
            layout.MemberName (propertyOf typeof<StoredMarked> "Text"),
            "The attribute of the record gives it options, when the common options let an attribute do that."
        )

        Assert.AreEqual<string voption>(
            ValueSome "Title",
            layout.MemberName (propertyOf typeof<StoredEntry> "Title"),
            "A record that has none is described."
        )

        let ignoring = layoutOf SerializerProfiles.fsharpDefault

        Assert.AreEqual<string voption>(
            ValueSome "Text",
            ignoring.MemberName (propertyOf typeof<StoredMarked> "Text"),
            "Where the common options do not let an attribute give options, the attribute is not read, and the record is written with the common options."
        )

    [<TestMethod>]
    member _.``A record whose contract a resolver of the caller makes must not be described`` () =
        let author : StoredAuthor = { Name = "name-1" }
        let name = propertyOf typeof<StoredAuthor> "Name"

        // The converter of FSharp.SystemTextJson for a record, made with options that nothing shows.
        let quiet () : JsonConverter =
            JsonRecordConverter<StoredAuthor>(
                JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower),
                JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always
            )

        let options = SerializerProfiles.fsharpDefault.Create ()

        options.TypeInfoResolver <-
            StoredContractResolver<StoredAuthor>(fun options ->
                JsonMetadataServices.CreateValueInfo<StoredAuthor>(options, quiet ())
            )

        let layout = LayoutAssert.WantLayout options

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

        // The same resolver leaves every other type to the serializer, as a resolver does that an application derives
        // from the one of the serializer to change some contracts.
        let passing = SerializerProfiles.fsharpDefault.Create ()

        passing.TypeInfoResolver <-
            StoredContractResolver<StoredBox<int>>(fun options ->
                JsonMetadataServices.CreateValueInfo<StoredBox<int>>(
                    options,
                    JsonRecordConverter<StoredBox<int>>(options, JsonFSharpOptions.Default ())
                )
            )

        Assert.AreEqual<string voption>(
            ValueSome "Name",
            (LayoutAssert.WantLayout passing).MemberName name,
            "The serializer made the contract of the record, also as the class that the application derives from its resolver, so the converter among the options tells how the record is written."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a member once`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        options.PropertyNamingPolicy <- StoredCountingNamingPolicy<StoredCounted>()
        let layout = LayoutAssert.WantLayout options
        let title = propertyOf typeof<StoredCounted> "Title"
        let first = layout.MemberName title
        let spelledForFirst = StoredCountingNamingPolicy<StoredCounted>.Spelled
        let second = layout.MemberName title

        Assert.AreEqual<string voption>(ValueSome "Title", first, "The record should be written under the name of its field.")
        Assert.AreEqual<string voption>(first, second, "The answer is the same every time.")

        Assert.AreEqual<int>(
            spelledForFirst,
            StoredCountingNamingPolicy<StoredCounted>.Spelled,
            "The layout has the policy spell the name to find the answer, and remembers the answer: the policy spelled nothing for the second one."
        )

    [<TestMethod>]
    member _.``A layout must ask the serializer about a member once when several threads ask at a time`` () : Task = task {
        let makeLayout () =
            let options = SerializerProfiles.fsharpDefault.Create ()
            options.PropertyNamingPolicy <- StoredGatedNamingPolicy ()
            LayoutAssert.WantLayout options

        let title = propertyOf typeof<StoredGated> "Title"

        // What one thread spells to find the answer, while the gate is open.
        let alone = makeLayout ()
        let spelledBefore = StoredGatedNamingPolicy.Spelled
        alone.MemberName title |> ignore
        let spelledForOne = StoredGatedNamingPolicy.Spelled - spelledBefore

        let layout = makeLayout ()
        let spelledBeforeAll = StoredGatedNamingPolicy.Spelled
        StoredGatedNamingPolicy.Gate.Reset ()

        // Each asks on a thread of its own: the pool is busy with the other tests, and it would start them late.
        let asking =
            Array.init 4 (fun _ -> Task.Factory.StartNew ((fun () -> layout.MemberName title), TaskCreationOptions.LongRunning))

        // The first thread waits inside the policy. The others get the time to come there too, which they would do if
        // every thread worked the answer out for itself.
        let entered =
            SpinWait.SpinUntil ((fun () -> StoredGatedNamingPolicy.Spelled > spelledBeforeAll), TimeSpan.FromSeconds 10.0)

        do! Task.Delay 200
        StoredGatedNamingPolicy.Gate.Set ()
        let! names = Task.WhenAll asking

        Assert.IsTrue (entered, "A thread should have asked the policy to spell the name.")

        Assert.AreEqual<int>(
            spelledForOne,
            StoredGatedNamingPolicy.Spelled - spelledBeforeAll,
            "One thread works the answer out, and the others wait for it: the policy spelled as much as for one thread."
        )

        Assert.IsTrue (names |> Array.forall (fun name -> name = ValueSome "Title"), "Every thread should get the answer.")
    }

    [<TestMethod>]
    member _.``The layout must leave the options it is made of as they are, and not follow a later change of them`` () =
        let options = SerializerProfiles.fsharpDefault.Create ()
        let layout = LayoutAssert.WantLayout options
        let comment = propertyOf typeof<StoredEntry> "Comment"

        Assert.IsFalse (options.IsReadOnly, "The caller may still have to finish the options.")
        Assert.IsNull (options.TypeInfoResolver, "The layout should give the options of the caller no resolver.")

        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

        Assert.AreEqual<string voption>(
            ValueSome "Comment",
            layout.MemberName comment,
            "A change of the options after the layout is made does not reach it, also for a member that it is asked about for the first time."
        )

        Assert.AreEqual<string voption>(
            ValueSome "comment",
            (LayoutAssert.WantLayout options).MemberName comment,
            "A layout that is made after the change has it."
        )

    [<TestMethod>]
    member _.``The layout must ask the resolver that the options have, and leave it and the options as they are`` () =
        // The resolver gives one property another name, which no attribute and no policy says.
        let resolver =
            DefaultJsonTypeInfoResolver()
                .WithAddedModifier(fun (info : JsonTypeInfo) ->
                    for property in info.Properties do
                        if
                            Type.(=) (info.Type, typeof<StoredPerson>)
                            && String.Equals (property.Name, "Note", StringComparison.Ordinal)
                        then
                            property.Name <- "remark"
                )

        let options = fsharpOptions (fun options -> options.TypeInfoResolver <- resolver)
        let layout = LayoutAssert.WantLayout options

        Assert.AreEqual<string voption>(
            ValueSome "remark",
            layout.MemberName (propertyOf typeof<StoredPerson> "Note"),
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
                if Type.(=) (info.Type, typeof<StoredPerson>) then
                    for property in info.Properties do
                        if String.Equals (property.Name, "Note", StringComparison.Ordinal) then
                            property.Name <- "remark"
            )

        let resolver = DefaultJsonTypeInfoResolver ()
        let options = fsharpOptions (fun options -> options.TypeInfoResolver <- resolver)
        let layout = LayoutAssert.WantLayout options

        resolver.Modifiers.Add rename

        Assert.AreEqual<string voption>(
            ValueSome "remark",
            layout.MemberName (propertyOf typeof<StoredPerson> "Note"),
            "A change of the resolver before the layout is asked should reach the layout, as it reaches the serializer."
        )

        Assert.Throws<InvalidOperationException>(
            Action (fun () -> resolver.Modifiers.Add rename),
            "The resolver should take no change after the layout has asked it: a change that reached only the serializer would go unnoticed."
        )
        |> ignore

        let written =
            JsonSerializer.SerializeToElement (StoredSamples.person.Value, typeof<StoredPerson>, options)

        Assert.IsTrue (
            fst (written.TryGetProperty "remark"),
            "The serializer should write the name that the layout gives: both asked one resolver."
        )
