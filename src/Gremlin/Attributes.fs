namespace FSharp.Gremlin

open System

/// <summary>
/// Sets the label of the vertices or the edges of a record type.
/// </summary>
/// <remarks>
/// Without this attribute, the label is the name of the type. ExRam.Gremlinq writes the same label. Thus a graph that
/// ExRam.Gremlinq wrote keeps its labels.
/// </remarks>
[<Sealed; AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Struct, AllowMultiple = false)>]
type LabelAttribute (label : string) =
    inherit Attribute ()

    /// The label of the vertices or the edges of the type.
    member _.Label = label

/// <summary>
/// Marks the field of a vertex record that holds the partition key of the vertex.
/// </summary>
/// <remarks>
/// A graph of Azure Cosmos DB has one partition key property, which its container defines. The client writes that
/// property of a new vertex from the marked field. A record without this attribute uses the field whose name in the
/// graph is the name of the partition key property.
/// </remarks>
[<Sealed; AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type PartitionKeyAttribute () =
    inherit Attribute ()
