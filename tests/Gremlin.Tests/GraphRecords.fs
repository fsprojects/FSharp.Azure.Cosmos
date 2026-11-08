namespace FSharp.Gremlin.Tests

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.Net.Mail
open System.Text.Json
open System.Text.Json.Serialization

open FSharp.Azure.Cosmos
open FSharp.Gremlin

/// <summary>
/// Records in the shape of the graph of SkillGro, the first application of the library: ids as unions with one case,
/// optional fields, lists, and vertex properties with the employee that did an action as a meta-property.
/// </summary>
module SkillGro =

    type CompanyId = CompanyId of Guid
    type DepartmentId = DepartmentId of Guid
    type EmployeeUserId = EmployeeUserId of Guid
    type LicensePurchaseId = LicensePurchaseId of Guid

    /// The meta-property of an action: the employee that did it.
    type EmployeeAction = { By : EmployeeUserId }

    type DepartmentVertex = {
        Id : DepartmentId
        ParentId : DepartmentId voption
        Name : string
        Description : string voption
        CulturePreferences : ImmutableArray<CultureInfo>
        [<PartitionKey>]
        CompanyId : CompanyId
        Created : VertexProperty<DateTimeOffset, EmployeeAction>
        Modified : VertexProperty<DateTimeOffset, EmployeeAction>
        Deleted : VertexProperty<DateTimeOffset, EmployeeAction> voption
    }

    type LicensePurchaseVertex = {
        Id : LicensePurchaseId
        [<PartitionKey>]
        CompanyId : CompanyId
        DepartmentId : DepartmentId option
        Duration : TimeSpan
        UsersQuantity : uint32
        ActivatedAt : DateTimeOffset
        IncomeAmount : decimal
        Tags : string list
        Labels : Set<string>
        Approvals : VertexProperty<DateTimeOffset, EmployeeAction> array
    }

    /// A vertex whose partition key is the field with the name of the partition key property.
    type TenantVertex = { Id : string; TenantId : string; Name : string }

    /// A vertex that is the only vertex of its partition.
    type SingleVertex = {
        [<PartitionKey>]
        Id : string
        Name : string
    }

    [<Label "DepartmentToCompany">]
    type EdgeDepartmentToCompany = { Date : DateTimeOffset; CreatedBy : EmployeeUserId }

    type EdgePermission = {
        Id : string voption
        Name : string
        Date : DateTimeOffset
        CreatedBy : EmployeeUserId
    }

    type EdgeUserToPosition = {
        Date : DateTimeOffset
        CreatedBy : EmployeeUserId
        DeletedDate : DateTimeOffset voption
        DeletedBy : EmployeeUserId voption
    }

    [<Label "Box">]
    type LabeledBox<'Value> = { Id : string; Value : 'Value }

/// <summary>
/// Records with names that the attributes of System.Text.Json and FSharp.SystemTextJson give, for the comparison of the
/// names of the graph model with the names that the serializer writes.
/// </summary>
module Named =

    type NamedVertex = {
        Id : string
        PlainName : string
        [<JsonName "jsonName">]
        WithJsonName : int
        [<JsonPropertyName "propertyName">]
        WithPropertyName : int
        [<JsonName "both"; JsonPropertyName "other">]
        WithBoth : int
        // A name with a field is the name of a field of a union case, so the serializer does not write the record
        // field under it
        [<JsonName("forUnionField", Field = "Case")>]
        WithFieldName : int
        [<JsonIgnore>]
        Ignored : string
    }

    /// The value that the comparison writes.
    let vertex = {
        Id = "v1"
        PlainName = "a"
        WithJsonName = 1
        WithPropertyName = 2
        WithBoth = 3
        WithFieldName = 4
        Ignored = "not written"
    }

