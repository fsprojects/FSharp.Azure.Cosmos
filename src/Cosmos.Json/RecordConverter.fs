namespace FSharp.Azure.Cosmos

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Reflection
open System.Runtime.CompilerServices
open System.Runtime.ExceptionServices
open System.Text.Json
open System.Text.Json.Serialization

open FSharp.Reflection

/// <summary>
/// How <see cref="T:FSharp.Azure.Cosmos.AllowNullFSharpRecordConverter`1"/> reads and writes one field of a record.
/// </summary>
[<Sealed>]
type internal RecordField (recordType : Type, field : PropertyInfo, options : JsonSerializerOptions) =

    static let isMadeOf (definition : Type) (valueType : Type) =
        valueType.IsGenericType
        && Type.(=) (valueType.GetGenericTypeDefinition (), definition)

    // The value of a call to the serializer, as a field value of a record: null stays null
    static let asFieldValue (value : objnull) : obj =
        match value with
        | null -> Unchecked.defaultof<obj>
        | value -> value

    let fieldType = field.PropertyType
    let isOption = isMadeOf typedefof<option<_>> fieldType
    let isValueOption = isMadeOf typedefof<voption<_>> fieldType
    let isSkippable = isMadeOf typedefof<Skippable<_>> fieldType
    let isNullable = isMadeOf typedefof<Nullable<_>> fieldType

    // A field of these types can be absent: None, ValueNone, Skip and null stand for no value
    let isOptional = isOption || isValueOption || isSkippable || isNullable

    // The value of the field when a document does not have it: null for a reference type, None, ValueNone, Skip, the
    // null of Nullable, and the default value of any other value type. GetUninitializedObject and not
    // Activator.CreateInstance: a struct of C# can have a constructor without parameters that sets other values, but
    // the default value has all bits zero. A Nullable without a value is null as an object, and GetUninitializedObject
    // gives a zero of the type in the Nullable.
    let absentValue : obj =
        if fieldType.IsValueType && not isNullable then
            RuntimeHelpers.GetUninitializedObject fieldType
        else
            Unchecked.defaultof<obj>

    // ValueNone and Skip are the first case of their union
    let readTag =
        if isValueOption || isSkippable then
            ValueSome (FSharpValue.PreComputeUnionTagReader (fieldType, true))
        else
            ValueNone

    // Whether a value of the field stands for no value: null, None, ValueNone or Skip
    let isNothing (value : objnull) =
        match value with
        | null -> true
        | value ->
            match readTag with
            | ValueSome readTag -> readTag value = 0
            | ValueNone -> false

    let isDefault (value : objnull) =
        match value with
        | null -> true
        | value ->
            isNothing value
            || (fieldType.IsValueType && value.Equals absentValue)

    let ignoreCondition =
        match field.GetCustomAttribute<JsonIgnoreAttribute> false with
        | null -> ValueNone
        | attribute -> ValueSome attribute.Condition

    let isIgnoredOnRead =
        match ignoreCondition with
        | ValueSome JsonIgnoreCondition.Always
        | ValueSome JsonIgnoreCondition.WhenReading -> true
        | _ -> false

    let isIgnoredOnWrite =
        match ignoreCondition with
        | ValueSome JsonIgnoreCondition.Always
        | ValueSome JsonIgnoreCondition.WhenWriting -> true
        | _ -> false

    // The names of JsonName of FSharp.SystemTextJson, the first one first. A JsonName with a field is the name of a field
    // of a union case, not of this field.
    let jsonNames =
        field.GetCustomAttributes<JsonNameAttribute> false
        |> Seq.filter (fun attribute ->
            match withNull attribute.Field with
            | null -> true
            | _ -> false
        )
        |> Seq.collect (fun attribute -> seq {
            yield attribute.Name
            yield! attribute.OtherNames
        })
        |> Seq.map _.AsString()
        |> Seq.toArray

    let propertyName =
        match field.GetCustomAttribute<JsonPropertyNameAttribute> false with
        | null -> ValueNone
        | attribute -> ValueSome attribute.Name

    let policyName =
        match options.PropertyNamingPolicy with
        | null -> field.Name
        | policy -> policy.ConvertName field.Name

    // The rule of FSharp.SystemTextJson: JsonName first, then JsonPropertyName, then the naming policy
    let name =
        match jsonNames with
        | [||] -> propertyName |> ValueOption.defaultValue policyName
        | names -> names.[0]

    let deserialize (reader : byref<Utf8JsonReader>) (options : JsonSerializerOptions) : obj =
        asFieldValue (JsonSerializer.Deserialize (&reader, fieldType, options))

    // Whether the serializer reads null as a value of the field type. A converter can give a value for null, for example
    // an empty list, or refuse null, as FSharp.SystemTextJson does for an F# list. The serializer is asked one time, at
    // the first null of the field: then all its converters exist.
    let readsNull =
        lazy
            try
                JsonSerializer.Deserialize ("null", fieldType, options)
                |> ignore
                true
            with :? JsonException ->
                false

    /// The field.
    member _.Member = field

    /// The type of the field.
    member _.Type = fieldType

    /// The name that the converter writes the field under.
    member _.Name = name

    /// <summary>
    /// The names that the converter reads the field from: <see cref="P:FSharp.Azure.Cosmos.RecordField.Name"/> and the
    /// other names of the attributes.
    /// </summary>
    member _.DeclaredNames : string array =
        seq {
            yield name
            yield! jsonNames
            yield! ValueOption.toArray propertyName
        }
        |> Seq.distinct
        |> Seq.toArray

    /// The name that the naming policy gives the field.
    member _.PolicyName = policyName

    /// The value of the field when a document does not have it.
    member _.AbsentValue = absentValue

    /// Whether a document can leave the field out when the converter does not allow null fields.
    member _.IsOptional = isOptional

    /// Whether the converter does not read the field from a document.
    member _.IsIgnoredOnRead = isIgnoredOnRead

    /// Whether the converter does not write the field.
    member _.IsIgnoredOnWrite = isIgnoredOnWrite

    /// <summary>
    /// Whether the converter writes <paramref name="value"/> of the field. Without an attribute, it writes every value
    /// except null, <see cref="P:Microsoft.FSharp.Core.FSharpOption`1.None"/>,
    /// <see cref="P:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> and
    /// <see cref="P:System.Text.Json.Serialization.Skippable`1.Skip"/>.
    /// </summary>
    member _.ShouldWrite (value : objnull) : bool =
        match ignoreCondition with
        | _ when isIgnoredOnWrite -> false
        | ValueSome JsonIgnoreCondition.Never -> true
        | ValueSome JsonIgnoreCondition.WhenWritingDefault -> not (isDefault value)
        | _ -> not (isNothing value)

    /// <summary>
    /// Reads the value of the field from <paramref name="reader"/>, which is on the value.
    /// </summary>
    member _.Read (reader : byref<Utf8JsonReader>, options : JsonSerializerOptions, allowNullFields : bool) : obj =
        if reader.TokenType <> JsonTokenType.Null then
            deserialize &reader options
        // FSharp.SystemTextJson cannot read null as a Skippable of a value type
        elif isSkippable then
            absentValue
        elif not (isOptional || allowNullFields) then
            raise (
                JsonException
                    $"The field {field.Name} of the record {recordType.Name} has the type {fieldType.Name}, but the document has null for it."
            )
        // The value that the serializer reads from null, for example None or an empty list, else the value without a
        // value. Also an option can refuse null: FSharp.SystemTextJson without UnwrapOption refuses null for a voption.
        elif readsNull.Value then
            deserialize &reader options
        else
            absentValue

