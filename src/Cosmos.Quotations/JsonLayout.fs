namespace FSharp.Azure.Cosmos.Quotations

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open FSharp.Reflection

open FSharp.Azure.Cosmos

/// <summary>
/// How a member of a document is written when it has no value: an
/// <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/> or a <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/>
/// without one, a <see cref="T:System.Text.Json.Serialization.Skippable`1"/> that is skipped, a
/// <see cref="T:System.Nullable`1"/> without a value or a reference that is <see langword="null"/>. A query has to
/// know it to ask whether the member has a value.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type OptionalEncoding =
    /// Without a value the member is written as null; with one, as the value itself.
    | AlwaysNull
    /// Without a value the member is left out of the document; with one, it is written as the value itself.
    | AlwaysUndefined
    /// <summary>
    /// Without a value the member is written as null or left out, and the layout cannot tell which: a rule that it
    /// cannot read decides, such as a <see cref="P:System.Text.Json.Serialization.Metadata.JsonPropertyInfo.ShouldSerialize"/>
    /// that code has set. With a value, the member is written as the value itself.
    /// </summary>
    | NullOrUndefined
    /// <summary>
    /// The layout does not describe how the value is written: as a union with the name of its case, which is what
    /// FSharp.SystemTextJson writes for an option when <c>UnwrapOption</c> is off; by a converter that writes
    /// something of its own for a value without a value, or that fails to write one; or not at all, because the
    /// serializer refuses the type.
    /// </summary>
    | NotDescribed

/// <summary>
/// A member of a document as a query reads it: a property or a field, and the type of the value that it is read from.
/// </summary>
/// <remarks>
/// The member alone does not tell that type. A quotation names an inherited property as the type that declares it
/// has it, also when the property is read from a value of a derived type, and the serializer can write the two types
/// in different ways: one as an object, the other by a converter.
/// </remarks>
[<Struct>]
type DocumentMember = {
    /// The type of the value that the member is read from. In a quotation it is the type of the receiver.
    Holder : Type
    /// The property or the field, as any type that has it names it.
    Member : MemberInfo
}

/// <summary>
/// How a value of an enumeration is written. A query has to know it to compare such values by their order and to test
/// their bits, which only a number allows.
/// </summary>
/// <remarks>
/// The encoding is the kind of JSON value, and not what is written for one value of the enumeration: a naming policy
/// can spell a name, an attribute can declare one, and a converter of the application writes what it likes. So what
/// stands for such a value in a query has to come from the serializer: the encoding does not say it.
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type EnumEncoding =
    /// As the number under the value.
    | Number
    /// <summary>
    /// As a JSON string: the name of the value in the spelling that its converter gives it, or what a converter of the
    /// application writes for it. The converter of System.Text.Json writes a value that has no name, alone or as a
    /// combination of flags, as its number even then, unless it was made to refuse such a value.
    /// </summary>
    | String
    /// <summary>
    /// The layout does not describe how the values are written: neither as numbers nor as strings, some as numbers
    /// and others as strings, or not all of them because the converter refuses one.
    /// </summary>
    | NotDescribed

/// <summary>
/// How a tuple is written. A query has to know it to read an item of a tuple that a document holds.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type TupleEncoding =
    /// As a JSON array of the items in their order. A tuple of more than seven items is one array as well.
    | Array
    /// <summary>
    /// As a JSON object with a property for every item that the serializer writes.
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.MemberName(FSharp.Azure.Cosmos.Quotations.DocumentMember)"/>
    /// names the property of a member of the tuple type, such as <c>Item1</c>, and has no name for an item that is
    /// not written. System.Text.Json writes a tuple this way; the items after the seventh are those of the tuple in
    /// <c>Rest</c>.
    /// </summary>
    | Object
    /// The layout does not describe how the tuple is written: a converter that it does not know writes it.
    | NotDescribed

/// <summary>
/// How a collection is written. A query has to know it to ask about the items of a collection that a document holds.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type CollectionEncoding =
    /// <summary>
    /// As a JSON array with an item for every item of the collection.
    /// </summary>
    /// <param name="element">The type of an item.</param>
    | Array of element : Type
    /// <summary>
    /// Not as an array of its items, or the layout does not know how: an array of bytes is written as one text, and a
    /// converter that the layout does not know can write anything.
    /// </summary>
    | NotDescribed

/// <summary>
/// How a map or a dictionary is written. A query has to know it to look a key up in a map that a document holds.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type MapEncoding =
    /// <summary>
    /// As a JSON object with a property for every key.
    /// </summary>
    /// <param name="key">The type of a key.</param>
    /// <param name="value">The type of a value.</param>
    | Object of key : Type * value : Type
    /// <summary>
    /// As a JSON array with an item for every key, which is an array of the key and its value. FSharp.SystemTextJson
    /// writes a map this way when its keys are no texts.
    /// </summary>
    /// <param name="key">The type of a key.</param>
    /// <param name="value">The type of a value.</param>
    | ArrayOfPairs of key : Type * value : Type
    /// The layout does not describe how the map is written: a converter that it does not know writes it.
    | NotDescribed

/// <summary>
/// A step from a JSON value to a value that it holds. The steps to a part of a union start at the value of the union.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type JsonStep =
    /// <summary>
    /// To the property of a JSON object that has this name.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    | Property of name : string
    /// <summary>
    /// To the item of a JSON array at this index.
    /// </summary>
    /// <param name="index">The index of the item, counted from zero.</param>
    | Item of index : int

/// <summary>
/// What a union writes to name a case. FSharp.SystemTextJson writes the name of the case, and the number or the
/// boolean that a case declares in place of a name with
/// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/>.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type UnionTag =
    /// <summary>
    /// A JSON string.
    /// </summary>
    /// <param name="text">The string.</param>
    | String of text : string
    /// <summary>
    /// A JSON number.
    /// </summary>
    /// <param name="number">The number.</param>
    | Number of number : int
    /// <summary>
    /// A JSON boolean.
    /// </summary>
    /// <param name="flag">The boolean.</param>
    | Boolean of flag : bool

/// <summary>
/// What tells that a value of a union is of one case. A query has to know it to ask for the values of a case.
/// </summary>
[<RequireQualifiedAccess>]
type UnionCaseMark =
    /// <summary>
    /// The JSON value at the end of <paramref name="path"/> is <paramref name="tag"/>. With no step the value of the
    /// union itself is the tag, as for a case without fields that is written as its name alone.
    /// </summary>
    /// <param name="path">The steps from the value of the union to the tag.</param>
    /// <param name="tag">The tag of the case.</param>
    | Tag of path : ImmutableArray<JsonStep> * tag : UnionTag
    /// <summary>
    /// The value of the union is a JSON object that has a property of this name, which holds the fields of the case.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    | Property of name : string
    /// <summary>
    /// The value of the union is <see langword="null"/>: the case is the one that a union marked with
    /// <see cref="F:Microsoft.FSharp.Core.CompilationRepresentationFlags.UseNullAsTrueValue"/> represents by null.
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.OptionalEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// says how a member that holds such a null is written.
    /// </summary>
    | Null
    /// <summary>
    /// Nothing that is written tells the case: the union is written without tags, or it has one case, whose one
    /// field is written in place of the union.
    /// </summary>
    | Unmarked

/// <summary>
/// Where the value of a field of a union case is written.
/// </summary>
[<RequireQualifiedAccess>]
type UnionFieldPlace =
    /// <summary>
    /// The value of the field is the JSON value at the end of <paramref name="path"/>. With no step it is written in
    /// place of the union.
    /// </summary>
    /// <param name="path">The steps from the value of the union to the value of the field.</param>
    | Value of path : ImmutableArray<JsonStep>
    /// <summary>
    /// The field is a record, and its members are properties of the JSON object at the end of
    /// <paramref name="path"/>, which holds the tag of the case next to them: the object is more than the record.
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.MemberName(FSharp.Azure.Cosmos.Quotations.DocumentMember)"/>
    /// names the property of a member of the record.
    /// </summary>
    /// <param name="path">The steps from the value of the union to the object that holds the members.</param>
    | Members of path : ImmutableArray<JsonStep>

/// <summary>
/// How a value of one case of a union is written. A query has to know it to test the case of a union that a document
/// holds and to read a field of that case.
/// </summary>
[<RequireQualifiedAccess>]
type UnionCaseEncoding =
    /// <summary>
    /// The case is written in the way that is described.
    /// </summary>
    /// <param name="mark">What tells that a value is of this case.</param>
    /// <param name="fields">Where the fields of the case are written, in the order of the fields.</param>
    | Written of mark : UnionCaseMark * fields : ImmutableArray<UnionFieldPlace>
    /// <summary>
    /// The layout does not describe how the case is written: the serializer refuses the union, a converter that the
    /// layout does not know writes it, or it is an option, a value option or a skippable value that is written as
    /// its value, which
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.OptionalEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// describes.
    /// </summary>
    | NotDescribed