/// Records that the graph model refuses, each one for one reason.
module Refused =

    type Meta = { By : string }

    type NotARecord () =
        member _.Id = "1"

    type NoId = { Name : string }
    type OptionalId = { Id : string voption; Name : string }
    type ListId = { Id : string array; Name : string }

    type ReservedId = {
        Id : string
        [<JsonPropertyName "id">]
        Other : string
    }

    type ReservedLabel = {
        Id : string
        [<JsonPropertyName "label">]
        Kind : string
    }

    type SameName = {
        Id : string
        [<JsonPropertyName "name">]
        First : string
        [<JsonPropertyName "name">]
        Second : string
    }

    type EmptyName = {
        Id : string
        [<JsonPropertyName "">]
        Name : string
    }

    type HiddenName = {
        Id : string
        [<JsonPropertyName "~name">]
        Name : string
    }

    /// A record that FSharp.SystemTextJson writes with a property that is no field.
    type IncludedProperty = {
        Id : string
        Name : string
    } with

        /// The name in capital letters. FSharp.SystemTextJson writes it, because it has JsonIncludeAttribute.
        [<JsonInclude>]
        member this.Upper = this.Name.ToUpperInvariant ()

    type IgnoredValueType = {
        Id : string
        [<JsonIgnore>]
        Count : int
    }

    type IgnoredId = {
        [<JsonIgnore>]
        Id : string
        Name : string
    }

    type NullableField = { Id : string; Count : Nullable<int> }
    type NestedOption = { Id : string; Note : string voption voption }
    type OptionalList = { Id : string; Tags : string array voption }
    type ListOfOptions = { Id : string; Tags : string voption array }
    type ListOfLists = { Id : string; Tags : string array array }
    type ListOfClasses = { Id : string; Emails : MailAddress array }
    type DictionaryField = { Id : string; Counts : Dictionary<string, int> }
    type MapField = { Id : string; Counts : Map<string, int> }
    type MapOfPairsField = { Id : string; Counts : Map<int, int> }

    /// A class that System.Text.Json cannot write: it has two constructors with JsonConstructorAttribute.
    type TwoConstructors (number : int, text : string) =

        /// Makes the value of a number.
        [<JsonConstructor>]
        new (number : int) = TwoConstructors (number, "")

        /// Makes the value of a text.
        [<JsonConstructor>]
        new (text : string) = TwoConstructors (0, text)

        /// The number.
        member _.Number = number

        /// The text.
        member _.Text = text

    type UnwritableField = { Id : string; Value : TwoConstructors }
    type ClassField = { Id : string; Email : MailAddress }
    type Address = { Street : string }
    type RecordField = { Id : string; Address : Address }
    type TupleField = { Id : string; Point : struct (int * int) }
    type Generic<'Value> = { Id : string; Value : 'Value }

    [<Label " ">]
    type EmptyLabel = { Id : string }

    type MetaWithList = { Values : string array }
    type MetaWithRecord = { Address : Address }

    type MetaWithSameName = {
        [<JsonPropertyName "by">]
        First : string
        [<JsonPropertyName "by">]
        Second : string
    }

    type MetaWithEmptyName = {
        [<JsonPropertyName "">]
        By : string
    }

    type MetaWithHiddenName = {
        [<JsonPropertyName "~by">]
        By : string
    }

    type VertexWithBadMeta = { Id : string; Created : VertexProperty<DateTimeOffset, MetaWithList> }
    type VertexPropertyOfOption = { Id : string; Note : VertexProperty<string voption, Meta> }
    type VertexPropertyOfList = { Id : string; Notes : VertexProperty<string array, Meta> }

    type NoPartitionKey = { Id : string; Name : string }

    type TwoPartitionKeys = {
        Id : string
        [<PartitionKey>]
        First : string
        [<PartitionKey>]
        Second : string
    }

    type PartitionKeyAndNamedField = {
        Id : string
        [<PartitionKey>]
        CompanyId : string
        PartitionKey : string
    }

    type OptionalPartitionKey = {
        Id : string
        [<PartitionKey>]
        CompanyId : string voption
    }

    type IgnoredPartitionKey = {
        Id : string
        [<PartitionKey; JsonIgnore>]
        CompanyId : string
    }

    type EdgeWithPartitionKey = {
        Date : DateTimeOffset
        [<PartitionKey>]
        CompanyId : string
    }

    type EdgeWithList = { Tags : string array }
    type EdgeWithMeta = { Created : VertexProperty<DateTimeOffset, Meta> }

    /// A record that a converter of the application writes.
    type WrittenByApplication = { Id : string; Name : string }

    /// <summary>
    /// Writes <see cref="T:FSharp.Gremlin.Tests.Refused.WrittenByApplication"/> in a way of its own: the graph model
    /// cannot know the names that it writes.
    /// </summary>
    type ApplicationConverter () =
        inherit JsonConverter<WrittenByApplication> ()

        /// <inheritdoc />
        override _.Read (reader, _, _) =
            reader.Skip ()
            { Id = ""; Name = "" }

        /// <inheritdoc />
        override _.Write (writer, value, _) =
            writer.WriteStartObject ()
            writer.WriteString ("key", value.Id)
            writer.WriteEndObject ()

    /// A record that FSharp.SystemTextJson writes with options of its own.
    type Overridden = { Id : string; Name : string voption }

    /// A generic record that FSharp.SystemTextJson writes with options of its own for each type argument.
    [<Label "OverriddenBox">]
    type OverriddenBox<'Value> = { Id : string; Value : 'Value }

    /// A record with options of its own in an attribute.
    [<JsonFSharpConverter>]
    type MarkedWithOptions = { Id : string; Name : string voption }

