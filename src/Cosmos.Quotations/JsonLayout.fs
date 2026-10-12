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
/// <para>
/// The encoding is the kind of JSON value, and not what is written for one value of the enumeration: a naming policy
/// can spell a name, an attribute can declare one, and a converter of the application writes what it likes. So what
/// stands for such a value in a query has to come from the serializer: the encoding does not say it.
/// </para>
/// <para>
/// The layout learns the kind by asking the serializer to write the values that have a name, the smallest number that
/// no name stands for, and a combination of two named values that has no name of its own. An enumeration is described
/// when they all come out as one kind of JSON value, where a number is no contradiction to names: a converter of
/// names writes a value without a name as its number. A converter that writes some other value in a way of its own is
/// not noticed.
/// </para>
/// </remarks>
[<RequireQualifiedAccess; Struct>]
type EnumEncoding =
    /// As the number under the value.
    | Number
    /// <summary>
    /// As a JSON string: the name of the value in the spelling that its converter gives it, or what a converter of the
    /// application writes for it. The converter of System.Text.Json writes the names of the flags for a combination of
    /// flags that it has names for, and the number for any other value that has no name, unless it was made to refuse
    /// such a value.
    /// </summary>
    | String
    /// <summary>
    /// The layout does not describe how the values are written: neither as numbers nor as strings, some as numbers
    /// and others as strings, a value that has no name as a string where the values with a name are numbers, or not
    /// all of them because the converter refuses one.
    /// </summary>
    | NotDescribed

/// <summary>
/// How a tuple is written. A query has to know it to read an item of a tuple that a document holds.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type TupleEncoding =
    /// As a JSON array of the items in their order. A tuple of more than seven items is one array as well.
    | Array
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
    /// Not as an array of its items, or the layout does not know how: an array of bytes is written as one text, a
    /// collection type with derived types is written as an object when the value is one of them, and a converter that
    /// the layout does not know can write anything.
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
    /// <summary>
    /// The layout does not describe how the map is written: a converter that it does not know writes it, or the map
    /// type has derived types, which put the discriminator among the properties of the object.
    /// </summary>
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
/// What tells that a value of a union is of one case. A query has to know it to ask for the values of a case.
/// </summary>
[<RequireQualifiedAccess>]
type UnionCaseMark =
    /// <summary>
    /// The JSON value at the end of <paramref name="path"/> is the text <paramref name="tag"/>. With no step the value
    /// of the union itself is the tag, as for a case without fields that is written as its name alone.
    /// </summary>
    /// <param name="path">The steps from the value of the union to the tag.</param>
    /// <param name="tag">The tag of the case.</param>
    | Tag of path : ImmutableArray<JsonStep> * tag : string
    /// <summary>
    /// Nothing that is written tells the case: the union has one case, whose one field is written in place of the
    /// union.
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
    /// layout does not know writes it, the union is written in a way that the layout does not support, such as one
    /// whose tags are no texts, or the fields of the case cannot be placed, as two that are written under one name.
    /// </summary>
    | NotDescribed

/// <summary>
/// A type that a polymorphic type can be written as, with the discriminator that tells it.
/// </summary>
[<Struct>]
type DerivedType = {
    /// The derived type.
    Type : Type
    /// The text that is written as the discriminator of the type; none when the type is written without one.
    Discriminator : string voption
}

/// <summary>
/// How a type with derived types is written. A query has to know it to ask for the documents of one derived type.
/// </summary>
[<RequireQualifiedAccess; Struct>]
type PolymorphismEncoding =
    /// The type has no derived types that the serializer writes with a discriminator, or has no polymorphism at all.
    | NotPolymorphic
    /// <summary>
    /// The serializer writes a derived type as an object with the discriminator of the type in a property.
    /// </summary>
    /// <param name="discriminatorProperty">The name of the property.</param>
    /// <param name="derivedTypes">The derived types that the type declares.</param>
    | Polymorphic of discriminatorProperty : string * derivedTypes : ImmutableArray<DerivedType>
    /// <summary>
    /// The layout does not describe how the type is written: the serializer refuses it, or a discriminator is a number
    /// and no text.
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
/// gives the layout of System.Text.Json with FSharp.SystemTextJson, for the set-ups that it supports. A layout of
/// another serializer implements this interface.
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
    /// How a value of the enumeration <paramref name="enumType"/> is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This member and the three after it are asked alike. <paramref name="memberOfDocument"/> is the member that
    /// holds the value, when it is a member of a type written as a JSON object: a member can name a converter of its
    /// own, which writes its value and no other, and the layout does not describe what such a converter writes. A
    /// field of a union case, an item of a collection and an item of a tuple have no such member.
    /// </para>
    /// <para>
    /// The type that is asked about is the type of the member, or the type under an option, a value option or a
    /// nullable that the member is.
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
    /// What tells the cases apart is the same for all of them, so a union is described as a whole in that: when the
    /// tags cannot be told (a tag that is no text, two cases that have one tag, a case that is represented by null, a
    /// name that is declared for a field, a case that writes the name of its tag twice), the layout describes none of
    /// the cases, because a document of one case can hold the tag of another.
    /// </para>
    /// <para>
    /// A case alone is not described when its fields cannot be placed: two of them are written under one name, or the
    /// record that the case unwraps is not written as the layout describes a record. The other cases of the union are
    /// described as they are written. Only next to an internal tag, where the members of such a record are written
    /// into the object that holds the tag and one of them can have its name, is the union not described as a whole.
    /// </para>
    /// </remarks>
    /// <param name="memberOfDocument">The member that holds the union, when a member does.</param>
    /// <param name="case">The case.</param>
    abstract UnionCaseEncoding : memberOfDocument : DocumentMember voption * case : UnionCaseInfo -> UnionCaseEncoding

    /// <summary>
    /// How the serializer writes a type that has derived types, with the attributes
    /// <see cref="T:System.Text.Json.Serialization.JsonDerivedTypeAttribute"/> and
    /// <see cref="T:System.Text.Json.Serialization.JsonPolymorphicAttribute"/>.
    /// </summary>
    /// <remarks>
    /// This is what the contract of <paramref name="baseType"/> says. A derived type is written with its discriminator
    /// when it is written as the base type, and without one when it is written as itself; which of the two a query
    /// meets depends on how the application writes its items.
    /// </remarks>
    /// <param name="baseType">The type that declares the derived types.</param>
    abstract Polymorphism : baseType : Type -> PolymorphismEncoding

