namespace FSharp.Gremlin

open System
open System.Collections.Concurrent
open System.Collections.Frozen
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization

open FSharp.Reflection

open FSharp.Azure.Cosmos

/// <summary>
/// Builds the models of <see cref="T:FSharp.Gremlin.GraphModel"/> and refuses what a graph cannot store.
/// </summary>
module internal GraphModelBuilder =

    /// The fields of a record, whatever their accessibility: a record of the caller can be internal or private.
    [<Literal>]
    let private AnyAccessibility = BindingFlags.Public ||| BindingFlags.NonPublic

    /// The name of the field that holds the id of an element.
    [<Literal>]
    let IdField = "Id"

    // Gremlin keeps these names for the id and the label of an element (T.id and T.label). Azure Cosmos DB reads a
    // property named id as the id of the vertex.
    let private reservedNames = [| "id"; "label" |] |> _.ToFrozenSet(StringComparer.Ordinal)

    // TinkerPop refuses an empty name of a property or a meta-property, and a name that starts with ~, which it keeps
    // for hidden keys (ElementHelper.validateProperty and Graph.Hidden of gremlin-core).
    let private nameRefusal (name : string) : string voption =
        if String.IsNullOrEmpty name then
            ValueSome "the name of a property cannot be empty."
        elif name.StartsWith ("~", StringComparison.Ordinal) then
            ValueSome $"Gremlin keeps the names that start with ~ for hidden keys, and the name is {name}."
        else
            ValueNone

    /// What a record is in the graph, for the messages of the refusals.
    [<RequireQualifiedAccess>]
    type Role =
        | Vertex
        | Edge
        | Meta

    let private article (role : Role) =
        match role with
        | Role.Vertex -> "a vertex"
        | Role.Edge -> "an edge"
        | Role.Meta -> "a record of meta-properties"

    let private noun (role : Role) =
        match role with
        | Role.Vertex -> "vertex"
        | Role.Edge -> "edge"
        | Role.Meta -> "record of meta-properties"

    /// The name of a type as F# code writes it, for the messages of the refusals.
    let rec displayName (valueType : Type) : string =
        if valueType.IsArray then
            $"{displayName (nonNull (valueType.GetElementType ()))}[]"
        elif valueType.IsGenericType then
            let name = valueType.Name
            let tick = name.IndexOf ('`', StringComparison.Ordinal)
            let baseName = if tick < 0 then name else name.Substring (0, tick)

            let arguments =
                valueType.GetGenericArguments ()
                |> Seq.map displayName
                |> String.concat ", "

            $"{baseName}<{arguments}>"
        else
            valueType.Name

    let private refuseRecord (recordType : Type) (role : Role) (reason : string) : 'a =
        raise (
            GraphModelException (recordType, ValueNone, $"The record {displayName recordType} cannot be {article role}: {reason}")
        )

    let private refuseMember
        (recordType : Type)
        (role : Role)
        (memberKind : string)
        (property : PropertyInfo)
        (reason : string)
        : 'a =
        raise (
            GraphModelException (
                recordType,
                ValueSome property.Name,
                $"The {memberKind} {property.Name} of the {noun role} {displayName recordType} is refused: {reason}"
            )
        )

    let private refuseField (recordType : Type) (role : Role) (field : PropertyInfo) (reason : string) : 'a =
        refuseMember recordType role "field" field reason

    let private isOption (valueType : Type) =
        SerializerProfile.isMadeOf typedefof<voption<_>> valueType
        || SerializerProfile.isMadeOf typedefof<option<_>> valueType

    let private isVertexProperty (valueType : Type) = SerializerProfile.isMadeOf typedefof<VertexProperty<_, _>> valueType

    /// The type of the value of a field, and how the field tells that the value is absent.
    let private unwrapOption (fieldType : Type) : struct (Optionality * Type) =
        if SerializerProfile.isMadeOf typedefof<voption<_>> fieldType then
            struct (Optionality.ValueOption, (fieldType.GetGenericArguments ()).[0])
        elif SerializerProfile.isMadeOf typedefof<option<_>> fieldType then
            struct (Optionality.Option, (fieldType.GetGenericArguments ()).[0])
        else
            struct (Optionality.Required, fieldType)

    /// What a value of a type is in the graph.
    [<RequireQualifiedAccess>]
    type private ValueKind =
        /// One value that a property can hold.
        | Scalar
        /// A list of items.
        | Collection of itemType : Type
        /// A vertex property with meta-properties.
        | WithMeta of valueType : Type * metaType : Type
        /// A value that no property can hold, and the reason.
        | Refused of reason : string

    let private classify (profile : SerializerProfile) (valueType : Type) : ValueKind =
        if isOption valueType then
            ValueKind.Refused
                "an option cannot hold another option, because FSharp.SystemTextJson writes both without a value as null."
        elif SerializerProfile.isMadeOf typedefof<Nullable<_>> valueType then
            ValueKind.Refused $"declare the field as a voption instead of {displayName valueType}."
        elif isVertexProperty valueType then
            let arguments = valueType.GetGenericArguments ()
            ValueKind.WithMeta (arguments.[0], arguments.[1])
        elif FSharpType.IsTuple valueType then
            ValueKind.Refused "a property holds one value, not a tuple."
        else
            match SerializerProfile.writtenAs profile valueType with
            | WrittenAs.Value -> ValueKind.Scalar
            | WrittenAs.Array itemType -> ValueKind.Collection itemType
            | WrittenAs.Object description ->
                ValueKind.Refused
                    $"the options write {displayName valueType} as {description}, and a property holds a string, a number or a Boolean. Give the type a converter that writes one of them."
            | WrittenAs.Unknown -> ValueKind.Refused $"the options cannot write {displayName valueType}."

    let private isIgnored (field : PropertyInfo) = field.IsDefined (typeof<JsonIgnoreAttribute>, false)

    let private isIdField (field : PropertyInfo) = String.Equals (field.Name, IdField, StringComparison.Ordinal)

    /// <summary>
    /// The fields of a record that the graph stores: all but those marked with
    /// <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/>.
    /// </summary>
    let private storedFields (recordType : Type) (role : Role) : PropertyInfo array =
        FSharpType.GetRecordFields (recordType, AnyAccessibility)
        |> Array.filter (fun field ->
            if not (isIgnored field) then
                true
            elif field.PropertyType.IsValueType then
                // Tarmil/FSharp.SystemTextJson#217. The canary test "a record with an ignored field of a value type
                // cannot be read" of SerializerProfileTests fails when a version of the library reads such a record, and
                // then this refusal can go.
                refuseField
                    recordType
                    role
                    field
                    "FSharp.SystemTextJson 1.4.36 throws NullReferenceException when it reads a record with an ignored field of a value type (https://github.com/Tarmil/FSharp.SystemTextJson/issues/217)."
            elif isIdField field && role <> Role.Meta then
                refuseField recordType role field "the field Id holds the id of the element, so it cannot be ignored."
            else
                false
        )

    /// The checks that every record type of the model passes: vertices, edges and the records of meta-properties.
    let private checkRecord (profile : SerializerProfile) (recordType : Type) (role : Role) =
        if not (FSharpType.IsRecord (recordType, AnyAccessibility)) then
            refuseRecord recordType role "it is not an F# record."

        match SerializerProfile.recordWriter profile recordType with
        | RecordWriter.Library ->
            // FSharp.SystemTextJson also writes a public property that is no field when the property has
            // JsonIncludeAttribute, and it requires the property when it reads the record
            let fields = FSharpType.GetRecordFields (recordType, AnyAccessibility)

            for property in recordType.GetProperties (BindingFlags.Instance ||| BindingFlags.Public) do
                let isField =
                    fields
                    |> Array.exists (fun field -> String.Equals (field.Name, property.Name, StringComparison.Ordinal))

                if
                    not isField
                    && property.IsDefined (typeof<JsonIncludeAttribute>, true)
                then
                    refuseMember
                        recordType
                        role
                        "property"
                        property
                        "FSharp.SystemTextJson writes it because it has JsonIncludeAttribute, and a graph stores only the fields of a record."
        | RecordWriter.OwnOptions ->
            refuseRecord
                recordType
                role
                "FSharp.SystemTextJson writes it with options of its own, in an override or in an attribute, and the graph model reads only the options that are common to all types."
        | RecordWriter.Other ->
            refuseRecord
                recordType
                role
                "the options write it with another converter than the record converter of FSharp.SystemTextJson, so the graph model does not know the names of its fields."

    /// <summary>
    /// The model of a record of meta-properties: the fields of the record, each one a meta-property that holds one
    /// value.
    /// </summary>
    let buildMeta (profile : SerializerProfile) (metaType : Type) : MetaModel =
        let role = Role.Meta
        checkRecord profile metaType role

        let properties =
            storedFields metaType role
            |> Seq.map (fun field ->
                let struct (optionality, valueType) = unwrapOption field.PropertyType

                match classify profile valueType with
                | ValueKind.Scalar -> {
                    Member = field
                    Name = SerializerProfile.memberName profile field
                    Optionality = optionality
                    Shape = PropertyShape.Scalar valueType
                  }
                | ValueKind.Refused reason -> refuseField metaType role field reason
                | ValueKind.Collection _
                | ValueKind.WithMeta _ ->
                    refuseField metaType role field "a meta-property holds one value: a string, a number or a Boolean."
            )
            |> ImmutableArray.CreateRange

        let names = HashSet<string> StringComparer.Ordinal

        for property in properties do
            match nameRefusal property.Name with
            | ValueSome reason -> refuseField metaType role property.Member reason
            | ValueNone -> ()

            if not (names.Add property.Name) then
                refuseField metaType role property.Member $"another field has the name {property.Name} too."

        let vertexProperty = typedefof<VertexProperty<_, _>>
        let valueName = SerializerProfile.memberName profile (nonNull (vertexProperty.GetProperty "Value"))
        let metaName = SerializerProfile.memberName profile (nonNull (vertexProperty.GetProperty "Meta"))

        // FSharp.SystemTextJson writes the value and the meta-properties of a vertex property under these names, so a
        // reader can tell them apart only when the names differ
        if String.Equals (valueName, metaName, StringComparison.Ordinal) then
            refuseRecord
                metaType
                role
                $"the naming policy gives the fields Value and Meta of VertexProperty one name, {valueName}, so the value and the meta-properties cannot be told apart."

        {
            Type = metaType
            ValueName = valueName
            MetaName = metaName
            Properties = properties
        }

    /// <summary>
    /// The shape of the property of a field of a vertex or an edge, and how the field tells that the element has no
    /// value for the property.
    /// </summary>
    let private shapeOf
        (profile : SerializerProfile)
        (metaOf : Type -> MetaModel)
        (recordType : Type)
        (role : Role)
        (field : PropertyInfo)
        : struct (Optionality * PropertyShape) =
        let refuse reason = refuseField recordType role field reason

        // The value of a vertex property and its meta-properties
        let checkVertexProperty (valueType : Type) (metaType : Type) =
            match
                SerializerProfile.recordWriter profile (typedefof<VertexProperty<_, _>>.MakeGenericType(valueType, metaType))
            with
            | RecordWriter.Library -> ()
            | RecordWriter.OwnOptions
            | RecordWriter.Other ->
                refuse "the options write VertexProperty in another way than the record converter of FSharp.SystemTextJson."

            if isOption valueType then
                refuse
                    "the value of a vertex property cannot be absent. Declare the field as a voption of the vertex property instead."

            match classify profile valueType with
            | ValueKind.Scalar -> ()
            | ValueKind.Refused reason -> refuse reason
            | ValueKind.Collection _
            | ValueKind.WithMeta _ -> refuse "the value of a vertex property is one value: a string, a number or a Boolean."

            try
                metaOf metaType |> ignore
            with :? GraphModelException as refusal ->
                raise (
                    GraphModelException (
                        recordType,
                        ValueSome field.Name,
                        $"The field {field.Name} of the {noun role} {displayName recordType} is refused, because its meta-properties are refused. {refusal.Message}",
                        refusal
                    )
                )

        let struct (optionality, valueType) = unwrapOption field.PropertyType

        let shape =
            match classify profile valueType with
            | ValueKind.Refused reason -> refuse reason
            | ValueKind.Scalar -> PropertyShape.Scalar valueType
            | ValueKind.WithMeta _ when role = Role.Edge -> refuse "a property of an edge has no meta-properties."
            | ValueKind.WithMeta (value, meta) ->
                checkVertexProperty value meta
                PropertyShape.WithMeta (value, meta)
            | ValueKind.Collection _ when role = Role.Edge ->
                refuse $"a property of an edge has one value, and the options write {displayName valueType} as a list."
            | ValueKind.Collection _ when optionality <> Optionality.Required ->
                refuse "a list property has no values when it is absent. Declare the field without an option."
            | ValueKind.Collection itemType when isOption itemType -> refuse "an item of a list cannot be absent."
            | ValueKind.Collection itemType ->
                match classify profile itemType with
                | ValueKind.Scalar -> PropertyShape.ScalarList itemType
                | ValueKind.WithMeta (value, meta) ->
                    checkVertexProperty value meta
                    PropertyShape.WithMetaList (value, meta)
                | ValueKind.Collection _ -> refuse "an item of a list cannot be a list."
                | ValueKind.Refused reason -> refuse $"an item of the list is refused: {reason}"

        struct (optionality, shape)

    let private labelOf (recordType : Type) (role : Role) : string =
        match recordType.GetCustomAttribute<LabelAttribute> false with
        | null ->
            if recordType.IsGenericType then
                refuseRecord
                    recordType
                    role
                    "a generic type needs LabelAttribute, because the name of the type ends with the number of its type parameters."

            recordType.Name
        | attribute when String.IsNullOrWhiteSpace attribute.Label ->
            refuseRecord recordType role "its LabelAttribute gives an empty label."
        | attribute -> attribute.Label

    /// <summary>
    /// The model of a record type as a vertex or as an edge.
    /// </summary>
    let buildElement
        (profile : SerializerProfile)
        (options : GraphModelOptions)
        (metaOf : Type -> MetaModel)
        (kind : ElementKind)
        (recordType : Type)
        : ElementModel =
        let role =
            match kind with
            | ElementKind.Vertex -> Role.Vertex
            | ElementKind.Edge -> Role.Edge

        // An anonymous record is generic too, but no attribute can give it a label
        if recordType.Name.StartsWith ("<>f__AnonymousType", StringComparison.Ordinal) then
            refuseRecord recordType role "an anonymous record has no name that can be its label."

        checkRecord profile recordType role
        let label = labelOf recordType role
        let fields = storedFields recordType role

        let modelOf (field : PropertyInfo) : PropertyModel =
            let struct (optionality, shape) = shapeOf profile metaOf recordType role field

            {
                Member = field
                Name = SerializerProfile.memberName profile field
                Optionality = optionality
                Shape = shape
            }

        let id =
            match fields |> Array.tryFind isIdField with
            | ValueSome field ->
                let model = modelOf field

                match model.Shape, model.Optionality with
                | PropertyShape.Scalar _, Optionality.Required -> ValueSome model
                | PropertyShape.Scalar _, _ when kind = ElementKind.Edge -> ValueSome model
                | PropertyShape.Scalar _, _ -> refuseField recordType role field "the id of a vertex cannot be absent."
                | _ -> refuseField recordType role field "the id of an element is one value: a string, a number or a Boolean."
            | ValueNone when kind = ElementKind.Vertex ->
                refuseRecord recordType role "it has no field Id, which holds the id of the vertex."
            | ValueNone -> ValueNone

        let properties =
            fields
            |> Seq.filter (isIdField >> not)
            |> Seq.map modelOf
            |> ImmutableArray.CreateRange

        let names = HashSet<string> StringComparer.Ordinal
        id
        |> ValueOption.iter (fun model -> names.Add model.Name |> ignore)

        for property in properties do
            match nameRefusal property.Name with
            | ValueSome reason -> refuseField recordType role property.Member reason
            | ValueNone -> ()

            if reservedNames.Contains property.Name then
                refuseField
                    recordType
                    role
                    property.Member
                    $"Gremlin keeps the property name {property.Name} for the {property.Name} of an element."

            if not (names.Add property.Name) then
                refuseField recordType role property.Member $"another field has the name {property.Name} too."

        // The id and the properties, the id first
        let modelled =
            seq {
                yield! ValueOption.toArray id
                yield! properties
            }
            |> ImmutableArray.CreateRange

        let marked =
            FSharpType.GetRecordFields (recordType, AnyAccessibility)
            |> Array.filter _.IsDefined(typeof<PartitionKeyAttribute>, false)

        let partitionKey =
            match kind, options.PartitionKeyProperty with
            | ElementKind.Edge, _ ->
                if marked.Length > 0 then
                    refuseField
                        recordType
                        role
                        marked.[0]
                        "an edge has no partition key of its own: Azure Cosmos DB keeps an edge in the partition of its out-vertex."

                ValueNone
            | ElementKind.Vertex, ValueNone -> ValueNone
            | ElementKind.Vertex, ValueSome propertyName ->
                // Also the id can give the partition key
                let named =
                    modelled
                    |> Seq.tryFind (fun property -> String.Equals (property.Name, propertyName, StringComparison.Ordinal))

                let field =
                    match marked, named with
                    | [||], ValueSome property -> property
                    | [||], ValueNone ->
                        refuseRecord
                            recordType
                            role
                            $"the graph keeps the partition key in the property {propertyName}. Mark the field that gives it with PartitionKeyAttribute, or give a field the name {propertyName}."
                    | [| markedField |], _ when isIgnored markedField ->
                        refuseField recordType role markedField "the field that gives the partition key cannot be ignored."
                    | [| markedField |], ValueSome property when not (property.Member.Equals markedField) ->
                        refuseField
                            recordType
                            role
                            property.Member
                            $"the field {markedField.Name} gives the partition key, which the graph keeps in the property {propertyName}, and this field has that name too."
                    | [| markedField |], _ ->
                        match id with
                        | ValueSome model when model.Member.Equals markedField -> model
                        | _ ->
                            properties
                            |> Seq.find (fun property -> property.Member.Equals markedField)
                    | _ -> refuseField recordType role marked.[1] "only one field can have PartitionKeyAttribute."

                match field.Shape, field.Optionality with
                | PropertyShape.Scalar _, Optionality.Required -> ValueSome { Field = field; PropertyName = propertyName }
                | _ ->
                    refuseField
                        recordType
                        role
                        field.Member
                        "every vertex has a partition key, and it is one value: a string, a number or a Boolean, without an option."

        let byMember = modelled.ToFrozenDictionary (_.Member.Name, StringComparer.Ordinal)

        {
            Type = recordType
            Kind = kind
            Label = label
            Id = id
            PartitionKey = partitionKey
            Properties = properties
            ByMember = byMember
        }