/// <summary>
/// What a query has to know about how documents were serialized. The translator asks it for every member that a
/// quotation reads, so that the query reads the property that the serializer wrote, and for every value that is no
/// JSON object by itself: an enumeration, a tuple, a collection, a map and a union are each written in more than one
/// way.
/// </summary>
/// <remarks>
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.JsonLayout.ofSerializerOptions(System.Text.Json.JsonSerializerOptions)"/>
/// gives the layout of System.Text.Json with or without FSharp.SystemTextJson. A layout of another serializer
/// implements this interface.
/// </remarks>
type IJsonLayout =

    /// <summary>
    /// The name of the JSON property that holds <paramref name="memberOfDocument"/>, a property or a field of a type
    /// that is written as a JSON object; no value when the documents hold no such property, as far as the layout can
    /// tell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member that the serializer never writes has a name all the same when the name is declared on it, with
    /// <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>: such a member stands for a property
    /// that something else writes into the document, as a type discriminator or a computed property is.
    /// </para>
    /// <para>
    /// A field of a union case has no name here.
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.UnionCaseEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},Microsoft.FSharp.Reflection.UnionCaseInfo)"/>
    /// gives the place of every field of a case, which is not always a property.
    /// </para>
    /// </remarks>
    abstract MemberName : memberOfDocument : DocumentMember -> string voption

    /// <summary>
    /// How a value of <paramref name="valueType"/> is written when it has no value: an option, a value option, a
    /// <see cref="T:System.Text.Json.Serialization.Skippable`1"/>, a nullable or a reference.
    /// </summary>
    /// <remarks>
    /// A member can name a converter of its own. The serializer leaves a reference or a nullable of such a member
    /// out, or writes it as <see langword="null"/>, before it asks that converter, so the member is described as any
    /// other, unless the converter asks for the null itself and writes something else for it. With a value the
    /// member is what its converter writes.
    /// An option, a value option or a skippable value of such a member is not described: the converter writes all of
    /// it.
    /// </remarks>
    /// <param name="memberOfDocument">
    /// The member that holds the value, when it is a member of a type written as a JSON object: a member can be left
    /// out. A field of a union case is such a member too, with the union as the type that it is read from: the
    /// layout knows where the case has its fields, and a field that stands among named fields can be left out, while
    /// an item of the array of the fields, and a field that stands in place of the fields or of the union, is always
    /// written. No value for an item of a collection or of a tuple that is written as an array, which is always
    /// written.
    /// </param>
    /// <param name="valueType">The type of the value, which is the type of the member when there is one.</param>
    abstract OptionalEncoding : memberOfDocument : DocumentMember voption * valueType : Type -> OptionalEncoding

    /// <summary>
    /// How a value of the enumeration <paramref name="enumType"/> is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This member and the three after it are asked alike. <paramref name="memberOfDocument"/> is the member that
    /// holds the value, when it is a member of a type written as a JSON object: a member can name a converter of its
    /// own, which writes its value and no other. A field of a union case is asked about as a member of the union. It
    /// has no value for an item of a collection and for an item of a tuple that is written as an array, which no
    /// member holds.
    /// </para>
    /// <para>
    /// The type that is asked about is the type of the member, or the type under an option, a value option, a
    /// skippable value or a nullable that the member is: what
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.OptionalEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// says of the member tells whether the value under it is written as itself. A converter of the member writes
    /// the whole value of the member, so with such a converter only the type of the member, or the type that the
    /// member is a nullable of, is described.
    /// </para>
    /// <para>
    /// A member that the serializer never writes is not described: something else writes its property, in a way of
    /// its own.
    /// </para>
    /// </remarks>
    /// <param name="memberOfDocument">The member that holds the value, when a member does.</param>
    /// <param name="enumType">The type of the value: an enumeration, or a nullable of one.</param>
    abstract EnumEncoding : memberOfDocument : DocumentMember voption * enumType : Type -> EnumEncoding

    /// <summary>
    /// How a value of the tuple type <paramref name="tupleType"/> is written. It is asked as
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.EnumEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// is.
    /// </summary>
    /// <param name="memberOfDocument">The member that holds the tuple, when a member does.</param>
    /// <param name="tupleType">The type of the tuple, a struct tuple or a reference tuple.</param>
    abstract TupleEncoding : memberOfDocument : DocumentMember voption * tupleType : Type -> TupleEncoding

    /// <summary>
    /// How a value of <paramref name="collectionType"/> is written, when it is a collection. It is asked as
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.EnumEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// is.
    /// </summary>
    /// <param name="memberOfDocument">The member that holds the collection, when a member does.</param>
    /// <param name="collectionType">The type of the collection.</param>
    abstract CollectionEncoding : memberOfDocument : DocumentMember voption * collectionType : Type -> CollectionEncoding

    /// <summary>
    /// How a value of <paramref name="mapType"/> is written, when it is a map or a dictionary. It is asked as
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.EnumEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// is.
    /// </summary>
    /// <param name="memberOfDocument">The member that holds the map, when a member does.</param>
    /// <param name="mapType">The type of the map.</param>
    abstract MapEncoding : memberOfDocument : DocumentMember voption * mapType : Type -> MapEncoding

    /// <summary>
    /// How a value of the union case <paramref name="case"/> is written: what tells that a value is of the case, and
    /// where its fields are. The union is the one that declares the case. It is asked as
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.EnumEncoding(Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Quotations.DocumentMember},System.Type)"/>
    /// is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member that names a converter of its own is not described: nothing shows how that converter writes a union.
    /// </para>
    /// <para>
    /// Neither is a union one of whose cases is written as an object with two properties of the name of the tag, the
    /// tag and a field: a query reads one property of a name, and nothing says which of the two.
    /// </para>
    /// </remarks>
    /// <param name="memberOfDocument">The member that holds the union, when a member does.</param>
    /// <param name="case">The case.</param>
    abstract UnionCaseEncoding : memberOfDocument : DocumentMember voption * case : UnionCaseInfo -> UnionCaseEncoding

