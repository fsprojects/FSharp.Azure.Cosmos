namespace FSharp.Gremlin.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Gremlin

/// A converter of FSharp.SystemTextJson of a class that an application derives from it.
type DerivedFSharpConverter () =
    inherit JsonFSharpConverter (JsonFSharpOptions.Default ())

/// <summary>
/// Checks which options of the serializer <see cref="M:FSharp.Gremlin.GraphModelModule.create(FSharp.Gremlin.GraphModelOptions,System.Text.Json.JsonSerializerOptions)"/>
/// accepts, and the facts of FSharp.SystemTextJson that the graph model depends on.
/// </summary>
/// <remarks>
/// A canary is a test of a fact of FSharp.SystemTextJson that the graph model depends on. When a new version of the
/// library changes the fact, the canary fails, and the model has to change with it.
/// </remarks>
[<TestClass; UnitTestCategory; SerializerProfileTestCategory>]
type SerializerProfileTests () =

    static let refusalsOf (options : GraphModelOptions) (serializerOptions : JsonSerializerOptions) : string array =
        match GraphModel.create options serializerOptions with
        | Ok _ -> [||]
        | Error reasons -> reasons |> Seq.toArray

    static let modelOf (serializerOptions : JsonSerializerOptions) : GraphModel =
        match GraphModel.create GraphModelOptions.Default serializerOptions with
        | Ok model -> model
        | Error reasons ->
            raise (
                AssertFailedException
                    $"""GraphModel.create should accept the options, but it refuses them: {String.Join ("; ", reasons)}"""
            )

    // The contract that the serializer makes for a type. The options get the resolver of the serializer, because only
    // options with a resolver give contracts.
    static let contractOf (options : JsonSerializerOptions) (valueType : Type) : JsonTypeInfo =
        JsonSerializerOptions(options, TypeInfoResolver = DefaultJsonTypeInfoResolver ()).GetTypeInfo valueType

    // The set-ups that the gate refuses, by the names of the rows
    static let refusedSetUp (name : string) : JsonSerializerOptions =
        match name with
        | "no converter" -> JsonSerializerOptions ()
        | "a derived converter" ->
            let options = JsonSerializerOptions ()
            options.Converters.Add (DerivedFSharpConverter ())
            options
        | "two converters" ->
            let options = Profiles.fsharpDefaults ()
            options.Converters.Add (JsonFSharpConverter ())
            options
        | "not every kind of type" ->
            JsonFSharpOptions.Default().WithTypes(JsonFSharpTypes.All &&& ~~~JsonFSharpTypes.Tuples).ToJsonSerializerOptions()
        | "options as unions" ->
            JsonFSharpOptions.Default().WithUnionEncoding(JsonUnionEncoding.AdjacentTag).ToJsonSerializerOptions()
        | "references" -> JsonFSharpOptions.Default().ToJsonSerializerOptions(ReferenceHandler = ReferenceHandler.Preserve)
        | "numbers as texts" ->
            JsonFSharpOptions.Default().ToJsonSerializerOptions(NumberHandling = JsonNumberHandling.WriteAsString)
        | "named numbers" ->
            JsonFSharpOptions
                .Default()
                .ToJsonSerializerOptions(NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)
        | "read-only properties ignored" -> JsonFSharpOptions.Default().ToJsonSerializerOptions(IgnoreReadOnlyProperties = true)
        | "read-only fields ignored" -> JsonFSharpOptions.Default().ToJsonSerializerOptions(IgnoreReadOnlyFields = true)
        | "record properties included" -> JsonFSharpOptions.Default().WithIncludeRecordProperties().ToJsonSerializerOptions()
        | "members overridden" ->
            let members = Dictionary<string, seq<Attribute>> StringComparer.Ordinal
            members.Add ("Name", [| JsonNameAttribute "display" :> Attribute |])
            JsonFSharpOptions.Default().WithOverrideMembers(members).ToJsonSerializerOptions()
        | _ -> invalidArg (nameof name) $"No refused set-up has the name '{name}'."

    [<TestMethod>]
    [<DataRow("defaults")>]
    [<DataRow("camel case")>]
    [<DataRow("snake case")>]
    [<DataRow("SkillGro")>]
    [<DataRow("Ecierge")>]
    member _.``GraphModel.create accepts a set-up of the serializer that the model supports`` (profile : string) =
        Assert.IsEmpty (
            refusalsOf GraphModelOptions.Default (Profiles.named profile),
            $"GraphModel.create should accept the set-up {profile}."
        )

    [<TestMethod>]
    [<DataRow("no converter", "FSharp.SystemTextJson is not among the converters of the options")>]
    [<DataRow("a derived converter", "is of a class that an application derived from it")>]
    [<DataRow("two converters", "More than one converter of FSharp.SystemTextJson")>]
    [<DataRow("not every kind of type", "its options must have JsonFSharpTypes.All")>]
    [<DataRow("options as unions", "UnwrapOption is not set")>]
    [<DataRow("references", "The options handle references")>]
    [<DataRow("numbers as texts", "The options write numbers as texts")>]
    [<DataRow("named numbers", "NaN and the infinities")>]
    [<DataRow("read-only properties ignored", "The options ignore read-only properties")>]
    [<DataRow("read-only fields ignored", "The options ignore read-only fields")>]
    [<DataRow("record properties included", "IncludeRecordProperties is set")>]
    [<DataRow("members overridden", "replace the attributes of members by their names (WithOverrideMembers)")>]
    member _.``GraphModel.create refuses a set-up of the serializer that the model does not support``
        (setUp : string, reason : string)
        =
        let refusal =
            Assert.ContainsSingle (
                refusalsOf GraphModelOptions.Default (refusedSetUp setUp),
                $"GraphModel.create should refuse the set-up '{setUp}' for one reason."
            )

        Assert.Contains (reason, refusal, StringComparison.Ordinal, "The refusal should give the reason.")

    [<TestMethod>]
    [<DataRow("", "The partition key property is empty", DisplayName = "empty")>]
    [<DataRow(" ", "The partition key property is empty", DisplayName = "only whitespace")>]
    [<DataRow("/PartitionKey", "contains a slash", DisplayName = "a path")>]
    [<DataRow("id", "does not accept /id", DisplayName = "id")>]
    [<DataRow("label", "does not accept /label", DisplayName = "label")>]
    member _.``GraphModel.create refuses a partition key property that a graph cannot have``
        (property : string, reason : string)
        =
        let refusal =
            Assert.ContainsSingle (
                refusalsOf { PartitionKeyProperty = ValueSome property } (Profiles.fsharpDefaults ()),
                $"GraphModel.create should refuse the partition key property '{property}'."
            )

        Assert.Contains (reason, refusal, StringComparison.Ordinal, "The refusal should give the reason.")

    [<TestMethod>]
    member _.``GraphModel.create gives the refusals of the options and of the settings together`` () =
        let refusals = refusalsOf { PartitionKeyProperty = ValueSome "" } (JsonSerializerOptions ())

        Assert.HasCount (2, refusals, "GraphModel.create should give the refusal of the options and the one of the settings.")

    [<TestMethod>]
    member _.``GraphModel.create leaves the options of the caller as they are`` () =
        let options = Profiles.skillGro ()
        (modelOf options).VertexOf typeof<SkillGro.DepartmentVertex>
        |> ignore

        Assert.IsFalse (
            options.IsReadOnly,
            "The model should ask a copy of the options for contracts, so that the caller can still change the options."
        )

    [<TestMethod>]
    [<DataRow("defaults")>]
    [<DataRow("camel case")>]
    [<DataRow("snake case")>]
    member _.``Canary: the names of the model are the names that the serializer writes`` (profile : string) =
        let options = Profiles.named profile
        let model = (modelOf options).VertexOf typeof<Named.NamedVertex>

        let written =
            use document = JsonDocument.Parse (JsonSerializer.Serialize (Named.vertex, options))
            document.RootElement.EnumerateObject ()
            |> Seq.map _.Name
            |> Seq.toArray

        let modelled =
            seq {
                yield! model.Id |> ValueOption.map _.Name |> ValueOption.toArray
                yield! model.Properties |> Seq.map _.Name
            }
            |> Seq.toArray

        CollectionAssert.AreEqual (
            written,
            modelled,
            "The model should name the id and the properties as FSharp.SystemTextJson writes the fields: JsonName first, then JsonPropertyName, then the naming policy."
        )

    [<TestMethod>]
    member _.``Canary: the contract of a record has no properties, so the names come from the rule of the library`` () =
        let info = contractOf (Profiles.fsharpDefaults ()) typeof<Named.NamedVertex>

        Assert.AreEqual (JsonTypeInfoKind.None, info.Kind, "FSharp.SystemTextJson should write a record with a converter.")
        Assert.IsEmpty (info.Properties, "The contract of a record should list no properties.")

    [<TestMethod>]
    member _.``Canary: an F# list has a converter of FSharp.SystemTextJson, and an immutable array is an enumerable`` () =
        let options = Profiles.fsharpDefaults ()
        let list = contractOf options typeof<string list>

        Assert.AreEqual (JsonTypeInfoKind.None, list.Kind, "FSharp.SystemTextJson should write an F# list with a converter.")

        Assert.AreEqual (
            typedefof<JsonListConverter<_>>,
            list.Converter.GetType().GetGenericTypeDefinition(),
            "The converter of an F# list should be JsonListConverter, which the model knows."
        )

        Assert.AreEqual (
            JsonTypeInfoKind.Enumerable,
            (contractOf options typeof<ImmutableArray<string>>).Kind,
            "System.Text.Json should write an immutable array as an enumerable."
        )

    [<TestMethod>]
    member _.``Canary: a vertex property is an object with the value and the meta-properties under the names of the meta model``
        ()
        =
        let options = Profiles.camelCase ()
        let meta = (modelOf options).MetaOf typeof<SkillGro.EmployeeAction>
        let by = SkillGro.EmployeeUserId (Guid "11111111-2222-3333-4444-555555555555")
        let property : VertexProperty<int, SkillGro.EmployeeAction> = VertexProperty.create 45 { By = by }

        Assert.AreEqual (
            $$$"""{"{{{meta.ValueName}}}":45,"{{{meta.MetaName}}}":{"by":"11111111-2222-3333-4444-555555555555"}}""",
            JsonSerializer.Serialize (property, options),
            "FSharp.SystemTextJson should write a vertex property as an object with the names of the meta model."
        )

    [<TestMethod>]
    member _.``Canary: a record with an ignored field of a value type cannot be read`` () =
        // Tarmil/FSharp.SystemTextJson#217. When a version of FSharp.SystemTextJson reads such a record, the graph model
        // can stop refusing the field.
        Assert.ThrowsExactly<NullReferenceException>(
            Action (fun () ->
                JsonSerializer.Deserialize<Refused.IgnoredValueType>("""{"Id":"1"}""", Profiles.fsharpDefaults ())
                |> ignore
            ),
            "FSharp.SystemTextJson 1.4.36 should throw NullReferenceException for an ignored field of a value type."
        )
        |> ignore