/// <summary>
/// How a graph stores the records of an application: for each record type, its label, its id, its partition key and
/// its properties, with the names that the serializer writes the fields under.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="M:FSharp.Gremlin.GraphModelModule.create(FSharp.Gremlin.GraphModelOptions,System.Text.Json.JsonSerializerOptions)"/>
/// makes the model. It refuses the options of a serializer that the model has not been compared with.
/// </para>
/// <para>
/// The model of a record type is made the first time that it is asked for, and then kept. A record type that the
/// model refuses throws a <see cref="T:FSharp.Gremlin.GraphModelException"/> each time that it is asked for. Many
/// threads can ask at the same time.
/// </para>
/// </remarks>
[<Sealed>]
type GraphModel internal (options : GraphModelOptions, profile : SerializerProfile) =

    let elements = ConcurrentDictionary<struct (Type * ElementKind), Lazy<ElementModel>>()
    let metas = ConcurrentDictionary<Type, Lazy<MetaModel>>()

    let metaOf (metaType : Type) : MetaModel =
        metas.GetOrAdd(metaType, (fun metaType -> lazy (GraphModelBuilder.buildMeta profile metaType))).Value

    let elementOf (kind : ElementKind) (recordType : Type) : ElementModel =
        elements
            .GetOrAdd(
                struct (recordType, kind),
                fun (struct (recordType, kind)) -> lazy (GraphModelBuilder.buildElement profile options metaOf kind recordType)
            )
            .Value

    /// The settings of the graph.
    member _.Options = options

    /// <summary>
    /// The model of <paramref name="recordType"/> as a vertex.
    /// </summary>
    /// <exception cref="T:FSharp.Gremlin.GraphModelException">The graph cannot store the record as a vertex.</exception>
    member _.VertexOf (recordType : Type) : ElementModel =
        ArgumentNullException.ThrowIfNull (recordType, nameof recordType)
        elementOf ElementKind.Vertex recordType

    /// <summary>
    /// The model of <paramref name="recordType"/> as an edge.
    /// </summary>
    /// <exception cref="T:FSharp.Gremlin.GraphModelException">The graph cannot store the record as an edge.</exception>
    member _.EdgeOf (recordType : Type) : ElementModel =
        ArgumentNullException.ThrowIfNull (recordType, nameof recordType)
        elementOf ElementKind.Edge recordType

    /// <summary>
    /// The model of <paramref name="metaType"/> as the record of the meta-properties of a
    /// <see cref="T:FSharp.Gremlin.VertexProperty`2"/>.
    /// </summary>
    /// <exception cref="T:FSharp.Gremlin.GraphModelException">The record cannot hold meta-properties.</exception>
    member _.MetaOf (metaType : Type) : MetaModel =
        ArgumentNullException.ThrowIfNull (metaType, nameof metaType)
        metaOf metaType

