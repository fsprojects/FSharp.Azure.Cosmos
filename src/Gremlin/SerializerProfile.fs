namespace FSharp.Gremlin

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata

open FSharp.Azure.Cosmos

/// How the serializer writes a value of a type, as far as the graph model has to know it.
[<RequireQualifiedAccess>]
type internal WrittenAs =
    /// A JSON value that is no array: a string, a number or a Boolean, as the graph model hopes. The encoder of the
    /// values checks it for each value, because a converter can write anything.
    | Value
    /// A JSON array with items of the type.
    | Array of itemType : Type
    /// A JSON object or an array that is not a list of values; the text says what the serializer writes.
    | Object of description : string
    /// The serializer refuses the type.
    | Unknown

/// Who writes the fields of a record type.
[<RequireQualifiedAccess>]
type internal RecordWriter =
    /// The record converter of FSharp.SystemTextJson, with the options that are common to all types.
    | Library
    /// <summary>
    /// A converter that tells the names that it writes the fields under, by the names of the fields, through
    /// <see cref="T:FSharp.Azure.Cosmos.IRecordFieldNames"/>. An example is the record converter of
    /// FSharp.Azure.Cosmos.Json. A field that the converter never writes has no name.
    /// </summary>
    | Described of fieldNames : IReadOnlyDictionary<string, string>
    /// The record converter of FSharp.SystemTextJson, with options of the type itself.
    | OwnOptions
    /// Another converter, or no record converter.
    | Other

/// <summary>
/// What the graph model reads from the options of the serializer: a read-only copy of the options, which the model asks
/// for contracts, and the converter of FSharp.SystemTextJson.
/// </summary>
[<NoComparison; NoEquality>]
type internal SerializerProfile = {
    /// A read-only copy of the options of the caller.
    Contract : JsonSerializerOptions
    /// The one converter of FSharp.SystemTextJson among the options of the caller, which the gate checked.
    Converter : JsonFSharpConverter
}