/// <summary>
/// Records that a converter writes, and the converter tells the names of their fields: the record converter of
/// FSharp.Azure.Cosmos.Json, and converters of an application.
/// </summary>
module Described =

    /// A vertex with each condition of JsonIgnore that changes which fields the record converter writes.
    type ConvertedVertex = {
        Id : string
        PlainName : string
        [<JsonName "jsonName">]
        WithJsonName : int
        [<JsonPropertyName "propertyName">]
        WithPropertyName : int
        // FSharp.SystemTextJson 1.4.36 cannot read an ignored field of a value type, and the record converter can
        [<JsonIgnore>]
        Count : int
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)>]
        ReadOnly : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)>]
        WriteOnly : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
        Note : string voption
    }

    /// The value that the comparison writes. Each field has a value, so the converter writes each field that it can.
    let vertex = {
        Id = "v1"
        PlainName = "a"
        WithJsonName = 1
        WithPropertyName = 2
        Count = 3
        ReadOnly = "r"
        WriteOnly = "w"
        Note = ValueSome "n"
    }

    /// A vertex that a converter of the application writes, with a name that no rule gives.
    type TaggedVertex = { Id : string; Name : string }

    /// <summary>
    /// Writes <see cref="T:FSharp.Gremlin.Tests.Described.TaggedVertex"/> with the name <c>tag</c> for the field
    /// <see cref="P:FSharp.Gremlin.Tests.Described.TaggedVertex.Name"/>, and tells the names that it writes.
    /// </summary>
    type TaggedVertexConverter () =
        inherit JsonConverter<TaggedVertex> ()

        static let names =
            seq {
                KeyValuePair ("Id", "id")
                KeyValuePair ("Name", "tag")
            }
            |> _.ToFrozenDictionary(StringComparer.Ordinal)

        /// <inheritdoc />
        override _.Read (reader, _, _) =
            reader.Skip ()
            { Id = ""; Name = "" }

        /// <inheritdoc />
        override _.Write (writer, value, _) =
            writer.WriteStartObject ()
            writer.WriteString ("id", value.Id)
            writer.WriteString ("tag", value.Name)
            writer.WriteEndObject ()

        interface IRecordFieldNames with

            /// <inheritdoc />
            member _.FieldNames = names :> IReadOnlyDictionary<_, _>

    /// <summary>
    /// Tells the names <c>v</c> and <c>m</c> for the fields of a vertex property of SkillGro, which the naming policy
    /// does not give.
    /// </summary>
    type RenamedVertexPropertyConverter () =
        inherit JsonConverter<VertexProperty<DateTimeOffset, SkillGro.EmployeeAction>> ()

        static let names =
            seq {
                KeyValuePair ("Value", "v")
                KeyValuePair ("Meta", "m")
            }
            |> _.ToFrozenDictionary(StringComparer.Ordinal)

        /// <inheritdoc />
        override _.Read (_reader, _, _) =
            raise (NotSupportedException "The tests only ask this converter for its names.")

        /// <inheritdoc />
        override _.Write (_, _, _) =
            raise (NotSupportedException "The tests only ask this converter for its names.")

        interface IRecordFieldNames with

            /// <inheritdoc />
            member _.FieldNames = names :> IReadOnlyDictionary<_, _>
