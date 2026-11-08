namespace FSharp.Gremlin

/// <summary>
/// A vertex property with its meta-properties: the value of the property, and a record whose fields are the
/// meta-properties.
/// </summary>
/// <remarks>
/// <para>
/// In the graph, <see cref="P:FSharp.Gremlin.VertexProperty`2.Value"/> is the value of the vertex property, and each
/// field of <see cref="P:FSharp.Gremlin.VertexProperty`2.Meta"/> is a meta-property of it. For example, the field
/// <c>Created : VertexProperty&lt;DateTimeOffset, EmployeeAction&gt;</c> with <c>EmployeeAction = { By : string }</c>
/// is the vertex property <c>created</c> with the meta-property <c>by</c>.
/// </para>
/// <para>
/// Azure Cosmos DB stores a meta-property only as a string, a number or a Boolean. Thus each field of the meta record
/// must have such a value.
/// </para>
/// </remarks>
[<Struct>]
type VertexProperty<'Value, 'Meta> = {
    /// The value of the vertex property.
    Value : 'Value
    /// The meta-properties: a record whose fields are the meta-properties.
    Meta : 'Meta
}

/// <summary>
/// Creates a <see cref="T:FSharp.Gremlin.VertexProperty`2"/>.
/// </summary>
[<RequireQualifiedAccess>]
module VertexProperty =

    /// <summary>
    /// Creates a vertex property from its <paramref name="value"/> and its meta-properties <paramref name="meta"/>.
    /// </summary>
    let create (value : 'Value) (meta : 'Meta) : VertexProperty<'Value, 'Meta> = { Value = value; Meta = meta }

    /// <summary>
    /// Creates the value of an optional vertex property: <see cref="create"/> in
    /// <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1.ValueSome"/>.
    /// </summary>
    let some (value : 'Value) (meta : 'Meta) : VertexProperty<'Value, 'Meta> voption = ValueSome (create value meta)