/// <summary>
/// The layouts of the serializers that <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> comes with.
/// </summary>
[<RequireQualifiedAccess>]
module JsonLayout =

    /// The members of a type whatever their accessibility: a record of the caller may be internal or private.
    [<Literal>]
    let private AnyAccessibility = BindingFlags.Public ||| BindingFlags.NonPublic

    /// The assembly of System.Text.Json, whose own converters are the ones that it documents.
    let private serializerAssembly = typeof<JsonSerializer>.Assembly

    /// The assembly of FSharp.SystemTextJson, whose own converters are the ones whose way to write is known here.
    let private fsharpSerializerAssembly = typeof<JsonFSharpConverter>.Assembly

    /// <summary>
    /// Whether <paramref name="converterType"/> is
    /// <see cref="T:System.Text.Json.Serialization.BaseJsonTupleConverter`1"/> or a class that is derived from it.
    /// </summary>
    let rec private isDerivedFromTupleConverter (converterType : Type | null) : bool =
        match converterType with
        | null -> false
        | converterType ->
            (converterType.IsGenericType
             && Type.(=) (converterType.GetGenericTypeDefinition (), typedefof<BaseJsonTupleConverter<_>>))
            || isDerivedFromTupleConverter converterType.BaseType

    /// <summary>
    /// Whether <paramref name="converterType"/> is one of the converters of FSharp.SystemTextJson for tuples: it has a
    /// class for two, three and four items of either kind of tuple and one for every other number, and
    /// <see cref="T:System.Text.Json.Serialization.BaseJsonTupleConverter`1"/> is the base of all of them.
    /// </summary>
    /// <remarks>
    /// The base class is public, and so are the classes of the library that are derived from it: a converter of an
    /// application can be derived from any of them and write a tuple in a way of its own. So only a class of the
    /// library itself is one of its converters.
    /// </remarks>
    let private isTupleConverter (converterType : Type) : bool =
        converterType.Assembly.Equals fsharpSerializerAssembly
        && isDerivedFromTupleConverter converterType

    /// Whether the steps of <paramref name="path"/> begin with the steps of <paramref name="start"/>.
    let private beginsWith (start : ImmutableArray<JsonStep>) (path : ImmutableArray<JsonStep>) : bool =
        path.Length >= start.Length
        && path.AsSpan(0, start.Length).SequenceEqual(start.AsSpan ())

    /// <summary>
    /// The kinds of values that can be absent, as far as the way they are written depends on the kind.
    /// </summary>
    [<RequireQualifiedAccess; Struct>]
    type private OptionalKind =
        /// An option: without a value it is a null reference.
        | Option
        /// A value option: a struct, so without a value it is not null but the default value of its type.
        | ValueOption
        /// The skippable value of FSharp.SystemTextJson: a struct like a value option.
        | Skippable
        /// A nullable or a reference: written as itself or as null.
        | Nullable
        /// A value type that always has a value.
        | Never

    /// <summary>
    /// Whether <paramref name="valueType"/> is made of the generic type <paramref name="definition"/>.
    /// </summary>
    let private isMadeOf (definition : Type) (valueType : Type) : bool =
        valueType.IsGenericType
        && Type.(=) (valueType.GetGenericTypeDefinition (), definition)

    /// <summary>
    /// The kind of <paramref name="valueType"/>.
    /// </summary>
    let private optionalKindOf (valueType : Type) : OptionalKind =
        if isMadeOf typedefof<_ option> valueType then
            OptionalKind.Option
        elif isMadeOf typedefof<_ voption> valueType then
            OptionalKind.ValueOption
        elif isMadeOf typedefof<Skippable<_>> valueType then
            OptionalKind.Skippable
        elif not valueType.IsValueType then
            OptionalKind.Nullable
        elif isMadeOf typedefof<Nullable<_>> valueType then
            OptionalKind.Nullable
        else
            OptionalKind.Never

    /// <summary>
    /// Whether <paramref name="memberOfDocument"/> itself carries an attribute of the type
    /// <paramref name="attributeType"/>. An attribute of a declaration that the member overrides does not count: the
    /// serializers do not read it for the override.
    /// </summary>
    let private isMarked (attributeType : Type) (memberOfDocument : MemberInfo) : bool =
        memberOfDocument.IsDefined (attributeType, false)

    /// <summary>
    /// The name that <paramref name="memberOfDocument"/> declares for itself, when it does: the first name of the
    /// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/> of FSharp.SystemTextJson, which that library
    /// reads before the <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/> of System.Text.Json.
    /// The attributes of the member itself count, and not those of a declaration that it overrides: the serializers
    /// read them that way.
    /// </summary>
    /// <param name="readsJsonName">Whether the type of the member is written by FSharp.SystemTextJson.</param>
    /// <param name="memberOfDocument">The member.</param>
    let private declaredNameOf (readsJsonName : bool) (memberOfDocument : MemberInfo) : string voption =
        let ofJsonName =
            if readsJsonName then
                memberOfDocument.GetCustomAttributes<JsonNameAttribute> false
                // A name with a field is the name of a field of a union case, not of this member. The library
                // declares the field as a string that is never null and leaves it null when none is given.
                |> Seq.tryFind (fun attribute ->
                    match withNull attribute.Field with
                    | null -> true
                    | _ -> false
                )
                |> ValueOption.map _.Name.AsString()
            else
                ValueNone

        ofJsonName
        |> ValueOption.orElseWith (fun () ->
            match memberOfDocument.GetCustomAttribute<JsonPropertyNameAttribute> false with
            | null -> ValueNone
            | attribute -> ValueSome attribute.Name
        )

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are one member, also when one of them was asked
    /// for through a derived class.
    /// </summary>
    let private isSameMember (first : MemberInfo) (second : MemberInfo) : bool =
        first.MetadataToken = second.MetadataToken
        && first.Module.Equals second.Module
        && Type.(=) (first.DeclaringType, second.DeclaringType)

    /// <summary>
    /// Whether <paramref name="candidate"/> is the property <paramref name="declared"/> as a derived class overrides
    /// it. A quotation names a property that is overridden as the class that declares it first has it, and the
    /// contract of the derived class lists the override.
    /// </summary>
    let private isOverrideOf (declared : MemberInfo) (candidate : MemberInfo) : bool =
        match declared, candidate with
        | (:? PropertyInfo as declared), (:? PropertyInfo as candidate) ->
            match declared.GetMethod, candidate.GetMethod with
            | null, _
            | _, null -> false
            | declaredGetter, candidateGetter ->
                isSameMember (declaredGetter.GetBaseDefinition ()) (candidateGetter.GetBaseDefinition ())
        | _ -> false

    /// <summary>
    /// Whether a union type has a case that is represented by <see langword="null"/>. To FSharp.SystemTextJson such a
    /// null is a value that can be left out, while a null of any other union is not.
    /// </summary>
    let private hasNullCase (unionType : Type) : bool =
        unionType.GetCustomAttributes (typeof<CompilationRepresentationAttribute>, false)
        |> Seq.cast<CompilationRepresentationAttribute>
        |> Seq.exists _.Flags.HasFlag(CompilationRepresentationFlags.UseNullAsTrueValue)

    /// <summary>
    /// <paramref name="answer"/> with what it has answered kept: a question is answered once, and every later asking
    /// gets the answer that was found.
    /// </summary>
    /// <remarks>
    /// An answer of a layout is a fact of the options, and finding it out asks the serializer, which can run code of
    /// the application: a converter factory, a modifier of a contract.
    /// <see cref="M:System.Collections.Concurrent.ConcurrentDictionary`2.GetOrAdd(`0,System.Func{`0,`1})"/> can run
    /// its function more than once for a key that several threads ask for at a time, so what is kept for a question
    /// is the answer as a <see cref="T:System.Lazy`1"/>, which is computed by one of them.
    /// </remarks>
    let private remembered (answer : 'Question -> 'Answer) : 'Question -> 'Answer =
        let answers = ConcurrentDictionary<'Question, Lazy<'Answer>>()
        let create = Func<'Question, Lazy<'Answer>>(fun question -> lazy answer question)
        fun question -> answers.GetOrAdd(question, create).Value

    /// <summary>
    /// What FSharp.SystemTextJson reads from its options and from the options of the serializer when it writes the
    /// fields of a record.
    /// </summary>
    [<Struct>]
    type private RecordOptions = {
        /// The naming policy for a field that declares no name; none when names are written as they are.
        NamingPolicy : JsonNamingPolicy | null
        /// Whether a reference that is null is left out: the serializer ignores null values.
        IgnoresNull : bool
        /// When an option without a value is left out.
        SkippableOptionFields : SkippableOptionFields
        /// Whether the properties of a record that are not its fields are written too.
        IncludesProperties : bool
        /// How unions are written: a union that is written as the one value it wraps is null where that value is.
        UnionEncoding : JsonUnionEncoding
        /// Whether a field of any type may be null in a document.
        AllowsNullFields : bool
    }

    /// <summary>
    /// Whether a type is no union, record or tuple of F#. FSharp.SystemTextJson leaves a null of such a type out when
    /// null values are ignored, and writes a null of the other types.
    /// </summary>
    let private isNoTypeOfFSharp (valueType : Type) : bool =
        not (
            FSharpType.IsUnion (valueType, AnyAccessibility)
            || FSharpType.IsRecord (valueType, AnyAccessibility)
            || FSharpType.IsTuple valueType
        )

    /// <summary>
    /// Whether FSharp.SystemTextJson has a value of <paramref name="valueType"/> that it reads from a null. A null of
    /// such a type is a value to the library, which leaves it out when null values are ignored. The rules are the
    /// ones of the function of the library that finds that value.
    /// </summary>
    /// <param name="recordOptions">The options of the record whose field is asked about.</param>
    /// <param name="valueType">The type of the field.</param>
    let rec private hasNullValue (recordOptions : RecordOptions) (valueType : Type) : bool =
        let isOption =
            isMadeOf typedefof<_ option> valueType
            || isMadeOf typedefof<_ voption> valueType

        if hasNullCase valueType || Type.(=) (valueType, typeof<unit>) then
            true
        elif
            isMadeOf typedefof<_ voption> valueType
            && recordOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapOption
        then
            true
        elif
            isMadeOf typedefof<Skippable<_>> valueType
            || (isOption
                && recordOptions.SkippableOptionFields = SkippableOptionFields.Always)
        then
            hasNullValue recordOptions (valueType.GetGenericArguments()[0])
        elif recordOptions.AllowsNullFields && isNoTypeOfFSharp valueType then
            true
        else
            // A union of one case with one field that is written as that field is null where the field is.
            recordOptions.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapSingleCaseUnions
            && FSharpType.IsUnion (valueType, AnyAccessibility)
            && (
                match FSharpType.GetUnionCases (valueType, AnyAccessibility) with
                | [| case |] ->
                    match case.GetFields () with
                    | [| field |] -> hasNullValue recordOptions field.PropertyType
                    | _ -> false
                | _ -> false
            )

    /// <summary>
    /// The options that FSharp.SystemTextJson makes the converter of one type with.
    /// </summary>
    [<Struct>]
    type private ConverterOptions = {
        /// The options of FSharp.SystemTextJson for the type: the ones the type has for itself, or the common ones.
        Effective : JsonFSharpOptions
        /// The options of the converter among the options that takes the type; none when an attribute on the type
        /// makes the converter by itself.
        Common : JsonFSharpOptions voption
        /// The naming policy of the options of the serializer that the converter is made with; none when names are
        /// written as they are.
        NamingPolicy : JsonNamingPolicy | null
        /// Whether those options of the serializer ignore null values.
        IgnoresNull : bool
    }

    /// <summary>
    /// What FSharp.SystemTextJson reads from its options and from the options of the serializer when it writes a
    /// union.
    /// </summary>
    [<Struct>]
    type private UnionOptions = {
        /// How unions are written, with the encoding that the options of a type inherit put in.
        Encoding : JsonUnionEncoding
        /// The name of the property that holds the tag of a case.
        TagName : string
        /// The name of the property that holds the fields of a case.
        FieldsName : string
        /// The naming policy for a case that declares no name; none when names are written as they are.
        TagNamingPolicy : JsonNamingPolicy | null
        /// The naming policy for a field that declares no name; none when names are written as they are.
        FieldNamingPolicy : JsonNamingPolicy | null
        /// <summary>
        /// The common options, when the union is written with them and has none of its own: the ones of the
        /// converter among the options that takes the union. A record that a case unwraps is written as that
        /// converter writes it.
        /// </summary>
        Common : JsonFSharpOptions voption
    }

    /// <summary>
    /// The bits of <see cref="T:System.Text.Json.Serialization.JsonUnionEncoding"/> that choose where the tag is.
    /// </summary>
    [<Literal>]
    let private UnionFormatBits = 0xFE

    /// <summary>
    /// The bit of <see cref="F:System.Text.Json.Serialization.JsonUnionEncoding.Untagged"/> among those bits.
    /// </summary>
    [<Literal>]
    let private UntaggedBit = 0x08

    /// <summary>
    /// <paramref name="name"/> as <paramref name="policy"/> spells it; as it is without a policy.
    /// </summary>
    let private spelled (policy : JsonNamingPolicy | null) (name : string) : string =
        match policy with
        | null -> name
        | policy -> policy.ConvertName name

    /// <summary>
    /// The tag that FSharp.SystemTextJson writes for a case: the first name of the
    /// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/> of the case that names no field, then the name
    /// of its <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>, then the name of the case as
    /// <paramref name="policy"/> spells it. A name that is declared is written as it is.
    /// </summary>
    let private unionTagOf (policy : JsonNamingPolicy | null) (case : UnionCaseInfo) : UnionTag =
        let declared =
            case.GetCustomAttributes typeof<JsonNameAttribute>
            |> Seq.cast<JsonNameAttribute>
            // The library declares the field as a string that is never null and leaves it null when none is given.
            |> Seq.tryFind (fun attribute ->
                match withNull attribute.Field with
                | null -> true
                | _ -> false
            )

        match declared with
        | ValueSome attribute ->
            match attribute.Name with
            | JsonName.String text -> UnionTag.String text
            | JsonName.Int number -> UnionTag.Number number
            | JsonName.Bool flag -> UnionTag.Boolean flag
        | ValueNone ->
            match
                case.GetCustomAttributes typeof<JsonPropertyNameAttribute>
                |> Seq.cast<JsonPropertyNameAttribute>
                |> Seq.tryHead
            with
            | ValueSome attribute -> UnionTag.String attribute.Name
            | ValueNone -> UnionTag.String (spelled policy case.Name)

    /// <summary>
    /// A tag as the name of a JSON property, which is how a union with external tags writes it.
    /// </summary>
    let private propertyNameOf (tag : UnionTag) : string =
        match tag with
        | UnionTag.String text -> text
        | UnionTag.Number number -> string number
        | UnionTag.Boolean true -> "true"
        | UnionTag.Boolean false -> "false"

    /// <summary>
    /// The names that FSharp.SystemTextJson writes the fields of a case under when it writes them as properties, in
    /// the order of the fields.
    /// </summary>
    /// <remarks>
    /// A field has the name that F# gives it. With
    /// <see cref="F:System.Text.Json.Serialization.JsonUnionEncoding.UnionFieldNamesFromTypes"/> a field that the
    /// source does not name, <c>Item</c> or <c>Item1</c>, <c>Item2</c> and so on, has the name of its type, and a
    /// name that the case then has more than once gets the number of its occurrence. Under that name the case can
    /// declare another one, with a <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/> that names the
    /// field; a name without one is spelled by the policy.
    /// </remarks>
    let private unionFieldNamesOf (options : UnionOptions) (case : UnionCaseInfo) : string array =
        let fields = case.GetFields ()
        let occurrences = Dictionary<string, int> StringComparer.Ordinal

        let own =
            if options.Encoding.HasFlag JsonUnionEncoding.UnionFieldNamesFromTypes then
                fields
                |> Array.mapi (fun index field ->
                    let isUnnamed =
                        if fields.Length = 1 then
                            String.Equals (field.Name, "Item", StringComparison.Ordinal)
                        else
                            String.Equals (field.Name, $"Item%d{index + 1}", StringComparison.Ordinal)

                    let name = if isUnnamed then field.PropertyType.Name else field.Name

                    let occurrence =
                        match occurrences.TryGetValue name with
                        | true, seen -> seen + 1
                        | _ -> 1

                    occurrences[name] <- occurrence
                    struct (name, occurrence)
                )
            else
                fields |> Array.map (fun field -> struct (field.Name, 1))

        let declared = Dictionary<string, string> StringComparer.Ordinal

        for attribute in Seq.cast<JsonNameAttribute>(case.GetCustomAttributes typeof<JsonNameAttribute>) do
            match withNull attribute.Field with
            | null -> ()
            | field -> declared[field] <- attribute.Name.AsString ()

        own
        |> Array.map (fun (struct (name, occurrence)) ->
            let isRepeated =
                match occurrences.TryGetValue name with
                | true, seen -> seen > 1
                | _ -> false

            let name =
                if occurrence = 1 && not isRepeated then
                    name
                else
                    $"%s{name}%d{occurrence}"

            match declared.TryGetValue name with
            | true, given -> given
            | _ -> spelled options.FieldNamingPolicy name
        )

    // JsonSerializerOptions.IgnoreNullValues is obsolete, and System.Text.Json and FSharp.SystemTextJson still follow
    // it, so the layout has to read it. This function is the one place that does.
    #nowarn "44"

    /// <summary>
    /// The condition on which <paramref name="options"/> leave a member out that has no condition of its own.
    /// <see cref="P:System.Text.Json.JsonSerializerOptions.IgnoreNullValues"/>, the setting of old, stands for
    /// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/>; the options refuse the two
    /// settings together.
    /// </summary>
    let private defaultIgnoreConditionOf (options : JsonSerializerOptions) : JsonIgnoreCondition =
        if options.IgnoreNullValues then
            JsonIgnoreCondition.WhenWritingNull
        else
            options.DefaultIgnoreCondition

    #warnon "44"

    /// <summary>
    /// Who writes the members of a type, which decides the rules that their names and their absence follow.
    /// </summary>
    [<RequireQualifiedAccess>]
    type private Writer =
        /// System.Text.Json writes the type as an object, and its contract lists the properties.
        | Serializer of JsonTypeInfo
        /// FSharp.SystemTextJson writes the type as a record.
        | Record of RecordOptions
        /// The type is a union, which the converter of its contract writes: the fields of its cases are its members.
        | Union of JsonTypeInfo
        /// A converter that the layout does not know writes the type, or the type is not written as an object.
        | Unknown

    /// <summary>
    /// How FSharp.SystemTextJson writes the fields of a union case, which decides what is written for a field without
    /// a value.
    /// </summary>
    [<RequireQualifiedAccess; Struct>]
    type private UnionFieldsKind =
        /// As the properties of an object: each is written or left out as a field of a record is.
        | Properties
        /// <summary>
        /// Each at a place that is always written: as an item of an array, or as the one field in place of the fields
        /// or of the union.
        /// </summary>
        | Values
        /// <summary>
        /// As the members of a record in place of the fields. The library fails to write such a record when it is
        /// <see langword="null"/>.
        /// </summary>
        | RecordMembers

    /// <summary>
    /// The fields of a union case as FSharp.SystemTextJson writes them.
    /// </summary>
    [<Struct>]
    type private WrittenFields = {
        /// Where the fields are written, in the order of the fields.
        Places : ImmutableArray<UnionFieldPlace>
        /// How the fields are written there.
        Kind : UnionFieldsKind
    }

    /// <summary>
    /// A union case as FSharp.SystemTextJson writes it.
    /// </summary>
    [<Struct>]
    type private WrittenCase = {
        /// What tells that a value is of the case.
        Mark : UnionCaseMark
        /// The fields of the case.
        Fields : WrittenFields
    }

    /// <summary>
    /// The layout of what System.Text.Json writes with the options it is made of, with or without the converter of
    /// FSharp.SystemTextJson among them.
    /// </summary>
    /// <remarks>
    /// The layout asks the serializer itself who writes a type: the contract that the options resolve for a type names
    /// the converter. So the order of the converters, the kinds of types that FSharp.SystemTextJson is set to handle
    /// and a converter that a type names in an attribute all count as they do when a document is written.
    /// </remarks>
    [<Sealed>]
    type private SerializerLayout (options : JsonSerializerOptions) =

        // A copy, so that the options of the caller stay as they are: the contract can be asked only from options
        // that have a resolver and that are not changed any more. The resolver is the one of the caller, because a
        // resolver cannot be copied: it can be any object. So the layout asks who the serializer asks, and a resolver
        // of System.Text.Json takes no change after either has asked it, which keeps the two from disagreeing.
        let contract =
            let copy =
                match options.TypeInfoResolver with
                | null -> JsonSerializerOptions (options, TypeInfoResolver = DefaultJsonTypeInfoResolver ())
                | _ -> JsonSerializerOptions options

            copy.MakeReadOnly ()
            copy

        /// The condition on which the options leave a member out that has no condition of its own.
        let defaultIgnoreCondition = defaultIgnoreConditionOf contract

        /// The contract of a type; none for a type that the serializer refuses.
        let typeInfoOf (documentType : Type) : JsonTypeInfo voption =
            try
                ValueSome (contract.GetTypeInfo documentType)
            with
            | :? NotSupportedException
            | :? InvalidOperationException
            | :? ArgumentException
            // FSharp.SystemTextJson makes its converters by reflection, so what one of them refuses a type with comes
            // wrapped in this one: a union with a case that has two attributes with names is refused that way.
            | :? TargetInvocationException -> ValueNone

        /// <summary>
        /// The options that FSharp.SystemTextJson writes a record or a union type with: the ones the type is given in
        /// the overrides, then the ones of the attribute on the type when the options allow that, then the common
        /// ones. The common ones are those of the converter that takes the type, which is the first one among the
        /// options that does, as the serializer chooses it: the options can hold several converters of
        /// FSharp.SystemTextJson for different kinds of types. No value when nothing shows the options of the
        /// converter: a converter that stands among the options by itself keeps them to itself, and so does one that
        /// an attribute of the caller makes.
        /// </summary>
        let converterOptionsOf (valueType : Type) : ConverterOptions voption =
            let ofAttribute () =
                match valueType.GetCustomAttributes (typeof<IJsonFSharpConverterAttribute>, true) with
                | [||] -> ValueNone
                | attributes -> ValueSome (attributes[0] :?> IJsonFSharpConverterAttribute).Options

            let ofOverrides (common : JsonFSharpOptions) =
                // The library declares the overrides as never null and gives null when there are none.
                match withNull (common.Overrides common) with
                | null -> ValueNone
                | overrides ->
                    match overrides.TryGetValue valueType with
                    | true, given -> ValueSome given
                    | _ when valueType.IsGenericType ->
                        match overrides.TryGetValue (valueType.GetGenericTypeDefinition ()) with
                        | true, given -> ValueSome given
                        | _ -> ValueNone
                    | _ -> ValueNone

            match contract.Converters |> Seq.tryFind _.CanConvert(valueType) with
            // The factory of FSharp.SystemTextJson itself, and no class that an application derives from it: such a
            // class can make the converter of a record with other options than the ones it shows.
            | ValueSome (:? JsonFSharpConverter as factory) when Type.(=) (factory.GetType (), typeof<JsonFSharpConverter>) ->
                let common = factory.Options

                let effective =
                    ofOverrides common
                    |> ValueOption.orElseWith (fun () -> if common.AllowOverride then ofAttribute () else ValueNone)
                    |> ValueOption.defaultValue common

                ValueSome {
                    Effective = effective
                    Common = ValueSome common
                    NamingPolicy = contract.PropertyNamingPolicy
                    IgnoresNull = defaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }
            | ValueSome _ -> ValueNone
            | ValueNone ->
                // No converter among the options takes the type, so an attribute on the type made the converter. The
                // attribute of FSharp.SystemTextJson shows the options it makes the converter with, and gives it
                // options of the serializer of its own: no naming policy, and nothing is ignored. Any other attribute
                // can make the converter with options that nothing shows.
                ofAttribute ()
                |> ValueOption.map (fun effective -> {
                    Effective = effective
                    Common = ValueNone
                    NamingPolicy = null
                    IgnoresNull = false
                })

        /// <summary>
        /// How FSharp.SystemTextJson writes unions with the options of a converter.
        /// </summary>
        /// <remarks>
        /// The options that a type has for itself can say that they inherit the encoding of unions, with
        /// <see cref="F:System.Text.Json.Serialization.JsonUnionEncoding.Inherit"/>: the library then takes the whole
        /// encoding from the common options, also the parts that the options of the type set. The options that the
        /// attribute makes say so unless the attribute sets the whole encoding.
        /// </remarks>
        let unionEncodingOf (found : ConverterOptions) : JsonUnionEncoding =
            match found.Common with
            | ValueSome common when found.Effective.UnionEncoding.HasFlag JsonUnionEncoding.Inherit -> common.UnionEncoding
            | _ -> found.Effective.UnionEncoding

        /// <summary>
        /// What FSharp.SystemTextJson reads when it writes the fields of a record type. It reads the same when it
        /// writes the named fields of a case of a union type, which it writes or leaves out as it does the fields of
        /// a record.
        /// </summary>
        let recordOptionsOf (recordType : Type) : RecordOptions voption =
            converterOptionsOf recordType
            |> ValueOption.map (fun found -> {
                NamingPolicy = found.NamingPolicy
                IgnoresNull = found.IgnoresNull
                SkippableOptionFields = found.Effective.SkippableOptionFields
                IncludesProperties = found.Effective.IncludeRecordProperties
                UnionEncoding = unionEncodingOf found
                AllowsNullFields = found.Effective.AllowNullFields
            })

        /// What FSharp.SystemTextJson reads when it writes a union type.
        let unionOptionsOf (unionType : Type) : UnionOptions voption =
            converterOptionsOf unionType
            |> ValueOption.map (fun found ->
                let effective = found.Effective

                {
                    Encoding = unionEncodingOf found
                    TagName = effective.UnionTagName
                    FieldsName = effective.UnionFieldsName
                    TagNamingPolicy = effective.UnionTagNamingPolicy
                    FieldNamingPolicy =
                        match withNull effective.UnionFieldNamingPolicy with
                        | null -> found.NamingPolicy
                        | policy -> policy
                    Common =
                        found.Common
                        |> ValueOption.filter (fun common -> obj.ReferenceEquals (common, effective))
                }
            )

        /// <summary>
        /// Whether the serializer made a contract itself. It chooses the converter by rules that the layout follows: the
        /// first one among the options that takes the type, then the one that an attribute names, then one of its own.
        /// A resolver of the application can make a contract from any converter instead, also from a converter of
        /// FSharp.SystemTextJson that it made with options of its own, and nothing shows how it made that converter.
        /// </summary>
        /// <remarks>
        /// <see cref="P:System.Text.Json.Serialization.Metadata.JsonTypeInfo.OriginatingResolver"/> names the resolver
        /// that made a contract. The resolver of the serializer names itself, also as the class that an application
        /// derives from it to change the contracts it makes, while a contract that an application makes from a
        /// converter names none.
        /// </remarks>
        let isMadeBySerializer (info : JsonTypeInfo) : bool =
            match info.OriginatingResolver with
            | :? DefaultJsonTypeInfoResolver -> true
            | _ -> false

        /// Who writes the members of a type.
        let writerOf =
            remembered (fun (documentType : Type) ->
                match typeInfoOf documentType with
                | ValueSome info when info.Kind = JsonTypeInfoKind.Object -> Writer.Serializer info
                | ValueSome info when
                    isMadeBySerializer info
                    && isMadeOf typedefof<JsonRecordConverter<_>> (info.Converter.GetType ())
                    ->
                    match recordOptionsOf documentType with
                    | ValueSome recordOptions -> Writer.Record recordOptions
                    | ValueNone -> Writer.Unknown
                | ValueSome info when FSharpType.IsUnion (documentType, AnyAccessibility) -> Writer.Union info
                | _ -> Writer.Unknown
            )

        /// <summary>
        /// How a value without a value is written with a contract where it is not left out. The serializer is asked
        /// to write one: a null, or the value option or the skippable value that has no value.
        /// </summary>
        /// <remarks>
        /// The serializer writes a null as <c>null</c> itself, unless the converter asks for the null with
        /// <see cref="P:System.Text.Json.Serialization.JsonConverter`1.HandleNull"/>. Such a converter writes what
        /// it likes, and some cannot write a null at all: the converters of FSharp.SystemTextJson for tuples, for maps
        /// and for unions without fields ask for the null and fail on it. Only <c>null</c> is described.
        /// </remarks>
        let absentWrittenWith (contractOfValue : JsonTypeInfo voption) : OptionalEncoding =
            match contractOfValue with
            // The serializer has no contract for the type, and it needs one also to write a null of it.
            | ValueNone -> OptionalEncoding.NotDescribed
            | ValueSome info ->
                let absent : objnull =
                    if info.Type.IsValueType then
                        Activator.CreateInstance info.Type
                    else
                        null

                try
                    if JsonSerializer.SerializeToElement(absent, info).ValueKind = JsonValueKind.Null then
                        OptionalEncoding.AlwaysNull
                    else
                        OptionalEncoding.NotDescribed
                with _ ->
                    // Whatever a converter raises for the value, no document holds what it would have written.
                    OptionalEncoding.NotDescribed

        /// <summary>
        /// Whether a value of <paramref name="valueType"/> that is present is written as the value itself: by a
        /// converter of FSharp.SystemTextJson that unwraps it, or by System.Text.Json, which writes an option as its
        /// value and a nullable and a reference as themselves.
        /// </summary>
        let isWrittenAsItself (kind : OptionalKind) (valueType : Type) : bool =
            match kind with
            | OptionalKind.Nullable -> true
            | OptionalKind.Never -> false
            | OptionalKind.Option
            | OptionalKind.ValueOption
            | OptionalKind.Skippable ->
                match typeInfoOf valueType with
                | ValueSome info ->
                    let converter = info.Converter.GetType ()

                    isMadeOf typedefof<JsonUnwrapOptionConverter<_>> converter
                    || isMadeOf typedefof<JsonUnwrapValueOptionConverter<_>> converter
                    || isMadeOf typedefof<JsonSkippableConverter<_>> converter
                    // System.Text.Json has converters of its own for the two option types, and none for a Skippable.
                    || (kind <> OptionalKind.Skippable
                        && converter.Assembly.Equals serializerAssembly)
                | ValueNone -> false

        /// <summary>
        /// The entry that a contract has for a member, with the member that the entry was made of: the member itself,
        /// or the override of it that the type of the contract has when the contract lists no other entry for it. What
        /// the serializer reads from a member, it reads from the one the entry was made of.
        /// </summary>
        let propertyOf (info : JsonTypeInfo) (memberOfDocument : MemberInfo) : struct (JsonPropertyInfo * MemberInfo) voption =
            let entries = seq {
                for property in info.Properties do
                    match property.AttributeProvider with
                    | :? MemberInfo as own -> struct (property, own)
                    | _ -> ()
            }

            entries
            |> Seq.tryFind (fun (struct (_, own)) -> isSameMember own memberOfDocument)
            |> ValueOption.orElseWith (fun () ->
                entries
                |> Seq.tryFind (fun (struct (_, own)) -> isOverrideOf memberOfDocument own)
            )

        /// <summary>
        /// The contract that a member with a converter of its own writes a value of <paramref name="valueType"/>
        /// with. The serializer has no such contract, it keeps the converter with the member; options that hold the
        /// converter before every other one give it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Those options have the resolver of the serializer and not the one of the caller. The serializer writes the
        /// member with its converter whatever contract the resolver of the caller makes for the type, and such a
        /// resolver can make that contract without looking at the converters of the options it is asked with.
        /// </para>
        /// <para>
        /// A member can name a factory, which the serializer has make the converter with the options. Such a factory
        /// may ask those options for the converter that they have for the type, to add something around it. Asked by
        /// options that hold the factory itself, it would be asked without end, so the factory makes the converter
        /// with the options of the layout first, and the options that are asked for the contract hold what it made.
        /// Nothing is caught here: the serializer has made the converter the same way before it listed the member,
        /// and would have refused a member whose converter does not fit its type.
        /// </para>
        /// </remarks>
        let ownInfoOf (converter : JsonConverter) (valueType : Type) : JsonTypeInfo voption =
            let made =
                match converter with
                | :? JsonConverterFactory as factory -> factory.CreateConverter (valueType, contract)
                | converter -> converter

            match made with
            // The serializer refuses a member whose factory makes nothing, so no contract lists such a member.
            | null -> ValueNone
            | made ->
                let own = JsonSerializerOptions (contract, TypeInfoResolver = DefaultJsonTypeInfoResolver ())
                own.Converters.Insert (0, made)
                ValueSome (own.GetTypeInfo valueType)

        /// <summary>
        /// Whether System.Text.Json never writes a member of an object: the member has no getter the serializer may
        /// call, or it is read-only and the options ignore read-only members.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The contract does not show the second case: such a property keeps its getter and gets no rule. The rule is
        /// the one of the serializer. A member that has a rule is not left out, because the serializer asks the rule
        /// instead: the contract has one for a member that says itself when it is ignored, with a
        /// <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/> of any condition, and for a member that
        /// code gave one. Neither is a collection or a dictionary left out, which the serializer writes also when
        /// nothing can assign it. What writes the member decides whether it is one: the serializer, which writes the
        /// items, or a converter of the options or of the member, which writes what it likes.
        /// </para>
        /// <para>
        /// One case cannot be told. Code that sets the rule of a member to <see langword="null"/> has set a rule to
        /// the serializer, which then writes the read-only member; it remembers that in a field that is not public,
        /// and the contract shows the same <see langword="null"/> as for a member that nothing touched. Such a member
        /// is taken to be left out.
        /// </para>
        /// </remarks>
        let isNeverWritten (property : JsonPropertyInfo) (memberOfDocument : MemberInfo) : bool =
            let ignoresReadOnly =
                match memberOfDocument with
                | :? PropertyInfo -> contract.IgnoreReadOnlyProperties
                | :? FieldInfo -> contract.IgnoreReadOnlyFields
                | _ -> false

            let isReadOnly () =
                match property.Set with
                | null -> true
                | _ -> false

            let isCollection () =
                let info =
                    match property.CustomConverter with
                    | null -> typeInfoOf property.PropertyType
                    | converter -> ownInfoOf converter property.PropertyType

                match info with
                | ValueSome info ->
                    info.Kind = JsonTypeInfoKind.Enumerable
                    || info.Kind = JsonTypeInfoKind.Dictionary
                | ValueNone -> false

            match property.Get with
            | null -> true
            | _ ->
                ignoresReadOnly
                && isReadOnly ()
                && not (isCollection ())
                && (
                    match property.ShouldSerialize with
                    | null -> true
                    | _ -> false
                )

        /// <summary>
        /// Whether FSharp.SystemTextJson writes a member of a record: its fields, whatever their accessibility, and
        /// of its other properties the public ones, when the options or the member say so. It never writes a member
        /// that is marked with <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/>, whatever the
        /// condition of the attribute says.
        /// </summary>
        let isWrittenByRecord (recordOptions : RecordOptions) (memberOfDocument : MemberInfo) : bool =
            match memberOfDocument with
            | :? PropertyInfo as property when not (isMarked typeof<JsonIgnoreAttribute> property) ->
                let isField =
                    match property.DeclaringType with
                    | null -> false
                    | recordType ->
                        FSharpType.GetRecordFields (recordType, AnyAccessibility)
                        |> Array.exists (fun field -> isSameMember field property)

                let isPublic =
                    match property.GetMethod with
                    | null -> false
                    | getter -> getter.IsPublic

                isField
                || (isPublic
                    && (recordOptions.IncludesProperties
                        || isMarked typeof<JsonIncludeAttribute> property))
            | _ -> false

        /// The name of a member, by the rules of who writes its type.
        let nameOf (memberOfDocument : DocumentMember) : string voption =
            match writerOf memberOfDocument.Holder with
            | Writer.Serializer info ->
                match propertyOf info memberOfDocument.Member with
                | ValueSome (struct (property, _)) when property.IsExtensionData -> ValueNone
                | ValueSome (struct (property, own)) when isNeverWritten property own -> declaredNameOf false own
                | ValueSome (struct (property, _)) -> ValueSome property.Name
                | ValueNone -> ValueNone
            | Writer.Record recordOptions ->
                let field = memberOfDocument.Member
                let declared = declaredNameOf true field

                if isWrittenByRecord recordOptions field then
                    declared
                    |> ValueOption.orElseWith (fun () ->
                        match recordOptions.NamingPolicy with
                        | null -> ValueSome field.Name
                        | policy -> ValueSome (policy.ConvertName field.Name)
                    )
                else
                    // A member that is never written has the name it declares, when it declares one.
                    declared
            // The place of a field of a case is what the encoding of the case says, and it is not always a property.
            | Writer.Union _
            | Writer.Unknown -> ValueNone

        /// <summary>
        /// How a member of a type that System.Text.Json writes as an object is written without a value. The member
        /// decides with its own <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/>, and the options
        /// decide for a member that has none.
        /// </summary>
        /// <param name="property">The entry of the member in the contract.</param>
        /// <param name="memberOfDocument">The member.</param>
        /// <param name="kind">The kind of its value.</param>
        /// <param name="whenWritten">How the value is written without one when it is not left out.</param>
        let absenceInObject
            (property : JsonPropertyInfo)
            (memberOfDocument : MemberInfo)
            (kind : OptionalKind)
            (whenWritten : OptionalEncoding)
            : OptionalEncoding =
            let condition =
                // The attribute of the member itself: an override has none of the declaration it overrides.
                match memberOfDocument.GetCustomAttribute<JsonIgnoreAttribute> false with
                | null ->
                    match property.ShouldSerialize with
                    // A rule that code has set: it is a function, which says nothing before it is called.
                    | null -> ValueSome defaultIgnoreCondition
                    | _ -> ValueNone
                | attribute -> ValueSome attribute.Condition

            match condition, kind with
            // The serializer never writes the member, so whatever writes the property decides. A member that is
            // always ignored is such a member: the serializer keeps no getter for it.
            | _ when isNeverWritten property memberOfDocument -> OptionalEncoding.NullOrUndefined
            // A rule that code has set chooses between leaving the member out and writing it.
            | ValueNone, _ when whenWritten.IsAlwaysNull -> OptionalEncoding.NullOrUndefined
            | ValueNone, _ -> OptionalEncoding.NotDescribed
            | ValueSome JsonIgnoreCondition.WhenWritingDefault, _ -> OptionalEncoding.AlwaysUndefined
            // An option without a value and a nullable without a value are null; a value option and a Skippable
            // without a value are structs, which are not null, so they are written, as null.
            | ValueSome JsonIgnoreCondition.WhenWritingNull, (OptionalKind.Option | OptionalKind.Nullable) ->
                OptionalEncoding.AlwaysUndefined
            | ValueSome _, _ -> whenWritten

        /// <summary>
        /// How a field of a record that FSharp.SystemTextJson writes is written without a value.
        /// </summary>
        /// <param name="recordOptions">The options that the record is written with.</param>
        /// <param name="kind">The kind of the value of the field.</param>
        /// <param name="valueType">The type of the value.</param>
        /// <param name="whenWritten">How the value is written without one when it is not left out.</param>
        let absenceInRecord
            (recordOptions : RecordOptions)
            (kind : OptionalKind)
            (valueType : Type)
            (whenWritten : OptionalEncoding)
            : OptionalEncoding =
            let isLeftOut =
                match kind with
                | OptionalKind.Skippable -> true
                | OptionalKind.Option ->
                    recordOptions.SkippableOptionFields = SkippableOptionFields.Always
                    || (recordOptions.SkippableOptionFields = SkippableOptionFields.FromJsonSerializerOptions
                        && recordOptions.IgnoresNull)
                // A value option without a value is not null, so only the setting for every option leaves it out.
                | OptionalKind.ValueOption -> recordOptions.SkippableOptionFields = SkippableOptionFields.Always
                | OptionalKind.Nullable ->
                    // A record, a union and a tuple of F# are written also when they are null and null values are
                    // ignored. A union with a case that is null is the exception, and so is a union that is written
                    // as the one value it wraps where that value can be null: their null is a value to the library,
                    // which is left out.
                    recordOptions.IgnoresNull
                    && (isNoTypeOfFSharp valueType
                        || hasNullValue recordOptions valueType)
                | OptionalKind.Never -> false

            if isLeftOut then
                OptionalEncoding.AlwaysUndefined
            else
                whenWritten

        /// <summary>
        /// How FSharp.SystemTextJson writes a case of a union with the options that it has for the union; no value
        /// when the layout does not describe the case. The rules are read from the source of the library and compared
        /// with what it writes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value that is null is written as null before anything else, and a case without fields as its tag alone
        /// when such tags are unwrapped. Then the encoding decides where the tag is: in a property next to the
        /// property of the fields, as the name of the property of the fields, as the first item or a property among
        /// the fields, or nowhere.
        /// </para>
        /// <para>
        /// The fields are an array of their values or an object with a property for each. A case with one field
        /// writes that value in place of the fields where the encoding unwraps such cases, which the internal tag and
        /// the encoding without tags do not do. A case whose one field is a record writes the members of the record
        /// in place of the fields where the encoding unwraps records. The library writes such a record with the
        /// options of the union, so it is described only when those are the common ones and the record by itself is
        /// written by the library as well: then the object is the record as it is written anywhere, and
        /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.MemberName(FSharp.Azure.Cosmos.Quotations.DocumentMember)"/>
        /// names its members. Only next to an internal tag is the object more than the record.
        /// </para>
        /// </remarks>
        let writtenCaseOf (options : UnionOptions) (case : UnionCaseInfo) : WrittenCase voption =
            let encoding = options.Encoding
            let fields = case.GetFields ()
            let tag = unionTagOf options.TagNamingPolicy case
            let hasNamedFields = encoding.HasFlag JsonUnionEncoding.NamedFields

            // The case that a union marked to do so represents by null. The compiler lets a union be marked so only
            // when it has a case with fields as well (FS1196), so the converter of the unions without fields, which
            // writes a tag for every case, never writes such a union.
            let isNullCase =
                fields.Length = 0
                && (
                    match FSharpValue.MakeUnion (case, [||], AnyAccessibility) with
                    | null -> true
                    | _ -> false
                )

            // The flag that unwraps records holds the one that names the fields.
            let unwrapsRecord =
                fields.Length = 1
                && FSharpType.IsRecord (fields[0].PropertyType, AnyAccessibility)
                && encoding.HasFlag JsonUnionEncoding.UnwrapRecordCases

            let unwrapsField =
                not unwrapsRecord
                && fields.Length = 1
                && encoding.HasFlag JsonUnionEncoding.UnwrapSingleFieldCases

            // Whether the record that the case unwraps is written as the layout describes a record of its type. The
            // converter of the union writes the members of the record with its own options of the serializer and
            // with the options of FSharp.SystemTextJson that it has, or with the ones that it finds for the record.
            // The record by itself is written the same way only by a converter that the same converter among the
            // options makes. One that an attribute of the record makes by itself has no naming policy, and another
            // converter among the options has other options.
            let isRecordDescribed () =
                let recordType = fields[0].PropertyType

                match options.Common, writerOf recordType, converterOptionsOf recordType with
                | ValueSome common, Writer.Record _, ValueSome ofRecord ->
                    ofRecord.Common
                    |> ValueOption.exists (fun forRecords -> obj.ReferenceEquals (forRecords, common))
                | _ -> false

            let step (container : ImmutableArray<JsonStep>) (next : JsonStep) : ImmutableArray<JsonStep> = container.Add next

            // The fields at their places.
            let placed (kind : UnionFieldsKind) (places : ImmutableArray<UnionFieldPlace>) : WrittenFields voption =
                ValueSome { Places = places; Kind = kind }

            // The fields as the properties of the object at the end of the steps, which holds the tag as well or not.
            let asProperties (container : ImmutableArray<JsonStep>) (holdsTag : bool) : WrittenFields voption =
                if unwrapsRecord then
                    if not (isRecordDescribed ()) then
                        ValueNone
                    elif holdsTag then
                        ImmutableArray.Create (UnionFieldPlace.Members container)
                        |> placed UnionFieldsKind.RecordMembers
                    else
                        // Without a tag in it the object is the record as it is written anywhere.
                        ImmutableArray.Create (UnionFieldPlace.Value container)
                        |> placed UnionFieldsKind.RecordMembers
                else
                    unionFieldNamesOf options case
                    |> Seq.map (fun name -> UnionFieldPlace.Value (step container (JsonStep.Property name)))
                    |> ImmutableArray.CreateRange
                    |> placed UnionFieldsKind.Properties

            // The fields as the items of the array at the end of the steps, from the index that is given.
            let asItems (container : ImmutableArray<JsonStep>) (first : int) : WrittenFields voption =
                fields
                |> Seq.mapi (fun index _ -> UnionFieldPlace.Value (step container (JsonStep.Item (first + index))))
                |> ImmutableArray.CreateRange
                |> placed UnionFieldsKind.Values

            // The fields as what the property at the end of the steps holds.
            let asFields (container : ImmutableArray<JsonStep>) : WrittenFields voption =
                if unwrapsField then
                    ImmutableArray.Create (UnionFieldPlace.Value container)
                    |> placed UnionFieldsKind.Values
                elif hasNamedFields then
                    asProperties container false
                else
                    asItems container 0

            let written (mark : UnionCaseMark) (writtenFields : WrittenFields voption) : WrittenCase voption =
                writtenFields
                |> ValueOption.map (fun writtenFields -> { Mark = mark; Fields = writtenFields })

            let root = ImmutableArray<JsonStep>.Empty

            if isNullCase then
                written UnionCaseMark.Null (placed UnionFieldsKind.Values ImmutableArray<UnionFieldPlace>.Empty)
            elif
                fields.Length = 0
                && encoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags
            then
                written (UnionCaseMark.Tag (root, tag)) (placed UnionFieldsKind.Values ImmutableArray<UnionFieldPlace>.Empty)
            else
                match int encoding &&& UnionFormatBits with
                // No bit says where the tag is: next to the fields.
                | 0 ->
                    written
                        (UnionCaseMark.Tag (ImmutableArray.Create (JsonStep.Property options.TagName), tag))
                        (asFields (ImmutableArray.Create (JsonStep.Property options.FieldsName)))
                | bits when bits = int JsonUnionEncoding.ExternalTag ->
                    let name = propertyNameOf tag
                    written (UnionCaseMark.Property name) (asFields (ImmutableArray.Create (JsonStep.Property name)))
                | bits when bits = int JsonUnionEncoding.InternalTag ->
                    if hasNamedFields then
                        written
                            (UnionCaseMark.Tag (ImmutableArray.Create (JsonStep.Property options.TagName), tag))
                            (asProperties root true)
                    else
                        written (UnionCaseMark.Tag (ImmutableArray.Create (JsonStep.Item 0), tag)) (asItems root 1)
                | UntaggedBit -> written UnionCaseMark.Unmarked (asProperties root false)
                // Two places for the tag at once: the library fails to write such a union.
                | _ -> ValueNone

        /// <summary>
        /// Whether a case is written as an object with two properties of the name of the tag. FSharp.SystemTextJson
        /// writes one when the options give the property of the fields the name of the tag, and when a field with a
        /// name, or a member of the record whose members the case writes, has that name next to an internal tag.
        /// </summary>
        /// <remarks>
        /// The place of the tag is then the place of a field or on the way to one. A query reads one property of a
        /// name, and nothing says which of the two it is: the tag cannot be told, and neither can the field.
        /// </remarks>
        let writesTagTwice (options : UnionOptions) (case : UnionCaseInfo) : bool =
            match writtenCaseOf options case with
            | ValueSome { Mark = UnionCaseMark.Tag (tagPath, _); Fields = writtenFields } ->
                writtenFields.Places
                |> Seq.exists (fun place ->
                    match place with
                    | UnionFieldPlace.Value path -> beginsWith tagPath path
                    | UnionFieldPlace.Members container ->
                        // The members of the record are properties of the object that holds the tag.
                        let fields = case.GetFields ()
                        let recordType = fields[0].PropertyType

                        FSharpType.GetRecordFields (recordType, AnyAccessibility)
                        |> Array.exists (fun field ->
                            nameOf { Holder = recordType; Member = field }
                            |> ValueOption.exists (fun name -> beginsWith tagPath (container.Add (JsonStep.Property name)))
                        )
                )
            | _ -> false

        /// <summary>
        /// How a case of a union is written by the contract of the union; no value when the layout does not describe
        /// the case. The contract names the converter, and the converters of FSharp.SystemTextJson for unions are told
        /// by their classes; the options that the union is written with say the rest.
        /// </summary>
        /// <remarks>
        /// No case of a union is described when one of its cases is written with the name of the tag twice: the
        /// second property of such a document can hold what is the tag of another case.
        /// </remarks>
        let writtenCaseWith (info : JsonTypeInfo) (case : UnionCaseInfo) : WrittenCase voption =
            let converter = info.Converter.GetType ()

            if isMadeOf typedefof<JsonUnwrappedUnionConverter<_, _>> converter then
                // The class says all of it: the one field of the one case is written in place of the union.
                ValueSome {
                    Mark = UnionCaseMark.Unmarked
                    Fields = {
                        Places = ImmutableArray.Create (UnionFieldPlace.Value ImmutableArray<JsonStep>.Empty)
                        Kind = UnionFieldsKind.Values
                    }
                }
            elif
                isMadeBySerializer info
                && (isMadeOf typedefof<JsonUnionConverter<_>> converter
                    || isMadeOf typedefof<JsonEnumLikeUnionConverter<_>> converter)
            then
                match unionOptionsOf case.DeclaringType with
                | ValueSome options when
                    FSharpType.GetUnionCases (case.DeclaringType, AnyAccessibility)
                    |> Array.exists (writesTagTwice options)
                    ->
                    ValueNone
                | ValueSome options -> writtenCaseOf options case
                | ValueNone -> ValueNone
            else
                ValueNone

        /// <summary>
        /// How the fields are written of the case that a member of a union is a field of; no value when the member is
        /// no field of a case, or when the layout does not describe the case.
        /// </summary>
        /// <param name="info">The contract of the union.</param>
        /// <param name="memberOfDocument">The member, with the union as its holder.</param>
        let unionFieldsOf (info : JsonTypeInfo) (memberOfDocument : DocumentMember) : UnionFieldsKind voption =
            FSharpType.GetUnionCases (memberOfDocument.Holder, AnyAccessibility)
            |> Seq.tryFind (fun case ->
                case.GetFields ()
                |> Array.exists (fun field -> isSameMember field memberOfDocument.Member)
            )
            |> ValueOption.bind (writtenCaseWith info)
            |> ValueOption.map _.Fields.Kind

        /// How a value is written without one, by the rules of who writes the type of its member.
        let encodingOf (struct (memberOfDocument : DocumentMember voption, valueType : Type)) : OptionalEncoding =
            let kind = optionalKindOf valueType

            if not (isWrittenAsItself kind valueType) then
                OptionalEncoding.NotDescribed
            else
                // What is written for the value where it is not left out, by the converter of the type or by the one
                // that a member names.
                let whenWritten = absentWrittenWith (typeInfoOf valueType)

                match memberOfDocument with
                | ValueNone -> whenWritten
                | ValueSome memberOfDocument ->
                    match writerOf memberOfDocument.Holder with
                    | Writer.Serializer info ->
                        match propertyOf info memberOfDocument.Member with
                        // The entries of the extension data are properties of the document itself, and nothing is
                        // written for extension data that is null: no property of its name is null or missing.
                        | ValueSome (struct (property, _)) when property.IsExtensionData -> OptionalEncoding.NotDescribed
                        | ValueSome (struct (property, own)) ->
                            match property.CustomConverter with
                            | null -> absenceInObject property own kind whenWritten
                            // The serializer leaves a null out, or writes it, before it asks the converter of the
                            // member, unless that converter asks for the null: what it writes then is asked of the
                            // serializer. So a reference or a nullable of such a member is described as any other. An
                            // option, a value option or a skippable value is not: the converter writes all of it, and
                            // nothing shows what it writes for one with a value.
                            | converter when kind = OptionalKind.Nullable ->
                                absenceInObject property own kind (absentWrittenWith (ownInfoOf converter property.PropertyType))
                            | _ -> OptionalEncoding.NotDescribed
                        | ValueNone -> OptionalEncoding.NotDescribed
                    | Writer.Record recordOptions ->
                        // Whatever writes the property of a member that FSharp.SystemTextJson never writes decides.
                        if isWrittenByRecord recordOptions memberOfDocument.Member then
                            absenceInRecord recordOptions kind valueType whenWritten
                        else
                            OptionalEncoding.NullOrUndefined
                    | Writer.Union info ->
                        match unionFieldsOf info memberOfDocument with
                        // A named field is written or left out as a field of a record is, with the options that the
                        // union has, which the library reads as it reads the ones of a record.
                        | ValueSome UnionFieldsKind.Properties ->
                            match recordOptionsOf memberOfDocument.Holder with
                            | ValueSome recordOptions -> absenceInRecord recordOptions kind valueType whenWritten
                            | ValueNone -> OptionalEncoding.NotDescribed
                        // An item of the array of the fields is always written, and so is the one field that stands
                        // in place of the fields or of the union.
                        | ValueSome UnionFieldsKind.Values -> whenWritten
                        // The library fails to write a record that is null where a case writes the members of the
                        // record, so no document shows such a record.
                        | ValueSome UnionFieldsKind.RecordMembers
                        | ValueNone -> OptionalEncoding.NotDescribed
                    | Writer.Unknown -> OptionalEncoding.NotDescribed

        /// <summary>
        /// The contract that the serializer writes a value with: the one of its type, or the one of the converter
        /// that the member of the value names. No contract when the layout does not know what writes the value. The
        /// value is asked about by the member that holds it, when a member does, and by its type.
        /// </summary>
        /// <param name="followsConverter">
        /// Whether the contract of a converter that the member names is given. Without it such a member has no
        /// contract: the question is one that the class of a converter does not answer.
        /// </param>
        let valueInfoWith
            (followsConverter : bool)
            (struct (memberOfDocument : DocumentMember voption, valueType : Type))
            : JsonTypeInfo voption =
            match memberOfDocument with
            | ValueNone -> typeInfoOf valueType
            | ValueSome memberOfDocument ->
                match writerOf memberOfDocument.Holder with
                | Writer.Serializer info ->
                    match propertyOf info memberOfDocument.Member with
                    // The entries of the extension data are properties of the document itself, so no property holds
                    // the dictionary.
                    | ValueSome (struct (property, _)) when property.IsExtensionData -> ValueNone
                    // Whatever writes a property that the serializer never writes decides how.
                    | ValueSome (struct (property, own)) when isNeverWritten property own -> ValueNone
                    | ValueSome (struct (property, _)) ->
                        let memberType = property.PropertyType

                        // The converter of a member writes the whole value of the member, also the nullable that
                        // the member is. What it writes for a part of the value, such as the value of an option,
                        // nothing shows.
                        let isWholeValue () =
                            Type.(=) (valueType, memberType)
                            || (
                                match Nullable.GetUnderlyingType memberType with
                                | null -> false
                                | underlying -> Type.(=) (valueType, underlying)
                            )

                        match property.CustomConverter with
                        | null -> typeInfoOf valueType
                        | converter when followsConverter && isWholeValue () -> ownInfoOf converter memberType
                        | _ -> ValueNone
                    | ValueNone -> ValueNone
                | Writer.Record recordOptions ->
                    // FSharp.SystemTextJson does not read the converter that a member of a record names. A member
                    // that it does not write is described by nothing: whatever writes its property decides how.
                    if isWrittenByRecord recordOptions memberOfDocument.Member then
                        typeInfoOf valueType
                    else
                        ValueNone
                | Writer.Union info ->
                    // FSharp.SystemTextJson writes a field of a case with the contract of its type, wherever the field
                    // stands. A field of a case that the layout does not describe is not described either.
                    match unionFieldsOf info memberOfDocument with
                    | ValueSome _ -> typeInfoOf valueType
                    | ValueNone -> ValueNone
                | Writer.Unknown -> ValueNone

        /// The contract that the serializer writes a value with, also the one of a converter that its member names.
        let valueInfoOf (question : struct (DocumentMember voption * Type)) : JsonTypeInfo voption = valueInfoWith true question

        /// <summary>
        /// How an enumeration is written. The serializer is asked to write its values, because nothing else tells:
        /// System.Text.Json has one converter for numbers and for names, which keeps to itself what it writes.
        /// </summary>
        /// <remarks>
        /// Every value that has a name is written, and all of them have to come out as one kind of JSON value: a
        /// converter of the application can write one value as its name and another as a number, and such an
        /// enumeration is written in no one way.
        /// </remarks>
        let enumEncodingOf (struct (_, valueType : Type) as question) : EnumEncoding =
            let enumType =
                match Nullable.GetUnderlyingType valueType with
                | null -> valueType
                | underlying -> underlying

            if not enumType.IsEnum then
                EnumEncoding.NotDescribed
            else
                match valueInfoOf question with
                | ValueNone -> EnumEncoding.NotDescribed
                | ValueSome info ->
                    // The values with a name: a converter of names writes a value without one as its number. An
                    // enumeration that declares no value has only such a value to write.
                    let values = Enum.GetValues enumType

                    let named =
                        if values.Length = 0 then
                            Seq.singleton (Enum.ToObject (enumType, 0))
                        else
                            Seq.cast<obj> values

                    try
                        let kinds =
                            named
                            |> Seq.map (fun value -> JsonSerializer.SerializeToElement(value, info).ValueKind)
                            |> Seq.distinct
                            |> Seq.toArray

                        match kinds with
                        | [| JsonValueKind.Number |] -> EnumEncoding.Number
                        | [| JsonValueKind.String |] -> EnumEncoding.String
                        | _ -> EnumEncoding.NotDescribed
                    with
                    | :? JsonException
                    | :? NotSupportedException ->
                        // The converter refuses a value, as the one of names does when it was made to. These are the
                        // two exceptions that the serializer has for a value that a converter does not write.
                        EnumEncoding.NotDescribed

        /// How a tuple is written: System.Text.Json writes it as an object, FSharp.SystemTextJson as an array.
        let tupleEncodingOf (struct (_, tupleType : Type) as question) : TupleEncoding =
            if not (FSharpType.IsTuple tupleType) then
                TupleEncoding.NotDescribed
            else
                match valueInfoOf question with
                | ValueSome info when info.Kind = JsonTypeInfoKind.Object -> TupleEncoding.Object
                | ValueSome info when isTupleConverter (info.Converter.GetType ()) -> TupleEncoding.Array
                | _ -> TupleEncoding.NotDescribed

        /// <summary>
        /// How a collection is written: as an array when the contract says that the serializer writes its items, or
        /// when a converter of FSharp.SystemTextJson for lists or sets writes it.
        /// </summary>
        let collectionEncodingOf (question : struct (DocumentMember voption * Type)) : CollectionEncoding =
            match valueInfoOf question with
            // The contract of a type whose items the serializer writes names the type of an item.
            | ValueSome info when info.Kind = JsonTypeInfoKind.Enumerable -> CollectionEncoding.Array (nonNull info.ElementType)
            | ValueSome info ->
                let converter = info.Converter.GetType ()

                if
                    isMadeOf typedefof<JsonListConverter<_>> converter
                    || isMadeOf typedefof<JsonSetConverter<_>> converter
                then
                    CollectionEncoding.Array (converter.GetGenericArguments()[0])
                else
                    CollectionEncoding.NotDescribed
            | ValueNone -> CollectionEncoding.NotDescribed

        /// <summary>
        /// How a map is written: as an object when the contract says that the serializer writes it as a dictionary, and
        /// as the converter of FSharp.SystemTextJson that writes it does.
        /// </summary>
        let mapEncodingOf (question : struct (DocumentMember voption * Type)) : MapEncoding =
            match valueInfoOf question with
            // The contract of a dictionary names the types of a key and of a value.
            | ValueSome info when info.Kind = JsonTypeInfoKind.Dictionary ->
                MapEncoding.Object (nonNull info.KeyType, nonNull info.ElementType)
            | ValueSome info ->
                let converter = info.Converter.GetType ()

                if isMadeOf typedefof<JsonMapObjectConverter<_, _>> converter then
                    let arguments = converter.GetGenericArguments ()
                    MapEncoding.Object (arguments[0], arguments[1])
                elif isMadeOf typedefof<JsonMapArrayOfPairsConverter<_, _>> converter then
                    let arguments = converter.GetGenericArguments ()
                    MapEncoding.ArrayOfPairs (arguments[0], arguments[1])
                else
                    MapEncoding.NotDescribed
            | ValueNone -> MapEncoding.NotDescribed

        /// <summary>
        /// How a case of a union is written, by the contract that the union is written with where it is asked about.
        /// </summary>
        let unionCaseEncodingOf (struct (memberOfDocument : DocumentMember voption, case : UnionCaseInfo)) : UnionCaseEncoding =
            match
                valueInfoWith false (struct (memberOfDocument, case.DeclaringType))
                |> ValueOption.bind (fun info -> writtenCaseWith info case)
            with
            | ValueSome written -> UnionCaseEncoding.Written (written.Mark, written.Fields.Places)
            | ValueNone -> UnionCaseEncoding.NotDescribed

        let rememberedName = remembered nameOf
        let rememberedEncoding = remembered encodingOf
        let rememberedEnumEncoding = remembered enumEncodingOf
        let rememberedTupleEncoding = remembered tupleEncodingOf
        let rememberedCollectionEncoding = remembered collectionEncodingOf
        let rememberedMapEncoding = remembered mapEncodingOf
        let rememberedUnionCaseEncoding = remembered unionCaseEncodingOf

        interface IJsonLayout with

            /// <inheritdoc />
            member _.MemberName (memberOfDocument : DocumentMember) : string voption = rememberedName memberOfDocument

            /// <inheritdoc />
            member _.OptionalEncoding (memberOfDocument : DocumentMember voption, valueType : Type) : OptionalEncoding =
                rememberedEncoding (struct (memberOfDocument, valueType))

            /// <inheritdoc />
            member _.EnumEncoding (memberOfDocument : DocumentMember voption, enumType : Type) : EnumEncoding =
                rememberedEnumEncoding (struct (memberOfDocument, enumType))

            /// <inheritdoc />
            member _.TupleEncoding (memberOfDocument : DocumentMember voption, tupleType : Type) : TupleEncoding =
                rememberedTupleEncoding (struct (memberOfDocument, tupleType))

            /// <inheritdoc />
            member _.CollectionEncoding (memberOfDocument : DocumentMember voption, collectionType : Type) : CollectionEncoding =
                rememberedCollectionEncoding (struct (memberOfDocument, collectionType))

            /// <inheritdoc />
            member _.MapEncoding (memberOfDocument : DocumentMember voption, mapType : Type) : MapEncoding =
                rememberedMapEncoding (struct (memberOfDocument, mapType))

            /// <inheritdoc />
            member _.UnionCaseEncoding (memberOfDocument : DocumentMember voption, case : UnionCaseInfo) : UnionCaseEncoding =
                rememberedUnionCaseEncoding (struct (memberOfDocument, case))

    /// <summary>
    /// The layout of the documents that System.Text.Json writes with <paramref name="options"/>, with or without the
    /// converter of FSharp.SystemTextJson among them. Pass the options that the Cosmos DB client serializes with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout reads <paramref name="options"/> when it is made and keeps a copy, so a change of them afterwards
    /// does not reach it. What it finds out about a member is kept for as long as the layout is.
    /// </para>
    /// <para>
    /// The resolver of the contracts, <see cref="P:System.Text.Json.JsonSerializerOptions.TypeInfoResolver"/>, is not
    /// copied: it can be any object, so the layout asks the one that the serializer asks. A change of that resolver
    /// before the layout is asked for the first time reaches the layout as it reaches the serializer. From that
    /// question on a <see cref="T:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver"/> refuses a
    /// change of its <see cref="P:System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver.Modifiers"/>,
    /// as it does after the first document is written, so the layout and the serializer cannot come to disagree
    /// unnoticed. A resolver of the application has to give one answer for a type every time it is asked, which the
    /// serializer needs as well.
    /// </para>
    /// <para>
    /// It describes the members of the types that System.Text.Json writes as objects and of the records that
    /// FSharp.SystemTextJson writes. A member of a type that another converter writes has no name here, and neither
    /// has a member of a record whose contract a resolver of the application made from a converter: nothing shows how
    /// that converter was made.
    /// </para>
    /// <para>
    /// An enumeration, a tuple, a collection and a map are described as System.Text.Json and FSharp.SystemTextJson
    /// write them, by the converter that the options have for the type or by the one that a member names for itself.
    /// What a converter of the application writes is not described, except for an enumeration: the layout has the
    /// serializer write every value of it that has a name, and describes the enumeration when numbers come out for
    /// all of them or strings for all of them. An enumeration whose values come out in both ways is not described,
    /// and neither is one whose converter refuses a value.
    /// </para>
    /// </remarks>
    /// <param name="options">The options of the serializer.</param>
    /// <returns>The layout.</returns>
    let ofSerializerOptions (options : JsonSerializerOptions) : IJsonLayout = SerializerLayout options