/// <summary>
/// Reads and writes an F# record as a JSON object, and lets a document leave fields out or have null for them.
/// </summary>
/// <remarks>
/// <para>
/// The converter writes a field under the first name of its <see cref="T:System.Text.Json.Serialization.JsonNameAttribute"/>,
/// else under the name of its <see cref="T:System.Text.Json.Serialization.JsonPropertyNameAttribute"/>, else under its
/// name as <see cref="P:System.Text.Json.JsonSerializerOptions.PropertyNamingPolicy"/> spells it. These are the rules of
/// FSharp.SystemTextJson. It reads a field under each of these names and under the name of the naming policy, so a
/// document that was written before the field had an attribute stays readable. Two fields that the converter writes
/// cannot have one name, and two fields that it reads cannot have one declared name. A field that the converter does
/// not write or does not read does not keep its name for that operation, so a new field can replace an old field with
/// <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/> under the same name.
/// </para>
/// <para>
/// The converter does not write a field that has no value: null,
/// <see cref="P:Microsoft.FSharp.Core.FSharpOption`1.None"/>,
/// <see cref="P:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/> or
/// <see cref="P:System.Text.Json.Serialization.Skippable`1.Skip"/>. The condition of
/// <see cref="T:System.Text.Json.Serialization.JsonIgnoreAttribute"/> changes this:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.Always"/> ignores the field;
/// </description></item>
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.Never"/> always writes the field, also without a
/// value;
/// </description></item>
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/> leaves out a field without a
/// value;
/// </description></item>
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault"/> also leaves out the default
/// value of a value type;
/// </description></item>
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.WhenWriting"/> reads the field and does not write
/// it;
/// </description></item>
/// <item><description>
/// <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.WhenReading"/> writes the field and does not read
/// it.
/// </description></item>
/// </list>
/// <para>
/// A field that a document leaves out, or that the converter does not read, gets the value without a value: null,
/// <see cref="P:Microsoft.FSharp.Core.FSharpOption`1.None"/>,
/// <see cref="P:Microsoft.FSharp.Core.FSharpValueOption`1.ValueNone"/>,
/// <see cref="P:System.Text.Json.Serialization.Skippable`1.Skip"/>, or the default value of a value type. Thus a field
/// of a value type with <see cref="F:System.Text.Json.Serialization.JsonIgnoreCondition.Always"/> gets its default
/// value. FSharp.SystemTextJson 1.4.36 throws <see cref="T:System.NullReferenceException"/> for such a field
/// (<see href="https://github.com/Tarmil/FSharp.SystemTextJson/issues/217">FSharp.SystemTextJson#217</see>).
/// </para>
/// <para>
/// A document can always leave out an option or have null for it. When null fields are allowed, a document can also
/// leave out any other field or have null for it. When null fields are not allowed, a document must have each field
/// that is not an option, and it can have null only for an option. A field that the document leaves out gets the value
/// without a value.
/// </para>
/// <para>
/// For a field that is null, the serializer decides when it can read null as a value of the type, for example with a
/// converter that gives an empty list. When the serializer refuses null for the type, the field gets the value without
/// a value. FSharp.SystemTextJson refuses null for an F# list, and for a
/// <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/> when its options do not unwrap options.
/// </para>
/// <para>
/// The converter skips the properties of a document that are no field of the record, for example the system
/// properties of Azure Cosmos DB, unless <see cref="P:System.Text.Json.JsonSerializerOptions.UnmappedMemberHandling"/>
/// disallows them. It also skips them when the serializer reads a stream in parts.
/// </para>
/// </remarks>
/// <param name="options">The options that the serializer writes and reads the values of the fields with.</param>
/// <param name="allowNullFields">Whether a document can leave out a field or have null for it.</param>
/// <exception cref="T:System.InvalidOperationException">
/// Two fields that the converter writes have one name, or two fields that it reads have one declared name.
/// </exception>
[<Sealed>]
type AllowNullFSharpRecordConverter<'Record> (options : JsonSerializerOptions, allowNullFields : bool) =
    inherit JsonConverter<'Record> ()

    let recordType = typeof<'Record>

    let fields =
        FSharpType.GetRecordFields (recordType, true)
        |> Array.map (fun field -> RecordField (recordType, field, options))

    // true: also a record whose representation is private
    let construct = FSharpValue.PreComputeRecordConstructor (recordType, true)
    let deconstruct = FSharpValue.PreComputeRecordReader (recordType, true)

    // The index of the field for each name that a document can have. Two fields that the converter writes cannot have
    // one name, because the document would have the name two times. Two fields that the converter reads cannot have one
    // declared name, because a document cannot tell them apart. A field that the converter does not write or does not
    // read, for example an old field with JsonIgnore, does not take part in the check of that direction, so a new field
    // can take its name. The names of the naming policy come after the declared names and never replace one, because
    // they are only there to read old documents. The names of the fields that the converter does not read come last, so
    // the converter skips the value of such a name only when no field that it reads has the name.
    let indexByName : FrozenDictionary<string, int> =
        let comparer =
            if options.PropertyNameCaseInsensitive then
                StringComparer.OrdinalIgnoreCase
            else
                StringComparer.Ordinal

        let written = Dictionary<string, int> comparer

        for index in 0 .. fields.Length - 1 do
            let field = fields.[index]

            if not field.IsIgnoredOnWrite then
                match written.TryGetValue field.Name with
                | true, other ->
                    invalidOp
                        $"The fields {fields.[other].Member.Name} and {field.Member.Name} of the record {recordType.Name} have the name '{field.Name}', so the converter cannot write both."
                | false, _ -> written.[field.Name] <- index

        let indices = Dictionary<string, int> comparer

        for index in 0 .. fields.Length - 1 do
            let field = fields.[index]

            if not field.IsIgnoredOnRead then
                for name in field.DeclaredNames do
                    match indices.TryGetValue name with
                    | true, other when other <> index ->
                        invalidOp
                            $"The fields {fields.[other].Member.Name} and {field.Member.Name} of the record {recordType.Name} have the name '{name}', so a document cannot tell them apart."
                    | _ -> indices.[name] <- index

        for index in 0 .. fields.Length - 1 do
            if not fields.[index].IsIgnoredOnRead then
                indices.TryAdd (fields.[index].PolicyName, index) |> ignore

        for index in 0 .. fields.Length - 1 do
            let field = fields.[index]

            if field.IsIgnoredOnRead then
                for name in field.DeclaredNames do
                    indices.TryAdd (name, index) |> ignore

                indices.TryAdd (field.PolicyName, index) |> ignore

        indices.ToFrozenDictionary comparer

    // The fields that a document must have when null fields are not allowed
    let requiredFields = [|
        for index in 0 .. fields.Length - 1 do
            if
                not fields.[index].IsOptional
                && not fields.[index].IsIgnoredOnRead
            then
                index
    |]

    // TrySkip and not Skip: when the serializer reads a stream in parts, the converter gets the whole record, but the
    // reader is not on its final block, and Skip throws then
    let skipValue (reader : byref<Utf8JsonReader>) =
        if not (reader.TrySkip ()) then
            raise (JsonException $"The document of the record {recordType.Name} ends in the middle of a value.")

    /// Whether a document can leave out a field or have null for it.
    member _.AllowNullFields = allowNullFields

    /// <inheritdoc />
    override _.Read (reader, _typeToConvert, options) =
        if reader.TokenType <> JsonTokenType.StartObject then
            raise (
                JsonException $"A JSON object was expected for the record {recordType.Name}, but the token is {reader.TokenType}."
            )

        let values = fields |> Array.map _.AbsentValue
        let found = Array.zeroCreate<bool> fields.Length
        let mutable reading = true

        while reading && reader.Read () do
            match reader.TokenType with
            | JsonTokenType.EndObject -> reading <- false
            | JsonTokenType.PropertyName ->
                let name = nonNull (reader.GetString ())

                match indexByName.TryGetValue name with
                | true, index when not fields.[index].IsIgnoredOnRead ->
                    reader.Read () |> ignore
                    values.[index] <- fields.[index].Read(&reader, options, allowNullFields)
                    found.[index] <- true
                | true, _ -> skipValue &reader
                | false, _ when options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow ->
                    raise (JsonException $"The record {recordType.Name} has no field for the property '{name}'.")
                | false, _ -> skipValue &reader
            | _ -> ()

        if not allowNullFields then
            for index in requiredFields do
                if not found.[index] then
                    raise (JsonException $"The document of the record {recordType.Name} has no field {fields.[index].Name}.")

        unbox<'Record>(construct values)

    /// <inheritdoc />
    override _.Write (writer, value, options) =
        writer.WriteStartObject ()
        // The serializer writes null itself and calls the converter only for a value
        let values = deconstruct (Unchecked.nonNull (box value))

        for index in 0 .. fields.Length - 1 do
            let field = fields.[index]
            let fieldValue = values.[index]

            if field.ShouldWrite fieldValue then
                writer.WritePropertyName field.Name
                JsonSerializer.Serialize (writer, fieldValue, field.Type, options)

        writer.WriteEndObject ()

