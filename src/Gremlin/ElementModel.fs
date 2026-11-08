namespace FSharp.Gremlin

open System
open System.Collections.Frozen
open System.Collections.Immutable
open System.Reflection

/// What a record type is in a graph.
[<RequireQualifiedAccess>]
type ElementKind =
    /// A vertex.
    | Vertex
    /// An edge.
    | Edge

/// How a field tells that the element has no value for its property.
[<RequireQualifiedAccess>]
type Optionality =
    /// The field always has a value, so the element always has the property.
    | Required
    /// <summary>
    /// The field is a <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/>: the element has no property when the
    /// field is <see cref="P:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/>.
    /// </summary>
    | ValueOption
    /// <summary>
    /// The field is an <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/>: the element has no property when the field
    /// is <see cref="P:Microsoft.FSharp.Core.FSharpOption`1.None"/>.
    /// </summary>
    | Option

/// <summary>
/// How a property holds the value of its field. A property of a vertex holds one value or a list of values, and each
/// value can have meta-properties. A property of an edge holds one value. A value is a string, a number or a Boolean,
/// as the serializer writes it.
/// </summary>
/// <remarks>
/// The types are those after the option of the field, so the shape of <c>string voption</c> is
/// <see cref="M:FSharp.Gremlin.PropertyShape.NewScalar(System.Type)"/> of <see cref="T:System.String"/>.
/// </remarks>
[<RequireQualifiedAccess>]
type PropertyShape =
    /// One value: Gremlin writes it with the cardinality single.
    | Scalar of valueType : Type
    /// A list of values: Gremlin writes one value for each item, with the cardinality list.
    | ScalarList of itemType : Type
    /// <summary>
    /// One value with its meta-properties: the field is a <see cref="T:FSharp.Gremlin.VertexProperty`2"/>.
    /// </summary>
    | WithMeta of valueType : Type * metaType : Type
    /// <summary>
    /// A list of values, each with its meta-properties: the field is a list of
    /// <see cref="T:FSharp.Gremlin.VertexProperty`2"/>.
    /// </summary>
    | WithMetaList of valueType : Type * metaType : Type

/// <summary>
/// A field of a record and the property of the graph that holds its value.
/// </summary>
type PropertyModel = {
    /// The property that F# makes for the field.
    Member : PropertyInfo
    /// <summary>
    /// The key of the property in the graph: the name that the serializer writes the field under. For the field that
    /// holds the id of an element, the name that the materializer writes the id under.
    /// </summary>
    Name : string
    /// How the field tells that the element has no value for the property.
    Optionality : Optionality
    /// How the property holds the value of the field.
    Shape : PropertyShape
}

/// <summary>
/// The partition key of a vertex: the field that gives its value and the property of the graph that holds it.
/// </summary>
type PartitionKeyModel = {
    /// The field that gives the value of the partition key.
    Field : PropertyModel
    /// <summary>
    /// The property of the graph that holds the partition key:
    /// <see cref="P:FSharp.Gremlin.GraphModelOptions.PartitionKeyProperty"/>. It can be another property than the one
    /// of <see cref="P:FSharp.Gremlin.PartitionKeyModel.Field"/>, and then a vertex has both.
    /// </summary>
    PropertyName : string
}

/// <summary>
/// How a graph stores a record type as a vertex or as an edge.
/// </summary>
[<NoComparison; ReferenceEquality>]
type ElementModel = {
    /// The record type.
    Type : Type
    /// Whether the record type is a vertex or an edge.
    Kind : ElementKind
    /// <summary>
    /// The label of the element: the label of <see cref="T:FSharp.Gremlin.LabelAttribute"/> or the name of the type.
    /// </summary>
    Label : string
    /// <summary>
    /// The field named <c>Id</c>, which holds the id of the element. A vertex always has it. An edge can have no such
    /// field, because Azure Cosmos DB makes an id for an edge.
    /// </summary>
    Id : PropertyModel voption
    /// The partition key of a vertex; none for an edge and for a graph without partitions.
    PartitionKey : PartitionKeyModel voption
    /// The fields other than the id, in the sequence of their declaration.
    Properties : ImmutableArray<PropertyModel>
    /// The id and the properties by the name of their field.
    ByMember : FrozenDictionary<string, PropertyModel>
}

/// <summary>
/// How a graph stores the meta-properties of a <see cref="T:FSharp.Gremlin.VertexProperty`2"/>: the record type of the
/// meta-properties and its fields.
/// </summary>
[<NoComparison; ReferenceEquality>]
type MetaModel = {
    /// The record type of the meta-properties.
    Type : Type
    /// <summary>
    /// The name that the serializer writes <see cref="P:FSharp.Gremlin.VertexProperty`2.Value"/> under.
    /// </summary>
    ValueName : string
    /// <summary>
    /// The name that the serializer writes <see cref="P:FSharp.Gremlin.VertexProperty`2.Meta"/> under.
    /// </summary>
    MetaName : string
    /// <summary>
    /// The meta-properties, one for each field, in the sequence of their declaration. Each one is a
    /// <see cref="M:FSharp.Gremlin.PropertyShape.NewScalar(System.Type)"/>.
    /// </summary>
    Properties : ImmutableArray<PropertyModel>
}

/// <summary>
/// The settings of a graph that <see cref="T:FSharp.Gremlin.GraphModel"/> reads.
/// </summary>
type GraphModelOptions = {
    /// <summary>
    /// The property of the vertices that holds the partition key: the path of the partition key of the graph
    /// container without the slash, for example <c>PartitionKey</c>. None for a graph without partitions, for example a
    /// graph of a TinkerPop Gremlin Server.
    /// </summary>
    PartitionKeyProperty : string voption
} with

    static let ``default`` = { PartitionKeyProperty = ValueNone }

    /// The settings of a graph without partitions.
    static member Default = ``default``
