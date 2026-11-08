namespace FSharp.Gremlin.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Gremlin

/// Assertions on the refusals of the graph model.
[<AbstractClass; Sealed; DebuggerNonUserCode>]
type private ModelAssert private () =

    /// <summary>
    /// Fails the test unless <paramref name="action"/> throws a <see cref="T:FSharp.Gremlin.GraphModelException"/> that
    /// names <paramref name="recordType"/>, <paramref name="field"/> (empty when the record type is the cause) and a
    /// message that contains <paramref name="reason"/>.
    /// </summary>
    static member Refuses (action : unit -> unit, recordType : Type, field : string, reason : string) =
        let refusal =
            Assert.ThrowsExactly<GraphModelException>(Action action, "The graph model should refuse the record type.")

        Assert.AreEqual (recordType, refusal.RecordType, "The refusal should name the record type.")

        Assert.AreEqual<string voption>(
            (if String.IsNullOrEmpty field then
                 ValueNone
             else
                 ValueSome field),
            refusal.Field,
            "The refusal should name the field that is the cause, and no field when the record type is the cause."
        )

        Assert.Contains (reason, refusal.Message, StringComparison.Ordinal, "The message of the refusal should give the reason.")

/// <summary>
/// Checks how <see cref="T:FSharp.Gremlin.GraphModel"/> describes records in the shape of SkillGro, and that it refuses
/// each record and field that a graph cannot store, with the record, the field and the reason.
/// </summary>
[<TestClass; UnitTestCategory; GraphModelTestCategory>]
type GraphModelTests () =

    static let partitioned = { PartitionKeyProperty = ValueSome "PartitionKey" }

    static let modelWith (options : GraphModelOptions) (serializerOptions : JsonSerializerOptions) : GraphModel =
        match GraphModel.create options serializerOptions with
        | Ok model -> model
        | Error reasons ->
            raise (
                AssertFailedException
                    $"""GraphModel.create should accept the options, but it refuses them: {String.Join ("; ", reasons)}"""
            )

    // One line for each property, so that a difference shows the field, the name, the optionality and the shape
    static let describe (property : PropertyModel) : string =
        let shape =
            match property.Shape with
            | PropertyShape.Scalar valueType -> $"Scalar {valueType.Name}"
            | PropertyShape.ScalarList itemType -> $"ScalarList {itemType.Name}"
            | PropertyShape.WithMeta (valueType, metaType) -> $"WithMeta {valueType.Name} {metaType.Name}"
            | PropertyShape.WithMetaList (valueType, metaType) -> $"WithMetaList {valueType.Name} {metaType.Name}"

        $"{property.Member.Name} as {property.Name}: %A{property.Optionality} {shape}"

    static let describeAll (properties : PropertyModel seq) = properties |> Seq.map describe |> Seq.toArray

    [<TestMethod>]
    member _.``a vertex has the name of its type as its label and the field Id as its id`` () =
        let model =
            (modelWith GraphModelOptions.Default (Profiles.skillGro ())).VertexOf typeof<SkillGro.DepartmentVertex>

        Assert.AreEqual (ElementKind.Vertex, model.Kind, "VertexOf should make the model of a vertex.")
        Assert.AreEqual (
            "DepartmentVertex",
            model.Label,
            "The label of a record without LabelAttribute should be the name of its type."
        )

        match model.Id with
        | ValueSome id ->
            Assert.AreEqual (
                "Id as id: Required Scalar DepartmentId",
                describe id,
                "The field Id should be the id of the vertex."
            )
        | ValueNone -> Assert.Fail "A vertex should have the field Id as its id."

    [<TestMethod>]
    member _.``LabelAttribute gives the label, also of a generic record`` () =
        let model = modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())

        Assert.AreEqual (
            "DepartmentToCompany",
            (model.EdgeOf typeof<SkillGro.EdgeDepartmentToCompany>).Label,
            "LabelAttribute should give the label of an edge."
        )

        Assert.AreEqual (
            "Box",
            (model.VertexOf typeof<SkillGro.LabeledBox<int>>).Label,
            "LabelAttribute should give the label of a generic record."
        )

    [<TestMethod>]
    [<DataRow("SkillGro")>]
    [<DataRow("SkillGro with the record converter")>]
    member _.``the fields of a department of SkillGro have the names of the serializer and the shapes of their types``
        (profile : string)
        =
        let model = (modelWith partitioned (Profiles.named profile)).VertexOf typeof<SkillGro.DepartmentVertex>

        CollectionAssert.AreEqual (
            [|
                "ParentId as parentId: ValueOption Scalar DepartmentId"
                "Name as name: Required Scalar String"
                "Description as description: ValueOption Scalar String"
                "CulturePreferences as culturePreferences: Required ScalarList CultureInfo"
                "CompanyId as companyId: Required Scalar CompanyId"
                "Created as created: Required WithMeta DateTimeOffset EmployeeAction"
                "Modified as modified: Required WithMeta DateTimeOffset EmployeeAction"
                "Deleted as deleted: ValueOption WithMeta DateTimeOffset EmployeeAction"
            |],
            describeAll model.Properties,
            "Each field other than Id should be a property with the name that the serializer writes and the shape of its type."
        )

    [<TestMethod>]
    member _.``the fields of a license purchase of SkillGro have the shapes of their types`` () =
        let model =
            (modelWith partitioned (Profiles.fsharpDefaults ())).VertexOf typeof<SkillGro.LicensePurchaseVertex>

        CollectionAssert.AreEqual (
            [|
                "CompanyId as CompanyId: Required Scalar CompanyId"
                "DepartmentId as DepartmentId: Option Scalar DepartmentId"
                "Duration as Duration: Required Scalar TimeSpan"
                "UsersQuantity as UsersQuantity: Required Scalar UInt32"
                "ActivatedAt as ActivatedAt: Required Scalar DateTimeOffset"
                "IncomeAmount as IncomeAmount: Required Scalar Decimal"
                "Tags as Tags: Required ScalarList String"
                "Labels as Labels: Required ScalarList String"
                "Approvals as Approvals: Required WithMetaList DateTimeOffset EmployeeAction"
            |],
            describeAll model.Properties,
            "An option, an F# list and an array of vertex properties should have the shapes of their types."
        )

    [<TestMethod>]
    [<DataRow("defaults", "Value", "Meta", "By")>]
    [<DataRow("camel case", "value", "meta", "by")>]
    member _.``the meta model has the names that the serializer writes``
        (profile : string, valueName : string, metaName : string, byName : string)
        =
        let meta =
            (modelWith GraphModelOptions.Default (Profiles.named profile)).MetaOf typeof<SkillGro.EmployeeAction>

        Assert.AreEqual (valueName, meta.ValueName, "ValueName should be the name of the field Value of VertexProperty.")
        Assert.AreEqual (metaName, meta.MetaName, "MetaName should be the name of the field Meta of VertexProperty.")

        CollectionAssert.AreEqual (
            [| $"By as {byName}: Required Scalar EmployeeUserId" |],
            describeAll meta.Properties,
            "Each field of the record of meta-properties should be a meta-property."
        )

    [<TestMethod>]
    member _.``the field with PartitionKeyAttribute gives the partition key and keeps its own property`` () =
        let model = (modelWith partitioned (Profiles.skillGro ())).VertexOf typeof<SkillGro.DepartmentVertex>

        match model.PartitionKey with
        | ValueSome partitionKey ->
            Assert.AreEqual (
                "PartitionKey",
                partitionKey.PropertyName,
                "The partition key should be in the property of the settings."
            )

            Assert.AreEqual (
                "CompanyId as companyId: Required Scalar CompanyId",
                describe partitionKey.Field,
                "The field with PartitionKeyAttribute should give the partition key."
            )

            Assert.Contains (
                partitionKey.Field,
                model.Properties,
                "The field that gives the partition key should keep its own property."
            )
        | ValueNone -> Assert.Fail "A vertex of a graph with partitions should have a partition key."

    [<TestMethod>]
    member _.``the field with the name of the partition key property gives the partition key`` () =
        let model =
            (modelWith { PartitionKeyProperty = ValueSome "tenantId" } (Profiles.camelCase ())).VertexOf
                typeof<SkillGro.TenantVertex>

        match model.PartitionKey with
        | ValueSome partitionKey ->
            Assert.AreEqual (
                "TenantId",
                partitionKey.Field.Member.Name,
                "The field named tenantId should give the partition key."
            )
            Assert.AreEqual (
                "tenantId",
                partitionKey.PropertyName,
                "The partition key should be in the property of the settings."
            )
        | ValueNone -> Assert.Fail "A vertex with a field named as the partition key property should have a partition key."

    [<TestMethod>]
    member _.``the id can give the partition key`` () =
        let model = (modelWith partitioned (Profiles.fsharpDefaults ())).VertexOf typeof<SkillGro.SingleVertex>

        match model.PartitionKey, model.Id with
        | ValueSome partitionKey, ValueSome id ->
            Assert.AreEqual (id, partitionKey.Field, "The field Id with PartitionKeyAttribute should give the partition key.")
        | _ -> Assert.Fail "The vertex should have an id and a partition key."

    [<TestMethod>]
    member _.``the id gives the partition key when it has the name of the partition key property`` () =
        let model =
            (modelWith { PartitionKeyProperty = ValueSome "Id" } (Profiles.fsharpDefaults ())).VertexOf
                typeof<SkillGro.TenantVertex>

        match model.PartitionKey, model.Id with
        | ValueSome partitionKey, ValueSome id ->
            Assert.AreEqual (
                id,
                partitionKey.Field,
                "The field Id, which has the name of the partition key property, should give the partition key."
            )
        | _ -> Assert.Fail "The vertex should have an id and a partition key."

    [<TestMethod>]
    member _.``a graph without partitions ignores PartitionKeyAttribute`` () =
        let model =
            (modelWith GraphModelOptions.Default (Profiles.skillGro ())).VertexOf typeof<SkillGro.DepartmentVertex>

        Assert.IsTrue (model.PartitionKey.IsNone, "A vertex of a graph without partitions should have no partition key.")

    [<TestMethod>]
    member _.``an edge can have no id or an optional id, and each property of an edge holds one value`` () =
        let model = modelWith partitioned (Profiles.fsharpDefaults ())
        let toCompany = model.EdgeOf typeof<SkillGro.EdgeDepartmentToCompany>

        Assert.AreEqual (ElementKind.Edge, toCompany.Kind, "EdgeOf should make the model of an edge.")
        Assert.IsTrue (toCompany.Id.IsNone, "An edge without a field Id should have no id, because Azure Cosmos DB makes one.")
        Assert.IsTrue (toCompany.PartitionKey.IsNone, "An edge should have no partition key of its own.")

        match (model.EdgeOf typeof<SkillGro.EdgePermission>).Id with
        | ValueSome id ->
            Assert.AreEqual ("Id as Id: ValueOption Scalar String", describe id, "The id of an edge can be optional.")
        | ValueNone -> Assert.Fail "An edge with a field Id should have an id."

        CollectionAssert.AreEqual (
            [|
                "Date as Date: Required Scalar DateTimeOffset"
                "CreatedBy as CreatedBy: Required Scalar EmployeeUserId"
                "DeletedDate as DeletedDate: ValueOption Scalar DateTimeOffset"
                "DeletedBy as DeletedBy: ValueOption Scalar EmployeeUserId"
            |],
            describeAll (model.EdgeOf typeof<SkillGro.EdgeUserToPosition>).Properties,
            "Each field of an edge should be a property that holds one value."
        )

    [<TestMethod>]
    member _.``ByMember finds the id and the properties by the name of their field`` () =
        let model =
            (modelWith GraphModelOptions.Default (Profiles.skillGro ())).VertexOf typeof<SkillGro.DepartmentVertex>

        CollectionAssert.AreEquivalent (
            [|
                "Id"
                "ParentId"
                "Name"
                "Description"
                "CulturePreferences"
                "CompanyId"
                "Created"
                "Modified"
                "Deleted"
            |],
            model.ByMember.Keys |> Seq.toArray,
            "ByMember should have the id and each property."
        )

        Assert.AreEqual ("parentId", model.ByMember["ParentId"].Name, "ByMember should give the model of a field by its name.")

    [<TestMethod>]
    member _.``a record that the record converter writes has the fields that the converter writes`` () =
        let model =
            (modelWith GraphModelOptions.Default (Profiles.withRecordConverter (Profiles.camelCase ()))).VertexOf
                typeof<Described.ConvertedVertex>

        CollectionAssert.AreEqual (
            [|
                "PlainName as plainName: Required Scalar String"
                "WithJsonName as jsonName: Required Scalar Int32"
                "WithPropertyName as propertyName: Required Scalar Int32"
                "WriteOnly as writeOnly: Required Scalar String"
                "Note as note: ValueOption Scalar String"
            |],
            describeAll model.Properties,
            "The properties should be the fields that the record converter writes, under its names: an ignored field of a value type and a field that the converter only reads are no properties."
        )

    [<TestMethod>]
    member _.``a converter that tells its names gives the names of the id and the properties`` () =
        let options = Profiles.fsharpDefaults ()
        // Before the converter of FSharp.SystemTextJson, so the serializer asks it first
        options.Converters.Insert (0, Described.TaggedVertexConverter ())
        let model = (modelWith GraphModelOptions.Default options).VertexOf typeof<Described.TaggedVertex>

        CollectionAssert.AreEqual (
            [| "Id as id: Required Scalar String"; "Name as tag: Required Scalar String" |],
            describeAll (
                seq {
                    yield! ValueOption.toArray model.Id
                    yield! model.Properties
                }
            ),
            "The model should name the id and the properties as the converter tells, also with a name that no rule gives."
        )

    [<TestMethod>]
    member _.``a field with JsonIgnoreAttribute is no property`` () =
        let model =
            (modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())).VertexOf typeof<Named.NamedVertex>

        Assert.IsFalse (model.ByMember.ContainsKey "Ignored", "A field that the serializer ignores should not be a property.")

    [<TestMethod>]
    member _.``the model of a record type is made one time`` () =
        let model = modelWith GraphModelOptions.Default (Profiles.skillGro ())

        Assert.AreSame (
            model.VertexOf typeof<SkillGro.DepartmentVertex>,
            model.VertexOf typeof<SkillGro.DepartmentVertex>,
            "The model should keep the model of a record type that it has made."
        )

    [<TestMethod>]
    member _.``a refused record type is refused each time that it is asked for`` () =
        let model = modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())

        for _ in 1..2 do
            ModelAssert.Refuses (
                (fun () -> model.VertexOf typeof<Refused.NoId> |> ignore),
                typeof<Refused.NoId>,
                "",
                "it has no field Id"
            )

    [<TestMethod>]
    [<DataRow(typeof<Refused.NotARecord>, "", "it is not an F# record", DisplayName = "a class")>]
    [<DataRow(typeof<Refused.NoId>, "", "it has no field Id", DisplayName = "no field Id")>]
    [<DataRow(typeof<Refused.OptionalId>, "Id", "the id of a vertex cannot be absent", DisplayName = "an optional id")>]
    [<DataRow(typeof<Refused.ListId>, "Id", "the id of an element is one value", DisplayName = "a list as id")>]
    [<DataRow(typeof<Refused.ReservedId>, "Other", "Gremlin keeps the property name id", DisplayName = "the name id")>]
    [<DataRow(typeof<Refused.ReservedLabel>, "Kind", "Gremlin keeps the property name label", DisplayName = "the name label")>]
    [<DataRow(typeof<Refused.SameName>, "Second", "another field has the name name too", DisplayName = "two fields with one name")>]
    [<DataRow(typeof<Refused.EmptyName>, "Name", "the name of a property cannot be empty", DisplayName = "an empty name")>]
    [<DataRow(typeof<Refused.HiddenName>, "Name", "for hidden keys", DisplayName = "the name of a hidden key")>]
    [<DataRow(typeof<Refused.IncludedProperty>,
              "Upper",
              "because it has JsonIncludeAttribute",
              DisplayName = "a property with JsonIncludeAttribute")>]
    [<DataRow(typeof<Refused.IgnoredValueType>,
              "Count",
              "NullReferenceException",
              DisplayName = "an ignored field of a value type")>]
    [<DataRow(typeof<Refused.IgnoredId>, "Id", "so it cannot be ignored", DisplayName = "an ignored id")>]
    [<DataRow(typeof<Refused.NullableField>, "Count", "declare the field as a voption", DisplayName = "a Nullable")>]
    [<DataRow(typeof<Refused.NestedOption>, "Note", "an option cannot hold another option", DisplayName = "an option of an option")>]
    [<DataRow(typeof<Refused.OptionalList>,
              "Tags",
              "a list property has no values when it is absent",
              DisplayName = "an optional list")>]
    [<DataRow(typeof<Refused.ListOfOptions>, "Tags", "an item of a list cannot be absent", DisplayName = "a list of options")>]
    [<DataRow(typeof<Refused.ListOfLists>, "Tags", "an item of a list cannot be a list", DisplayName = "a list of lists")>]
    [<DataRow(typeof<Refused.ListOfClasses>,
              "Emails",
              "an item of the list is refused: the options write MailAddress as an object",
              DisplayName = "a list of a class without a converter")>]
    [<DataRow(typeof<Refused.DictionaryField>, "Counts", "as an object with a property for each key", DisplayName = "a dictionary")>]
    [<DataRow(typeof<Refused.MapField>, "Counts", "as an object with a property for each key", DisplayName = "a map")>]
    [<DataRow(typeof<Refused.MapOfPairsField>,
              "Counts",
              "as an array of pairs of a key and a value",
              DisplayName = "a map with keys that are no texts")>]
    [<DataRow(typeof<Refused.UnwritableField>,
              "Value",
              "the options cannot write TwoConstructors",
              DisplayName = "a type that the serializer cannot write")>]
    [<DataRow(typeof<Refused.ClassField>,
              "Email",
              "the options write MailAddress as an object",
              DisplayName = "a class without a converter")>]
    [<DataRow(typeof<Refused.RecordField>, "Address", "as an object with a property for each field", DisplayName = "a record")>]
    [<DataRow(typeof<Refused.TupleField>, "Point", "not a tuple", DisplayName = "a tuple")>]
    [<DataRow(typeof<Refused.Generic<int>>,
              "",
              "a generic type needs LabelAttribute",
              DisplayName = "a generic record without a label")>]
    [<DataRow(typeof<Refused.EmptyLabel>, "", "its LabelAttribute gives an empty label", DisplayName = "an empty label")>]
    [<DataRow(typeof<Refused.VertexWithBadMeta>,
              "Created",
              "because its meta-properties are refused",
              DisplayName = "refused meta-properties")>]
    [<DataRow(typeof<Refused.VertexPropertyOfOption>,
              "Note",
              "the value of a vertex property cannot be absent",
              DisplayName = "a vertex property of an option")>]
    [<DataRow(typeof<Refused.VertexPropertyOfList>,
              "Notes",
              "the value of a vertex property is one value",
              DisplayName = "a vertex property of a list")>]
    member _.``VertexOf refuses a record that a graph cannot store as a vertex``
        (recordType : Type, field : string, reason : string)
        =
        let model = modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())
        ModelAssert.Refuses ((fun () -> model.VertexOf recordType |> ignore), recordType, field, reason)

    [<TestMethod>]
    member _.``VertexOf refuses an anonymous record, which has no name for a label`` () =
        let model = modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())
        let anonymous = typeof<{| Id : string |}>
        ModelAssert.Refuses ((fun () -> model.VertexOf anonymous |> ignore), anonymous, "", "an anonymous record has no name")

    [<TestMethod>]
    [<DataRow(typeof<Refused.NoPartitionKey>,
              "",
              "the graph keeps the partition key in the property PartitionKey",
              DisplayName = "no partition key")>]
    [<DataRow(typeof<Refused.TwoPartitionKeys>,
              "Second",
              "only one field can have PartitionKeyAttribute",
              DisplayName = "two fields with PartitionKeyAttribute")>]
    [<DataRow(typeof<Refused.PartitionKeyAndNamedField>,
              "PartitionKey",
              "the field CompanyId gives the partition key",
              DisplayName = "a field with the name of the partition key property")>]
    [<DataRow(typeof<Refused.OptionalPartitionKey>,
              "CompanyId",
              "every vertex has a partition key",
              DisplayName = "an optional partition key")>]
    [<DataRow(typeof<Refused.IgnoredPartitionKey>,
              "CompanyId",
              "the field that gives the partition key cannot be ignored",
              DisplayName = "an ignored partition key")>]
    member _.``VertexOf refuses a vertex without one partition key in a graph with partitions``
        (recordType : Type, field : string, reason : string)
        =
        let model = modelWith partitioned (Profiles.fsharpDefaults ())
        ModelAssert.Refuses ((fun () -> model.VertexOf recordType |> ignore), recordType, field, reason)

    [<TestMethod>]
    [<DataRow(typeof<Refused.EdgeWithPartitionKey>,
              "CompanyId",
              "an edge has no partition key of its own",
              DisplayName = "a partition key")>]
    [<DataRow(typeof<Refused.EdgeWithList>, "Tags", "a property of an edge has one value", DisplayName = "a list")>]
    [<DataRow(typeof<Refused.EdgeWithMeta>,
              "Created",
              "a property of an edge has no meta-properties",
              DisplayName = "a vertex property")>]
    member _.``EdgeOf refuses a record that a graph cannot store as an edge``
        (recordType : Type, field : string, reason : string)
        =
        let model = modelWith partitioned (Profiles.fsharpDefaults ())
        ModelAssert.Refuses ((fun () -> model.EdgeOf recordType |> ignore), recordType, field, reason)

    [<TestMethod>]
    [<DataRow(typeof<Refused.NotARecord>, "", "it is not an F# record", DisplayName = "a class")>]
    [<DataRow(typeof<Refused.MetaWithList>, "Values", "a meta-property holds one value", DisplayName = "a list")>]
    [<DataRow(typeof<Refused.MetaWithRecord>, "Address", "as an object with a property for each field", DisplayName = "a record")>]
    [<DataRow(typeof<Refused.MetaWithSameName>,
              "Second",
              "another field has the name by too",
              DisplayName = "two fields with one name")>]
    [<DataRow(typeof<Refused.MetaWithEmptyName>, "By", "the name of a property cannot be empty", DisplayName = "an empty name")>]
    [<DataRow(typeof<Refused.MetaWithHiddenName>, "By", "for hidden keys", DisplayName = "the name of a hidden key")>]
    member _.``MetaOf refuses a record that cannot hold meta-properties`` (recordType : Type, field : string, reason : string) =
        let model = modelWith GraphModelOptions.Default (Profiles.fsharpDefaults ())
        ModelAssert.Refuses ((fun () -> model.MetaOf recordType |> ignore), recordType, field, reason)

    [<TestMethod>]
    member _.``MetaOf refuses meta-properties when the naming policy gives the value and the meta-properties one name`` () =
        let model =
            modelWith
                GraphModelOptions.Default
                (JsonFSharpOptions.Default().ToJsonSerializerOptions(PropertyNamingPolicy = OneNameForValueAndMeta ()))

        ModelAssert.Refuses (
            (fun () -> model.MetaOf typeof<SkillGro.EmployeeAction> |> ignore),
            typeof<SkillGro.EmployeeAction>,
            "",
            "gives the fields Value and Meta of VertexProperty one name, x"
        )

    [<TestMethod>]
    member _.``VertexOf refuses a record that a converter of the application writes`` () =
        let options = Profiles.fsharpDefaults ()
        // Before the converter of FSharp.SystemTextJson, so the serializer asks it first
        options.Converters.Insert (0, Refused.ApplicationConverter ())
        let model = modelWith GraphModelOptions.Default options

        ModelAssert.Refuses (
            (fun () ->
                model.VertexOf typeof<Refused.WrittenByApplication>
                |> ignore
            ),
            typeof<Refused.WrittenByApplication>,
            "",
            "the options write it with another converter than the record converter of FSharp.SystemTextJson"
        )

    [<TestMethod>]
    [<DataRow(typeof<Refused.IgnoredId>, "Id", "so it cannot be ignored", DisplayName = "an ignored id")>]
    [<DataRow(typeof<Refused.IgnoredPartitionKey>,
              "CompanyId",
              "the field that gives the partition key cannot be ignored",
              DisplayName = "an ignored partition key")>]
    member _.``VertexOf refuses a record of the record converter without a field that the graph needs``
        (recordType : Type, field : string, reason : string)
        =
        let model = modelWith partitioned (Profiles.withRecordConverter (Profiles.fsharpDefaults ()))
        ModelAssert.Refuses ((fun () -> model.VertexOf recordType |> ignore), recordType, field, reason)

    [<TestMethod>]
    member _.``VertexOf refuses a vertex property that a converter writes under other names than the meta model`` () =
        let options = Profiles.skillGro ()
        // Before the converter of FSharp.SystemTextJson, so the serializer asks it first
        options.Converters.Insert (0, Described.RenamedVertexPropertyConverter ())
        let model = modelWith GraphModelOptions.Default options

        ModelAssert.Refuses (
            (fun () -> model.VertexOf typeof<SkillGro.DepartmentVertex> |> ignore),
            typeof<SkillGro.DepartmentVertex>,
            "Created",
            "under other names than the naming policy gives them"
        )

    [<TestMethod>]
    member _.``VertexOf refuses a record that a record converter of the application writes with other options`` () =
        let options = Profiles.camelCase ()

        // A record converter that an application made with options without a naming policy, before the converter that
        // the gate checked: the serializer takes it and writes the names of the fields without camel case
        options.Converters.Insert (
            0,
            JsonRecordConverter<Refused.WrittenByApplication>(JsonSerializerOptions (), JsonFSharpOptions.Default ())
        )

        let model = modelWith GraphModelOptions.Default options

        ModelAssert.Refuses (
            (fun () ->
                model.VertexOf typeof<Refused.WrittenByApplication>
                |> ignore
            ),
            typeof<Refused.WrittenByApplication>,
            "",
            "the options write it with another converter than the record converter of FSharp.SystemTextJson"
        )

    [<TestMethod>]
    member _.``VertexOf refuses a record that FSharp.SystemTextJson writes with options of its own`` () =
        let options =
            JsonFSharpOptions
                .Default()
                .WithOverrides(fun common ->
                    let overrides = Dictionary<Type, JsonFSharpOptions>()
                    overrides.Add (typeof<Refused.Overridden>, common.WithSkippableOptionFields SkippableOptionFields.Always)
                    overrides :> IDictionary<Type, JsonFSharpOptions>
                )
                .ToJsonSerializerOptions()

        let model = modelWith GraphModelOptions.Default options

        ModelAssert.Refuses (
            (fun () -> model.VertexOf typeof<Refused.Overridden> |> ignore),
            typeof<Refused.Overridden>,
            "",
            "FSharp.SystemTextJson writes it with options of its own"
        )

    [<TestMethod>]
    member _.``VertexOf refuses a generic record whose definition has options of its own`` () =
        let options =
            JsonFSharpOptions
                .Default()
                .WithOverrides(fun common ->
                    let overrides = Dictionary<Type, JsonFSharpOptions>()
                    // The definition stands for every type that is made of it
                    overrides.Add (
                        typedefof<Refused.OverriddenBox<_>>,
                        common.WithSkippableOptionFields SkippableOptionFields.Always
                    )
                    overrides :> IDictionary<Type, JsonFSharpOptions>
                )
                .ToJsonSerializerOptions()

        let model = modelWith GraphModelOptions.Default options

        ModelAssert.Refuses (
            (fun () -> model.VertexOf typeof<Refused.OverriddenBox<int>> |> ignore),
            typeof<Refused.OverriddenBox<int>>,
            "",
            "FSharp.SystemTextJson writes it with options of its own"
        )

    [<TestMethod>]
    member _.``VertexOf refuses a record with options of its own in an attribute that the options allow`` () =
        let model =
            modelWith GraphModelOptions.Default (JsonFSharpOptions.Default().WithAllowOverride().ToJsonSerializerOptions())

        ModelAssert.Refuses (
            (fun () -> model.VertexOf typeof<Refused.MarkedWithOptions> |> ignore),
            typeof<Refused.MarkedWithOptions>,
            "",
            "FSharp.SystemTextJson writes it with options of its own"
        )