/// <summary>
/// The layouts of the serializers that <see cref="T:FSharp.Azure.Cosmos.Quotations.IJsonLayout"/> comes with.
/// </summary>
[<RequireQualifiedAccess>]
module JsonLayout =

    /// The members of a type whatever their accessibility: a record of the caller may be internal or private.
    [<Literal>]
    let private AnyAccessibility = BindingFlags.Public ||| BindingFlags.NonPublic

    /// The assembly of FSharp.SystemTextJson, whose own converters are the ones whose way to write is known here.
    let private fsharpSerializerAssembly = typeof<JsonFSharpConverter>.Assembly

    /// <summary>
    /// Whether <paramref name="converterType"/> is one of the converters of FSharp.SystemTextJson for tuples. A
    /// converter of the library is asked for a tuple only when it is such a one: it has a class for two, three and four
    /// items of either kind of tuple and one for every other number.
    /// </summary>
    /// <remarks>
    /// The base class of those converters,
    /// <see cref="T:System.Text.Json.Serialization.BaseJsonTupleConverter`1"/>, is public, and so are the classes of
    /// the library that are derived from it: a converter of an application can be derived from any of them and write a
    /// tuple in a way of its own. So only a class of the assembly of the library itself is one of its converters.
    /// </remarks>
    let private isTupleConverter (converterType : Type) : bool = converterType.Assembly.Equals fsharpSerializerAssembly

    /// <summary>
    /// Whether <paramref name="valueType"/> is made of the generic type <paramref name="definition"/>.
    /// </summary>
    let private isMadeOf (definition : Type) (valueType : Type) : bool =
        valueType.IsGenericType
        && Type.(=) (valueType.GetGenericTypeDefinition (), definition)

    /// <summary>
    /// Whether <paramref name="declaration"/> carries an attribute of the type <paramref name="attributeType"/>: a
    /// type, or a property of a record, which a type cannot inherit it from.
    /// </summary>
    let private isMarked (attributeType : Type) (declaration : MemberInfo) : bool = declaration.IsDefined (attributeType, false)

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
    /// Whether a union type has a case that is represented by <see langword="null"/>. FSharp.SystemTextJson writes
    /// such a case as null before it looks at anything else, which the layout does not describe.
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
        /// Whether the properties of a record that are not its fields are written too.
        IncludesProperties : bool
    }

    /// <summary>
    /// What FSharp.SystemTextJson reads from its options and from the options of the serializer when it writes a
    /// union.
    /// </summary>
    [<Struct>]
    type private UnionOptions = {
        /// How unions are written.
        Encoding : JsonUnionEncoding
        /// The name of the property that holds the tag of a case.
        TagName : string
        /// The name of the property that holds the fields of a case.
        FieldsName : string
        /// The naming policy for a case that declares no name; none when names are written as they are.
        TagNamingPolicy : JsonNamingPolicy | null
        /// The naming policy for a field of a case; none when names are written as they are.
        FieldNamingPolicy : JsonNamingPolicy | null
    }

    /// <summary>
    /// The bits of <see cref="T:System.Text.Json.Serialization.JsonUnionEncoding"/> that choose where the tag is.
    /// </summary>
    [<Literal>]
    let private UnionFormatBits = 0xFE

    /// <summary>
    /// <paramref name="name"/> as <paramref name="policy"/> spells it; as it is without a policy.
    /// </summary>
    let private spelled (policy : JsonNamingPolicy | null) (name : string) : string =
        match policy with
        | null -> name
        | policy -> policy.ConvertName name

    /// <summary>
    /// The tag that FSharp.SystemTextJson writes for a case: the first name of the
    /// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/> of the case, then the name of its
    /// <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>, then the name of the case as
    /// <paramref name="policy"/> spells it. A name that is declared is written as it is. No value for a name that is
    /// a number or a boolean. A case that names one of its fields is refused together with its union, so no tag is
    /// needed of such a case.
    /// </summary>
    let private unionTagOf (policy : JsonNamingPolicy | null) (case : UnionCaseInfo) : string voption =
        let declared =
            case.GetCustomAttributes typeof<JsonNameAttribute>
            |> Seq.cast<JsonNameAttribute>
            |> Seq.tryHead

        match declared with
        | ValueSome attribute ->
            match attribute.Name with
            | JsonName.String text -> ValueSome text
            | JsonName.Int _
            | JsonName.Bool _ -> ValueNone
        | ValueNone ->
            match
                case.GetCustomAttributes typeof<JsonPropertyNameAttribute>
                |> Seq.cast<JsonPropertyNameAttribute>
                |> Seq.tryHead
            with
            | ValueSome attribute -> ValueSome attribute.Name
            | ValueNone -> ValueSome (spelled policy case.Name)

    /// <summary>
    /// Whether a case declares the name of one of its fields, with a
    /// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/> that has a field.
    /// </summary>
    let private declaresFieldNames (case : UnionCaseInfo) : bool =
        case.GetCustomAttributes typeof<JsonNameAttribute>
        |> Seq.cast<JsonNameAttribute>
        |> Seq.exists (fun attribute ->
            match withNull attribute.Field with
            | null -> false
            | _ -> true
        )

    /// <summary>
    /// The names that FSharp.SystemTextJson writes the fields of a case under when it writes them as properties, in
    /// the order of the fields: the name that F# gives a field, as the policy spells it.
    /// </summary>
    let private unionFieldNamesOf (options : UnionOptions) (case : UnionCaseInfo) : string array =
        case.GetFields ()
        |> Array.map (fun field -> spelled options.FieldNamingPolicy field.Name)

    /// <summary>
    /// Who writes the members of a type, which decides the rules that their names follow.
    /// </summary>
    [<RequireQualifiedAccess>]
    type private Writer =
        /// System.Text.Json writes the type as an object, and its contract lists the properties.
        | Serializer of JsonTypeInfo
        /// FSharp.SystemTextJson writes the type as a record, with the common options.
        | Record of RecordOptions
        /// A converter that the layout does not know writes the type, or the type is not written as an object.
        | Unknown

    /// <summary>
    /// Whether FSharp.SystemTextJson writes a member of a record: its fields, whatever their accessibility, and of its
    /// other properties the public ones, when the options or the member say so. It never writes a member that is marked
    /// with <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/>, whatever the condition of the attribute
    /// says.
    /// </summary>
    let private isWrittenByRecord (recordOptions : RecordOptions) (memberOfDocument : MemberInfo) : bool =
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

    /// <summary>
    /// The name that FSharp.SystemTextJson writes a member of a record under: the name that the member declares, and
    /// the name of the member as the naming policy spells it when it declares none. A member that is never written has
    /// the name that it declares, when it declares one: it stands for a property that something else writes.
    /// </summary>
    let private recordMemberNameOf (recordOptions : RecordOptions) (memberOfDocument : MemberInfo) : string voption =
        let declared = declaredNameOf true memberOfDocument

        if isWrittenByRecord recordOptions memberOfDocument then
            declared
            |> ValueOption.orElseWith (fun () -> ValueSome (spelled recordOptions.NamingPolicy memberOfDocument.Name))
        else
            declared

    /// <summary>
    /// The names that FSharp.SystemTextJson writes the members of a record under, one for each member that it writes.
    /// </summary>
    let private writtenNamesOfRecord (recordOptions : RecordOptions) (recordType : Type) : string array =
        recordType.GetProperties (BindingFlags.Instance ||| AnyAccessibility)
        |> Array.where (isWrittenByRecord recordOptions)
        // A member that is written has a name: it declares one or the policy spells one.
        |> Array.map (fun written -> (recordMemberNameOf recordOptions written).Value)

    /// <summary>
    /// Whether no two of <paramref name="names"/> are one text. A document tells properties apart by their exact
    /// characters, and so it tells the tags of a union.
    /// </summary>
    let private areDistinct (names : string seq) : bool =
        let seen = HashSet<string> StringComparer.Ordinal
        names |> Seq.forall seen.Add

    /// How many constants of an enumeration, from the first on, are tried as the parts of a combination. A combination
    /// needs two of them, and an enumeration that has a pair whose combination is no constant has one among its first
    /// constants in all but the most unusual cases. The bound keeps an enumeration of thousands of codes from being
    /// searched pair by pair.
    let private CombinedConstants = 8

    /// The smallest number from zero up that is no constant of an enumeration, as a value of it: a value that a
    /// converter of names has no name for. Nothing when every number under the enumeration is a constant.
    let private undeclaredValueOf (enumType : Type) (constants : obj array) : obj seq =
        // Of one more number than there are constants, at least one is no constant.
        seq { 0 .. constants.Length }
        |> Seq.map (fun number -> Enum.ToObject (enumType, number))
        |> Seq.where (fun value -> not (Enum.IsDefined (enumType, value)))
        |> Seq.truncate 1

    /// The combination of two of the first constants of an enumeration that is no constant itself, as a value of the
    /// enumeration: a converter of flags writes it as the names of the flags, and a converter of another kind writes
    /// it by a rule of its own. Nothing when there is no such pair.
    let private combinationOf (enumType : Type) (constants : obj array) : obj seq =
        let candidates = constants |> Array.truncate CombinedConstants

        seq {
            for first in 0 .. candidates.Length - 1 do
                for second in first + 1 .. candidates.Length - 1 do
                    Enum.Parse (enumType, $"{candidates[first]}, {candidates[second]}")
        }
        |> Seq.where (fun value -> not (Enum.IsDefined (enumType, value)))
        |> Seq.truncate 1

    /// <summary>
    /// The settings of the options that the layout cannot describe documents with, one sentence for each; none when
    /// the layout supports the options.
    /// </summary>
    /// <remarks>
    /// A set-up that is not listed here is one that the layout has not been compared with the serializer for. The
    /// converters of the application that stand among the options are no reason to refuse: a type that one of them
    /// writes is not described, and the layout says so when it is asked about the type.
    /// </remarks>
    let private refusalsOf (options : JsonSerializerOptions) : ImmutableArray<string> =
        let reasons = ImmutableArray.CreateBuilder<string>()

        let factories =
            options.Converters
            |> Seq.where (fun converter -> converter :? JsonFSharpConverter)
            |> Seq.cast<JsonFSharpConverter>
            |> Seq.toArray

        match factories with
        | [||] ->
            reasons.Add
                "FSharp.SystemTextJson is not among the converters of the options, and it writes the records, unions, tuples and options of F#."
        | [| factory |] ->
            if not (Type.(=) (factory.GetType (), typeof<JsonFSharpConverter>)) then
                reasons.Add
                    "The converter of FSharp.SystemTextJson is of a class that an application derived from it, which can make converters with options that it does not show."

            let common = factory.Options
            let encoding = common.UnionEncoding

            if not (common.Types.HasFlag JsonFSharpTypes.All) then
                reasons.Add "FSharp.SystemTextJson has to write every kind of type of F#: its options say JsonFSharpTypes.All."

            if not (encoding.HasFlag JsonUnionEncoding.UnwrapOption) then
                reasons.Add
                    "UnwrapOption is not set, so an option is written as a union with the name of its case, and not as its value."

            match int encoding &&& UnionFormatBits with
            | 0
            | 4 -> ()
            | _ ->
                reasons.Add
                    "Only AdjacentTag and InternalTag are supported places for the tag of a union: ExternalTag and Untagged are not."

            if encoding.HasFlag JsonUnionEncoding.UnwrapSingleFieldCases then
                reasons.Add "UnwrapSingleFieldCases is set, which the layout does not support."

            if encoding.HasFlag JsonUnionEncoding.UnionFieldNamesFromTypes then
                reasons.Add "UnionFieldNamesFromTypes is set, which the layout does not support."

            if
                int encoding &&& UnionFormatBits = 0
                && String.Equals (common.UnionTagName, common.UnionFieldsName, StringComparison.Ordinal)
            then
                reasons.Add
                    $"The tag and the fields of a union are written under one name, %s{common.UnionTagName}: the later property hides the tag."
        | _ ->
            reasons.Add
                "More than one converter of FSharp.SystemTextJson is among the converters of the options, and the layout reads the options of one."

        if not (isNull options.ReferenceHandler) then
            reasons.Add
                "The options handle references: ReferenceHandler.Preserve adds the properties $id and $ref to documents, and ReferenceHandler.IgnoreCycles writes a cycle as null."

        if options.NumberHandling.HasFlag JsonNumberHandling.WriteAsString then
            reasons.Add "The options write numbers as texts."

        if options.NumberHandling.HasFlag JsonNumberHandling.AllowNamedFloatingPointLiterals then
            reasons.Add "The options write the numbers that are not finite, NaN and the infinities, as texts."

        if options.IgnoreReadOnlyProperties then
            reasons.Add "The options ignore read-only properties."

        if options.IgnoreReadOnlyFields then
            reasons.Add "The options ignore read-only fields."

        if not (isNull options.DictionaryKeyPolicy) then
            reasons.Add
                "The options have a policy for the keys of dictionaries, which changes the name of the property that a key is written as."

        reasons.ToImmutable ()

    /// <summary>
    /// The layout of what System.Text.Json writes with the options it is made of, with the converter of
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
        /// The options that FSharp.SystemTextJson writes a record or a union type with, when they are the common ones:
        /// the options of the converter that takes the type, which is the first one among the options that does, as
        /// the serializer chooses it. No value when the type has options of its own, in the overrides or in an
        /// attribute that the options allow, or when another converter writes it: such a type is not described.
        /// </summary>
        let commonOptionsOf (valueType : Type) : JsonFSharpOptions voption =
            let hasOptionsOfItsOwn (common : JsonFSharpOptions) =
                let isOverridden =
                    // The library declares the overrides as never null and gives null when there are none.
                    match withNull (common.Overrides common) with
                    | null -> false
                    | overrides ->
                        overrides.ContainsKey valueType
                        || (valueType.IsGenericType
                            && overrides.ContainsKey (valueType.GetGenericTypeDefinition ()))

                isOverridden
                || (common.AllowOverride
                    && isMarked typeof<IJsonFSharpConverterAttribute> valueType)

            match contract.Converters |> Seq.tryFind _.CanConvert(valueType) with
            // The one factory of FSharp.SystemTextJson among the options, of that class itself: the options are refused
            // when a class that an application derives from it stands among them, because such a class can make the
            // converter of a record with other options than the ones it shows.
            | ValueSome (:? JsonFSharpConverter as factory) ->
                let common = factory.Options

                if hasOptionsOfItsOwn common then
                    ValueNone
                else
                    ValueSome common
            | _ -> ValueNone

        /// What FSharp.SystemTextJson reads when it writes a union type, when it writes it with the common options.
        let unionOptionsOf (unionType : Type) : UnionOptions voption =
            commonOptionsOf unionType
            |> ValueOption.map (fun common -> {
                Encoding = common.UnionEncoding
                TagName = common.UnionTagName
                FieldsName = common.UnionFieldsName
                TagNamingPolicy = common.UnionTagNamingPolicy
                FieldNamingPolicy =
                    match withNull common.UnionFieldNamingPolicy with
                    | null -> contract.PropertyNamingPolicy
                    | policy -> policy
            })

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
                    match commonOptionsOf documentType with
                    | ValueSome common ->
                        let recordOptions = {
                            NamingPolicy = contract.PropertyNamingPolicy
                            IncludesProperties = common.IncludeRecordProperties
                        }

                        // Two members that are written under one name, which the policy can make of two names, cannot be
                        // told apart in a document.
                        if areDistinct (writtenNamesOfRecord recordOptions documentType) then
                            Writer.Record recordOptions
                        else
                            Writer.Unknown
                    | ValueNone -> Writer.Unknown
                | _ -> Writer.Unknown
            )

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
        /// Whether System.Text.Json never writes a member of an object: the member has no getter the serializer may
        /// call. A member that is marked with
        /// <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/> is one.
        /// </summary>
        let isNeverWritten (property : JsonPropertyInfo) : bool =
            match property.Get with
            | null -> true
            | _ -> false

        /// The name of a member, by the rules of who writes its type.
        let nameOf (memberOfDocument : DocumentMember) : string voption =
            match writerOf memberOfDocument.Holder with
            | Writer.Serializer info ->
                match propertyOf info memberOfDocument.Member with
                | ValueSome (struct (property, _)) when property.IsExtensionData -> ValueNone
                | ValueSome (struct (property, own)) when isNeverWritten property -> declaredNameOf false own
                | ValueSome (struct (property, _)) -> ValueSome property.Name
                | ValueNone -> ValueNone
            | Writer.Record recordOptions -> recordMemberNameOf recordOptions memberOfDocument.Member
            | Writer.Unknown -> ValueNone

        /// <summary>
        /// The contract that the serializer writes a value with: the one of its type. No contract when the layout does
        /// not know what writes the value, or when the member that holds the value names a converter of its own: such
        /// a converter writes the whole value of the member, and nothing shows what it writes. The value is asked
        /// about by the member that holds it, when a member does, and by its type.
        /// </summary>
        let valueInfoOf (struct (memberOfDocument : DocumentMember voption, valueType : Type)) : JsonTypeInfo voption =
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
                    | ValueSome (struct (property, _)) when isNeverWritten property -> ValueNone
                    | ValueSome (struct (property, _)) ->
                        match property.CustomConverter with
                        | null -> typeInfoOf valueType
                        | _ -> ValueNone
                    | ValueNone -> ValueNone
                | Writer.Record recordOptions ->
                    // FSharp.SystemTextJson does not read the converter that a member of a record names. A member
                    // that it does not write is described by nothing: whatever writes its property decides how.
                    if isWrittenByRecord recordOptions memberOfDocument.Member then
                        typeInfoOf valueType
                    else
                        ValueNone
                | Writer.Unknown -> ValueNone

        /// <summary>
        /// How an enumeration is written. The serializer is asked to write its values, because nothing else tells:
        /// System.Text.Json has one converter for numbers and for names, which keeps to itself what it writes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every value that has a name is written, and all of them have to come out as one kind of JSON value: a
        /// converter of the application can write one value as its name and another as a number, and such an
        /// enumeration is written in no one way.
        /// </para>
        /// <para>
        /// A value of an enumeration can be any number under it, and a converter of names writes one that has no name
        /// as its number. So the serializer is asked for two such values as well: the smallest number that no constant
        /// has, and a combination of two constants that is no constant either. A number contradicts nothing, and
        /// anything else has to be the kind that the values with a name come out as. A converter that writes some
        /// other value in a way of its own is not noticed. A value that a converter refuses is no value of a
        /// document, so the refusal of one of the two is no contradiction.
        /// </para>
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
                    // The kind of JSON value that the serializer writes for a value; none when the converter refuses
                    // the value, as the one of names does when it was made to. These are the two exceptions that the
                    // serializer has for a value that a converter does not write.
                    let kindOf (value : obj) : JsonValueKind voption =
                        try
                            ValueSome (JsonSerializer.SerializeToElement(value, info).ValueKind)
                        with
                        | :? JsonException
                        | :? NotSupportedException -> ValueNone

                    // Whether a value that has no name comes out as the values with a name do, or as a number.
                    let isWrittenLike (named : JsonValueKind) (value : obj) : bool =
                        match kindOf value with
                        | ValueNone
                        | ValueSome JsonValueKind.Number -> true
                        | ValueSome written -> written = named

                    let constants = Seq.cast<obj>(Enum.GetValues enumType) |> Seq.toArray

                    // The values with a name: a converter of names writes a value without one as its number. An
                    // enumeration that declares no value has only such a value to write.
                    let named =
                        if constants.Length = 0 then
                            [| Enum.ToObject (enumType, 0) |]
                        else
                            constants

                    let unnamed = [| yield! undeclaredValueOf enumType constants; yield! combinationOf enumType constants |]

                    let kinds = named |> Seq.map kindOf |> Seq.distinct |> Seq.toArray

                    match kinds with
                    | [| ValueSome JsonValueKind.Number |] when unnamed |> Seq.forall (isWrittenLike JsonValueKind.Number) ->
                        EnumEncoding.Number
                    | [| ValueSome JsonValueKind.String |] when unnamed |> Seq.forall (isWrittenLike JsonValueKind.String) ->
                        EnumEncoding.String
                    | _ -> EnumEncoding.NotDescribed

        /// How a tuple is written: FSharp.SystemTextJson writes it as an array.
        let tupleEncodingOf (struct (_, tupleType : Type) as question) : TupleEncoding =
            if not (FSharpType.IsTuple tupleType) then
                TupleEncoding.NotDescribed
            else
                match valueInfoOf question with
                | ValueSome info when isTupleConverter (info.Converter.GetType ()) -> TupleEncoding.Array
                | _ -> TupleEncoding.NotDescribed

        /// <summary>
        /// How a collection is written: as an array when the contract says that the serializer writes its items, or
        /// when a converter of FSharp.SystemTextJson for lists or sets writes it.
        /// </summary>
        /// <remarks>
        /// A collection type with derived types is not described: a value that is written as one of them is an object
        /// with the discriminator and the items under <c>$values</c>.
        /// </remarks>
        let collectionEncodingOf (question : struct (DocumentMember voption * Type)) : CollectionEncoding =
            match valueInfoOf question with
            // The contract of a type whose items the serializer writes names the type of an item.
            | ValueSome info when
                info.Kind = JsonTypeInfoKind.Enumerable
                && isNull info.PolymorphismOptions
                ->
                CollectionEncoding.Array (nonNull info.ElementType)
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
        /// <remarks>
        /// A dictionary type with derived types is not described: a value that is written as one of them has the
        /// discriminator among the properties of the object.
        /// </remarks>
        let mapEncodingOf (question : struct (DocumentMember voption * Type)) : MapEncoding =
            match valueInfoOf question with
            // The contract of a dictionary names the types of a key and of a value.
            | ValueSome info when
                info.Kind = JsonTypeInfoKind.Dictionary
                && isNull info.PolymorphismOptions
                ->
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
        /// The one field of a case that is a record that the options unwrap: its members are written in place of the
        /// fields. No type when the case does not write its fields that way.
        /// </summary>
        let unwrappedRecordOf (options : UnionOptions) (case : UnionCaseInfo) : Type voption =
            match case.GetFields () with
            | [| field |] when
                FSharpType.IsRecord (field.PropertyType, AnyAccessibility)
                && options.Encoding.HasFlag JsonUnionEncoding.UnwrapRecordCases
                ->
                ValueSome field.PropertyType
            | _ -> ValueNone

        /// <summary>
        /// Whether FSharp.SystemTextJson writes a case as an object that has the name of the tag twice: next to an
        /// internal tag, a field with that name, or a member with that name of the record whose members the case
        /// writes. The library writes such a document and reads it back, but a query reads one property of a name,
        /// and nothing says which of the two it is. A case that writes the members of a record that the layout does not
        /// describe is taken to do so: the union writes them under names that nothing shows.
        /// </summary>
        let repeatsTag (options : UnionOptions) (case : UnionCaseInfo) : bool =
            let isInternalTag = int options.Encoding &&& UnionFormatBits = int JsonUnionEncoding.InternalTag

            if
                not (
                    isInternalTag
                    && options.Encoding.HasFlag JsonUnionEncoding.NamedFields
                )
            then
                false
            else
                let namesNextToTag =
                    match unwrappedRecordOf options case with
                    | ValueSome recordType ->
                        match writerOf recordType with
                        | Writer.Record recordOptions -> ValueSome (writtenNamesOfRecord recordOptions recordType)
                        | _ -> ValueNone
                    | ValueNone -> ValueSome (unionFieldNamesOf options case)

                match namesNextToTag with
                | ValueSome names ->
                    names
                    |> Array.exists (fun name -> String.Equals (name, options.TagName, StringComparison.Ordinal))
                | ValueNone -> true

        /// <summary>
        /// Whether the layout describes a union: no case is represented by null, every tag is a text and no two cases
        /// have the same one, no case declares the names of its fields, and no case writes the name of the tag twice. A
        /// union is described as a whole, because a document of one case can hold the tag of another.
        /// </summary>
        let isUnionDescribed (options : UnionOptions) (unionType : Type) : bool =
            let cases = FSharpType.GetUnionCases (unionType, AnyAccessibility)
            let tags = cases |> Array.map (unionTagOf options.TagNamingPolicy)

            not (hasNullCase unionType)
            && tags |> Array.forall _.IsSome
            // Two cases with one tag are written with one mark: a name that two cases declare, or that a naming policy
            // makes of two names.
            && areDistinct (tags |> Array.map _.Value)
            && cases
               |> Array.forall (fun case ->
                   not (declaresFieldNames case)
                   && not (repeatsTag options case)
               )

        /// <summary>
        /// How FSharp.SystemTextJson writes a case of a union with the common options. The rules are read from the
        /// source of the library and compared with what it writes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A case without fields is its tag alone when such tags are unwrapped. Otherwise the encoding decides where
        /// the tag is: in a property next to the property of the fields, or as the first item or a property among the
        /// fields.
        /// </para>
        /// <para>
        /// The fields are an array of their values or an object with a property for each. A case whose one field is a
        /// record writes the members of the record in place of the fields where the encoding unwraps records. The
        /// converter of the union writes such a record with the options that it has, so the case is described only
        /// when the record by itself is written by FSharp.SystemTextJson with the same options: then the object is
        /// the record as it is written anywhere, and
        /// <see cref="M:FSharp.Azure.Cosmos.Quotations.IJsonLayout.MemberName(FSharp.Azure.Cosmos.Quotations.DocumentMember)"/>
        /// names its members. Only next to an internal tag is the object more than the record.
        /// </para>
        /// </remarks>
        let writtenCaseOf (options : UnionOptions) (case : UnionCaseInfo) (tag : string) : UnionCaseEncoding =
            let encoding = options.Encoding
            let fields = case.GetFields ()
            let hasNamedFields = encoding.HasFlag JsonUnionEncoding.NamedFields
            let root = ImmutableArray<JsonStep>.Empty
            let step (container : ImmutableArray<JsonStep>) (next : JsonStep) : ImmutableArray<JsonStep> = container.Add next

            // The fields as the properties of the object at the end of the steps, which holds the tag as well or not;
            // no places when the record that the case unwraps is not written as the layout describes a record, and
            // when two fields are written under one name.
            let asProperties (container : ImmutableArray<JsonStep>) (holdsTag : bool) : ImmutableArray<UnionFieldPlace> voption =
                match unwrappedRecordOf options case with
                | ValueSome recordType ->
                    match writerOf recordType with
                    | Writer.Record _ when holdsTag -> ValueSome (ImmutableArray.Create (UnionFieldPlace.Members container))
                    // Without a tag in it the object is the record as it is written anywhere.
                    | Writer.Record _ -> ValueSome (ImmutableArray.Create (UnionFieldPlace.Value container))
                    | _ -> ValueNone
                | ValueNone ->
                    let names = unionFieldNamesOf options case

                    // Two fields whose names the naming policy spells alike are written under one name, and a document
                    // cannot tell them apart.
                    if areDistinct names then
                        names
                        |> Seq.map (fun name -> UnionFieldPlace.Value (step container (JsonStep.Property name)))
                        |> ImmutableArray.CreateRange
                        |> ValueSome
                    else
                        ValueNone

            // The fields as the items of the array at the end of the steps, from the index that is given.
            let asItems (container : ImmutableArray<JsonStep>) (first : int) : ImmutableArray<UnionFieldPlace> voption =
                fields
                |> Seq.mapi (fun index _ -> UnionFieldPlace.Value (step container (JsonStep.Item (first + index))))
                |> ImmutableArray.CreateRange
                |> ValueSome

            let written (path : ImmutableArray<JsonStep>) (places : ImmutableArray<UnionFieldPlace> voption) : UnionCaseEncoding =
                match places with
                | ValueSome places -> UnionCaseEncoding.Written (UnionCaseMark.Tag (path, tag), places)
                | ValueNone -> UnionCaseEncoding.NotDescribed

            if
                fields.Length = 0
                && encoding.HasFlag JsonUnionEncoding.UnwrapFieldlessTags
            then
                written root (ValueSome ImmutableArray<UnionFieldPlace>.Empty)
            else
                match int encoding &&& UnionFormatBits with
                // No bit says where the tag is: next to the fields.
                | 0 ->
                    let container = ImmutableArray.Create (JsonStep.Property options.FieldsName)

                    written
                        (ImmutableArray.Create (JsonStep.Property options.TagName))
                        (if hasNamedFields then
                             asProperties container false
                         else
                             asItems container 0)
                | bits when bits = int JsonUnionEncoding.InternalTag ->
                    if hasNamedFields then
                        written (ImmutableArray.Create (JsonStep.Property options.TagName)) (asProperties root true)
                    else
                        written (ImmutableArray.Create (JsonStep.Item 0)) (asItems root 1)
                // Another place for the tag: the options are refused when the layout is made.
                | _ -> UnionCaseEncoding.NotDescribed

        /// <summary>
        /// How a case of a union is written. The contract of the union names the converter, and the converters of
        /// FSharp.SystemTextJson for unions are told by their classes; the options that the union is written with
        /// say the rest.
        /// </summary>
        let unionCaseEncodingOf (struct (memberOfDocument : DocumentMember voption, case : UnionCaseInfo)) : UnionCaseEncoding =
            let unionType = case.DeclaringType

            match valueInfoOf (struct (memberOfDocument, unionType)) with
            | ValueNone -> UnionCaseEncoding.NotDescribed
            | ValueSome info ->
                let converter = info.Converter.GetType ()

                if isMadeOf typedefof<JsonUnwrappedUnionConverter<_, _>> converter then
                    // The class says all of it: the one field of the one case is written in place of the union.
                    UnionCaseEncoding.Written (
                        UnionCaseMark.Unmarked,
                        ImmutableArray.Create (UnionFieldPlace.Value ImmutableArray<JsonStep>.Empty)
                    )
                elif
                    isMadeBySerializer info
                    && (isMadeOf typedefof<JsonUnionConverter<_>> converter
                        || isMadeOf typedefof<JsonEnumLikeUnionConverter<_>> converter)
                then
                    match unionOptionsOf unionType with
                    | ValueSome options when isUnionDescribed options unionType ->
                        match unionTagOf options.TagNamingPolicy case with
                        | ValueSome tag -> writtenCaseOf options case tag
                        | ValueNone -> UnionCaseEncoding.NotDescribed
                    | _ -> UnionCaseEncoding.NotDescribed
                else
                    UnionCaseEncoding.NotDescribed

        /// <summary>
        /// How a type with derived types is written: what the contract of the type says of its polymorphism.
        /// </summary>
        let polymorphismOf (baseType : Type) : PolymorphismEncoding =
            match typeInfoOf baseType with
            | ValueNone -> PolymorphismEncoding.NotDescribed
            | ValueSome info ->
                match info.PolymorphismOptions with
                | null -> PolymorphismEncoding.NotPolymorphic
                | polymorphism ->
                    let derived =
                        polymorphism.DerivedTypes
                        |> Seq.map (fun derived ->
                            match derived.TypeDiscriminator with
                            | null -> ValueSome { Type = derived.DerivedType; Discriminator = ValueNone }
                            | :? string as text -> ValueSome { Type = derived.DerivedType; Discriminator = ValueSome text }
                            | _ -> ValueNone
                        )
                        |> Seq.toArray

                    if derived |> Array.exists _.IsNone then
                        PolymorphismEncoding.NotDescribed
                    else
                        PolymorphismEncoding.Polymorphic (
                            polymorphism.TypeDiscriminatorPropertyName,
                            derived |> Seq.map _.Value |> ImmutableArray.CreateRange
                        )

        let rememberedName = remembered nameOf
        let rememberedEnumEncoding = remembered enumEncodingOf
        let rememberedUnionCaseEncoding = remembered unionCaseEncodingOf

        interface IJsonLayout with

            /// <inheritdoc />
            member _.MemberName (memberOfDocument : DocumentMember) : string voption = rememberedName memberOfDocument

            /// <inheritdoc />
            member _.EnumEncoding (memberOfDocument : DocumentMember voption, enumType : Type) : EnumEncoding =
                rememberedEnumEncoding (struct (memberOfDocument, enumType))

            /// <inheritdoc />
            member _.TupleEncoding (memberOfDocument : DocumentMember voption, tupleType : Type) : TupleEncoding =
                tupleEncodingOf (struct (memberOfDocument, tupleType))

            /// <inheritdoc />
            member _.CollectionEncoding (memberOfDocument : DocumentMember voption, collectionType : Type) : CollectionEncoding =
                collectionEncodingOf (struct (memberOfDocument, collectionType))

            /// <inheritdoc />
            member _.MapEncoding (memberOfDocument : DocumentMember voption, mapType : Type) : MapEncoding =
                mapEncodingOf (struct (memberOfDocument, mapType))

            /// <inheritdoc />
            member _.UnionCaseEncoding (memberOfDocument : DocumentMember voption, case : UnionCaseInfo) : UnionCaseEncoding =
                rememberedUnionCaseEncoding (struct (memberOfDocument, case))

            /// <inheritdoc />
            member _.Polymorphism (baseType : Type) : PolymorphismEncoding = polymorphismOf baseType

    /// <summary>
    /// The layout of the documents that System.Text.Json writes with <paramref name="options"/> and the converter of
    /// FSharp.SystemTextJson, or the reasons why the layout cannot describe them. Pass the options that the Cosmos DB
    /// client serializes with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout supports the set-ups that it has been compared with the serializer for, and refuses the others
    /// instead of describing them wrongly: a query that reads a property that is not there returns nothing, and no
    /// error says why. It is made when
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// one converter of FSharp.SystemTextJson, of that class itself, is among the options, and it writes every kind
    /// of type of F#;
    /// </description></item>
    /// <item><description>
    /// the unions are written with an adjacent or an internal tag, an option as its value (<c>UnwrapOption</c>), and
    /// without the flags <c>UnwrapSingleFieldCases</c> and <c>UnionFieldNamesFromTypes</c>, and an adjacent tag is
    /// written under another name than the fields;
    /// </description></item>
    /// <item><description>
    /// the options neither handle references nor write numbers, those that are not finite included, as texts nor ignore
    /// read-only members, and they have no policy for the keys of dictionaries, which would change the name of the
    /// property that a key is written as.
    /// </description></item>
    /// </list>
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
    /// FSharp.SystemTextJson writes with its common options: a record or a union that has options of its own, in the
    /// overrides or in an attribute, is not described. A member of a type that another converter writes has no name
    /// here, and neither has a member of a record whose contract a resolver of the application made from a
    /// converter: nothing shows how that converter was made.
    /// </para>
    /// <para>
    /// An enumeration, a tuple, a collection and a map are described as System.Text.Json and FSharp.SystemTextJson
    /// write them, by the converter that the options have for the type. What a converter of the application writes is
    /// not described, except for an enumeration: the layout has the serializer write every value of it that has a
    /// name, the smallest number that no name stands for and a combination of two named values that has no name of its
    /// own, and describes the enumeration when numbers come out for all the values that have a name or strings for all
    /// of them, and the other two come out as the same kind or as numbers. An enumeration whose named values come out
    /// in both ways is not described, nor is one whose converter refuses a named value, nor is one whose converter
    /// writes one of the other two as another kind than the named values. A converter that refuses one of the other
    /// two is no reason: such a value cannot be in a document. A converter that writes some other value in a way of
    /// its own is not noticed. A member that names a converter of its own is not described either.
    /// </para>
    /// </remarks>
    /// <param name="options">The options of the serializer.</param>
    /// <returns>The layout, or one sentence for every setting of the options that the layout does not support.</returns>
    let ofSerializerOptions (options : JsonSerializerOptions) : Result<IJsonLayout, ImmutableArray<string>> =
        let refusals = refusalsOf options

        if refusals.IsEmpty then
            Result.Ok (SerializerLayout options :> IJsonLayout)
        else
            Result.Error refusals