/// <summary>
/// The rules of System.Text.Json and FSharp.SystemTextJson that the graph model depends on. They are the rules of the
/// narrow layout of the Quotations library (pull request 65): both libraries accept the same options and name the
/// members of a record alike.
/// </summary>
[<RequireQualifiedAccess>]
module internal SerializerProfile =

    /// Whether valueType is made of the generic type definition.
    let isMadeOf (definition : Type) (valueType : Type) : bool =
        valueType.IsGenericType
        && Type.(=) (valueType.GetGenericTypeDefinition (), definition)

    let private fsharpConvertersOf (options : JsonSerializerOptions) : JsonFSharpConverter array =
        options.Converters
        |> Seq.filter (fun converter -> converter :? JsonFSharpConverter)
        |> Seq.cast<JsonFSharpConverter>
        |> Seq.toArray

    /// <summary>
    /// The number of members whose attributes <paramref name="common"/> replace by the name of the member
    /// (<c>WithOverrideMembers</c>); no value when the model cannot read them. FSharp.SystemTextJson 1.4.36 keeps them
    /// in an internal record and shows no property for them, so the model reads them by reflection. When a version of
    /// the library keeps them in another place, the gate refuses all options, and the tests of the accepted set-ups
    /// fail.
    /// </summary>
    let private overriddenMembersOf (common : JsonFSharpOptions) : int voption =
        let flags =
            BindingFlags.Instance
            ||| BindingFlags.Public
            ||| BindingFlags.NonPublic

        match typeof<JsonFSharpOptions>.GetProperty("Record", flags) with
        | null -> ValueNone
        | recordProperty ->
            match recordProperty.GetValue common with
            | null -> ValueNone
            | record ->
                match record.GetType().GetProperty("OverrideMembers", flags) with
                | null -> ValueNone
                | membersProperty ->
                    match membersProperty.GetValue record with
                    | :? IDictionary<string, seq<Attribute>> as members -> ValueSome members.Count
                    | _ -> ValueNone

    /// <summary>
    /// The reasons why the graph model cannot work with <paramref name="options"/>; empty when it can. A set-up that is
    /// not refused here is one that the model has been compared with the serializer for.
    /// </summary>
    let refusalsOf (options : JsonSerializerOptions) : ImmutableArray<string> =
        let reasons = ImmutableArray.CreateBuilder<string>()

        match fsharpConvertersOf options with
        | [||] ->
            reasons.Add
                "FSharp.SystemTextJson is not among the converters of the options, and the graph model needs it to write the records of F#."
        | [| converter |] ->
            if not (Type.(=) (converter.GetType (), typeof<JsonFSharpConverter>)) then
                reasons.Add
                    "The converter of FSharp.SystemTextJson is of a class that an application derived from it, which can write records with other options than the ones it shows."

            let common = converter.Options

            if not (common.Types.HasFlag JsonFSharpTypes.All) then
                reasons.Add
                    "FSharp.SystemTextJson has to write every kind of type of F#: its options must have JsonFSharpTypes.All."

            if not (common.UnionEncoding.HasFlag JsonUnionEncoding.UnwrapOption) then
                reasons.Add
                    "UnwrapOption is not set, so an option is written as a union with the name of its case, and not as its value."

            if common.IncludeRecordProperties then
                reasons.Add
                    "IncludeRecordProperties is set, so FSharp.SystemTextJson writes the properties of a record that are no field, and a graph stores only the fields."

            match overriddenMembersOf common with
            | ValueSome 0 -> ()
            | ValueSome _ ->
                reasons.Add
                    "The options of FSharp.SystemTextJson replace the attributes of members by their names (WithOverrideMembers), and the graph model reads the names from the attributes of the members."
            | ValueNone ->
                reasons.Add
                    "The graph model cannot read whether the options of FSharp.SystemTextJson replace the attributes of members (WithOverrideMembers), because this version of FSharp.SystemTextJson keeps them in another place."
        | _ ->
            reasons.Add
                "More than one converter of FSharp.SystemTextJson is among the converters of the options, and the graph model reads the options of one."

        match options.ReferenceHandler with
        | null -> ()
        | _ ->
            reasons.Add
                "The options handle references: ReferenceHandler.Preserve adds the properties $id and $ref, and ReferenceHandler.IgnoreCycles writes a cycle as null."

        if options.NumberHandling.HasFlag JsonNumberHandling.WriteAsString then
            reasons.Add "The options write numbers as texts, so the graph would compare them as texts."

        if options.NumberHandling.HasFlag JsonNumberHandling.AllowNamedFloatingPointLiterals then
            reasons.Add "The options write the numbers that are not finite, NaN and the infinities, as texts."

        if options.IgnoreReadOnlyProperties then
            reasons.Add "The options ignore read-only properties."

        if options.IgnoreReadOnlyFields then
            reasons.Add "The options ignore read-only fields."

        reasons.ToImmutable ()

    /// <summary>
    /// The profile of <paramref name="options"/>, or the reasons why the graph model cannot work with them.
    /// </summary>
    let ofOptions (options : JsonSerializerOptions) : Result<SerializerProfile, ImmutableArray<string>> =
        let refusals = refusalsOf options

        if not refusals.IsEmpty then
            Error refusals
        else
            // A copy, so that the options of the caller stay as they are: a contract can be asked only from options that
            // have a resolver and that are not changed any more. The resolver is the one of the caller, because a resolver
            // cannot be copied: the model asks the resolver that the serializer asks.
            let contract =
                match options.TypeInfoResolver with
                | null -> JsonSerializerOptions (options, TypeInfoResolver = DefaultJsonTypeInfoResolver ())
                | _ -> JsonSerializerOptions options

            contract.MakeReadOnly ()

            Ok { Contract = contract; Converter = (fsharpConvertersOf options).[0] }

    /// The contract of a type; none for a type that the serializer refuses.
    let typeInfoOf (profile : SerializerProfile) (valueType : Type) : JsonTypeInfo voption =
        try
            ValueSome (profile.Contract.GetTypeInfo valueType)
        with
        | :? NotSupportedException
        | :? InvalidOperationException
        | :? ArgumentException
        // FSharp.SystemTextJson makes its converters by reflection, so what one of them refuses a type with comes
        // wrapped in this one.
        | :? TargetInvocationException -> ValueNone

    /// <summary>
    /// How the serializer writes a value of <paramref name="valueType"/>. The contract tells it for the types that
    /// System.Text.Json writes itself, and the class of the converter tells it for the types of FSharp.SystemTextJson,
    /// whose contracts are empty.
    /// </summary>
    let writtenAs (profile : SerializerProfile) (valueType : Type) : WrittenAs =
        match typeInfoOf profile valueType with
        | ValueNone -> WrittenAs.Unknown
        | ValueSome info ->
            match info.Kind with
            // The contract of an enumerable always names the type of an item. A collection type with derived types is
            // written as an object with the items under $values, and the encoder of the values refuses that object.
            | JsonTypeInfoKind.Enumerable -> WrittenAs.Array (nonNull info.ElementType)
            | JsonTypeInfoKind.Dictionary -> WrittenAs.Object "an object with a property for each key"
            | JsonTypeInfoKind.Object -> WrittenAs.Object "an object with a property for each of its properties"
            | _ ->
                let converter = info.Converter.GetType ()

                if
                    isMadeOf typedefof<JsonListConverter<_>> converter
                    || isMadeOf typedefof<JsonSetConverter<_>> converter
                then
                    WrittenAs.Array (converter.GetGenericArguments ()).[0]
                elif isMadeOf typedefof<JsonMapObjectConverter<_, _>> converter then
                    WrittenAs.Object "an object with a property for each key"
                elif isMadeOf typedefof<JsonMapArrayOfPairsConverter<_, _>> converter then
                    WrittenAs.Object "an array of pairs of a key and a value"
                elif isMadeOf typedefof<JsonRecordConverter<_>> converter then
                    WrittenAs.Object "an object with a property for each field"
                else
                    WrittenAs.Value

    /// <summary>
    /// Whether FSharp.SystemTextJson writes <paramref name="valueType"/> with options of the type itself: in the
    /// overrides of its options, or in an attribute that the options allow. The graph model reads only the options that
    /// are common to all types.
    /// </summary>
    let private hasOptionsOfItsOwn (common : JsonFSharpOptions) (valueType : Type) : bool =
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
            && valueType.IsDefined (typeof<IJsonFSharpConverterAttribute>, false))

    /// <summary>
    /// Who writes the fields of <paramref name="recordType"/>. A converter that tells its names writes the fields under
    /// these names. Of the converters of FSharp.SystemTextJson, only the record converter that the converter of the
    /// gate makes writes the fields under the names of <see cref="M:FSharp.Gremlin.SerializerProfileModule.memberName(FSharp.Gremlin.SerializerProfile,System.Reflection.PropertyInfo)"/>:
    /// a resolver or another converter of the application can write them in another way.
    /// </summary>
    let recordWriter (profile : SerializerProfile) (recordType : Type) : RecordWriter =
        match typeInfoOf profile recordType with
        | ValueNone -> RecordWriter.Other
        | ValueSome info ->
            // A type test from a class to an interface that the class does not implement needs an object
            match box info.Converter with
            // The converter of the contract writes the record, so the names that it tells are the names of the document
            | :? IRecordFieldNames as described -> RecordWriter.Described described.FieldNames
            | _ ->
                // The serializer takes the first converter of the options that can convert the type, and the converter
                // of the gate makes a JsonRecordConverter for each record. A converter before it can be a
                // JsonRecordConverter that an application made with other options, which writes other names, so the
                // class of the converter in the contract is not enough.
                let chosen =
                    profile.Contract.Converters
                    |> Seq.tryFind _.CanConvert(recordType)

                match chosen with
                | ValueSome converter when Object.ReferenceEquals (converter, profile.Converter) ->
                    match info.OriginatingResolver with
                    | :? DefaultJsonTypeInfoResolver when hasOptionsOfItsOwn profile.Converter.Options recordType ->
                        RecordWriter.OwnOptions
                    | :? DefaultJsonTypeInfoResolver -> RecordWriter.Library
                    | _ -> RecordWriter.Other
                | _ -> RecordWriter.Other

    /// <summary>
    /// The name that FSharp.SystemTextJson writes a field of a record under: the first name of its
    /// <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/>, then the name of its
    /// <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>, then its name as the naming policy of the
    /// options spells it, then its name.
    /// </summary>
    let memberName (profile : SerializerProfile) (field : PropertyInfo) : string =
        let declared =
            field.GetCustomAttributes<JsonNameAttribute> false
            // A name with a field is the name of a field of a union case, not of this member. The library declares the
            // field as a string that is never null and leaves it null when none is given.
            |> Seq.tryFind (fun attribute ->
                match withNull attribute.Field with
                | null -> true
                | _ -> false
            )
            |> ValueOption.map _.Name.AsString()
            |> ValueOption.orElseWith (fun () ->
                match field.GetCustomAttribute<JsonPropertyNameAttribute> false with
                | null -> ValueNone
                | attribute -> ValueSome attribute.Name
            )

        match declared with
        | ValueSome name -> name
        | ValueNone ->
            match profile.Contract.PropertyNamingPolicy with
            | null -> field.Name
            | policy -> policy.ConvertName field.Name