/// <summary>
/// Makes a <see cref="T:FSharp.Gremlin.GraphModel"/>.
/// </summary>
[<RequireQualifiedAccess>]
module GraphModel =

    let private refusalsOfOptions (options : GraphModelOptions) : ImmutableArray<string> =
        match options.PartitionKeyProperty with
        | ValueNone -> ImmutableArray.Empty
        | ValueSome name when String.IsNullOrWhiteSpace name -> ImmutableArray.Create "The partition key property is empty."
        | ValueSome name when name.Contains ('/', StringComparison.Ordinal) ->
            ImmutableArray.Create
                $"The partition key property '{name}' contains a slash. Give the path of the partition key without the slash, and a path of one property."
        | ValueSome name when
            String.Equals (name, "id", StringComparison.Ordinal)
            || String.Equals (name, "label", StringComparison.Ordinal)
            ->
            ImmutableArray.Create
                $"Azure Cosmos DB does not accept /{name} as the partition key of a graph, and Gremlin keeps the property name {name}."
        | ValueSome _ -> ImmutableArray.Empty

    /// <summary>
    /// Makes the model of a graph with the settings <paramref name="options"/>, for the records that
    /// <paramref name="serializerOptions"/> write, or gives the reasons why the model cannot work with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model supports the set-ups of the serializer that it has been compared with, and refuses the others: a
    /// model that names a property in another way than the serializer finds no vertices, and no error says why. The
    /// options must have one converter of FSharp.SystemTextJson, of that class itself, that writes every kind of type
    /// of F# and writes an option as its value (<c>UnwrapOption</c>). That converter must not write the properties of
    /// a record that are no field (<c>IncludeRecordProperties</c>) or replace the attributes of members
    /// (<c>WithOverrideMembers</c>). The options must not handle references, write numbers as texts, or ignore
    /// read-only members.
    /// </para>
    /// <para>
    /// The model reads <paramref name="serializerOptions"/> when it is made and keeps a copy, so a change of them
    /// afterwards does not reach it.
    /// </para>
    /// </remarks>
    /// <param name="options">The settings of the graph.</param>
    /// <param name="serializerOptions">The options that the records are written and read with.</param>
    let create
        (options : GraphModelOptions)
        (serializerOptions : JsonSerializerOptions)
        : Result<GraphModel, ImmutableArray<string>> =
        ArgumentNullException.ThrowIfNull (serializerOptions, nameof serializerOptions)
        let optionRefusals = refusalsOfOptions options

        match SerializerProfile.ofOptions serializerOptions with
        | Ok profile when optionRefusals.IsEmpty -> Ok (GraphModel (options, profile))
        | Ok _ -> Error optionRefusals
        | Error refusals -> Error (refusals.AddRange optionRefusals)