/// <summary>
/// Makes an <see cref="T:FSharp.Azure.Cosmos.AllowNullFSharpRecordConverter`1"/> for each F# record type that
/// <paramref name="isConverted"/> accepts.
/// </summary>
/// <remarks>
/// Put the factory before the converter of FSharp.SystemTextJson in
/// <see cref="P:System.Text.Json.JsonSerializerOptions.Converters"/>: the serializer asks the converters in sequence, and
/// FSharp.SystemTextJson also writes records.
/// </remarks>
/// <param name="allowNullFields">Whether a document can leave out a field or have null for it.</param>
/// <param name="isConverted">Whether the factory makes the converter of a record type.</param>
[<Sealed>]
type AllowNullFSharpRecordConverterFactory (allowNullFields : bool, isConverted : Func<Type, bool>) =
    inherit JsonConverterFactory ()

    /// <summary>
    /// Makes the factory for every F# record type.
    /// </summary>
    /// <param name="allowNullFields">Whether a document can leave out a field or have null for it.</param>
    new (allowNullFields : bool) = AllowNullFSharpRecordConverterFactory (allowNullFields, Func<Type, bool>(fun _ -> true))

    /// Whether a document can leave out a field or have null for it.
    member _.AllowNullFields = allowNullFields

    /// <inheritdoc />
    override _.CanConvert (typeToConvert) =
        FSharpType.IsRecord (typeToConvert, true)
        && isConverted.Invoke typeToConvert

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert, options) =
        let converterType = typedefof<AllowNullFSharpRecordConverter<_>>.MakeGenericType typeToConvert

        try
            Activator.CreateInstance (converterType, [| box options; box allowNullFields |])
            |> nonNull
            :?> JsonConverter
        // The constructor refuses a record with two fields of one name. Give that exception, not the one of reflection.
        with :? TargetInvocationException as invocation ->
            match invocation.InnerException with
            | null -> reraise ()
            | inner ->
                ExceptionDispatchInfo.Throw inner
                Unchecked.defaultof<JsonConverter>
