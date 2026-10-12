namespace FSharp.Azure.Cosmos.Quotations

open System
open System.Collections.Concurrent
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
/// What a query has to know about how documents were serialized. The translator asks it for every member that a
/// quotation reads, so that the query reads the property that the serializer wrote.
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
    /// A member that the serializer never writes has a name all the same when the name is declared on it, with
    /// <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>: such a member stands for a property
    /// that something else writes into the document, as a type discriminator or a computed property is.
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
    /// out. No value for an item of a collection or of a tuple, which is always written.
    /// </param>
    /// <param name="valueType">The type of the value, which is the type of the member when there is one.</param>
    abstract OptionalEncoding : memberOfDocument : DocumentMember voption * valueType : Type -> OptionalEncoding

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
        /// A converter that the layout does not know writes the type, or the type is not written as an object.
        | Unknown

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
            | :? ArgumentException -> ValueNone

        /// <summary>
        /// The options that FSharp.SystemTextJson writes a record type with: the ones the type is given in the
        /// overrides, then the ones of the attribute on the type when the options allow that, then the common ones.
        /// The common ones are those of the converter that takes the type, which is the first one among the options
        /// that does, as the serializer chooses it: the options can hold several converters of FSharp.SystemTextJson
        /// for different kinds of types. No value when nothing shows the options of the converter: a converter of
        /// records that stands among the options by itself keeps them to itself, and so does one that an attribute of
        /// the caller makes.
        /// </summary>
        let recordOptionsOf (recordType : Type) : RecordOptions voption =
            let ofAttribute () =
                match recordType.GetCustomAttributes (typeof<IJsonFSharpConverterAttribute>, true) with
                | [||] -> ValueNone
                | attributes -> ValueSome (attributes[0] :?> IJsonFSharpConverterAttribute).Options

            let ofOverrides (common : JsonFSharpOptions) =
                // The library declares the overrides as never null and gives null when there are none.
                match withNull (common.Overrides common) with
                | null -> ValueNone
                | overrides ->
                    match overrides.TryGetValue recordType with
                    | true, given -> ValueSome given
                    | _ when recordType.IsGenericType ->
                        match overrides.TryGetValue (recordType.GetGenericTypeDefinition ()) with
                        | true, given -> ValueSome given
                        | _ -> ValueNone
                    | _ -> ValueNone

            match contract.Converters |> Seq.tryFind _.CanConvert(recordType) with
            // The factory of FSharp.SystemTextJson itself, and no class that an application derives from it: such a
            // class can make the converter of a record with other options than the ones it shows.
            | ValueSome (:? JsonFSharpConverter as factory) when Type.(=) (factory.GetType (), typeof<JsonFSharpConverter>) ->
                let common = factory.Options

                let effective =
                    ofOverrides common
                    |> ValueOption.orElseWith (fun () -> if common.AllowOverride then ofAttribute () else ValueNone)
                    |> ValueOption.defaultValue common

                ValueSome {
                    NamingPolicy = contract.PropertyNamingPolicy
                    IgnoresNull = defaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    SkippableOptionFields = effective.SkippableOptionFields
                    IncludesProperties = effective.IncludeRecordProperties
                    // The options that a type has for itself can say that they inherit the encoding of unions.
                    UnionEncoding =
                        if effective.UnionEncoding.HasFlag JsonUnionEncoding.Inherit then
                            common.UnionEncoding
                        else
                            effective.UnionEncoding
                    AllowsNullFields = effective.AllowNullFields
                }
            | ValueSome _ -> ValueNone
            | ValueNone ->
                // No converter among the options takes the type, so an attribute on the type made the converter. The
                // attribute of FSharp.SystemTextJson shows the options it makes the converter with, and gives it
                // options of the serializer of its own: no naming policy, and nothing is ignored. Any other attribute
                // can make the converter with options that nothing shows.
                ofAttribute ()
                |> ValueOption.map (fun effective -> {
                    NamingPolicy = null
                    IgnoresNull = false
                    SkippableOptionFields = effective.SkippableOptionFields
                    IncludesProperties = effective.IncludeRecordProperties
                    UnionEncoding = effective.UnionEncoding
                    AllowsNullFields = effective.AllowNullFields
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
                    match recordOptionsOf documentType with
                    | ValueSome recordOptions -> Writer.Record recordOptions
                    | ValueNone -> Writer.Unknown
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
                    | Writer.Unknown -> OptionalEncoding.NotDescribed

        let rememberedName = remembered nameOf
        let rememberedEncoding = remembered encodingOf

        interface IJsonLayout with

            /// <inheritdoc />
            member _.MemberName (memberOfDocument : DocumentMember) : string voption = rememberedName memberOfDocument

            /// <inheritdoc />
            member _.OptionalEncoding (memberOfDocument : DocumentMember voption, valueType : Type) : OptionalEncoding =
                rememberedEncoding (struct (memberOfDocument, valueType))

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
    /// </remarks>
    /// <param name="options">The options of the serializer.</param>
    /// <returns>The layout.</returns>
    let ofSerializerOptions (options : JsonSerializerOptions) : IJsonLayout = SerializerLayout options
