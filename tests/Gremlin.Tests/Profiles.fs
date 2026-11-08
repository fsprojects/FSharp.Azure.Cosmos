namespace FSharp.Gremlin.Tests

open System.Globalization
open System.Text.Json
open System.Text.Json.Serialization

/// <summary>
/// Writes a <see cref="T:System.Globalization.CultureInfo"/> as its name, as the converter of SkillGro does.
/// </summary>
type CultureInfoConverter () =
    inherit JsonConverter<CultureInfo> ()

    /// <inheritdoc />
    override _.Read (reader, _, _) = CultureInfo.GetCultureInfo (nonNull (reader.GetString ()))

    /// <inheritdoc />
    override _.Write (writer, value, _) = writer.WriteStringValue value.Name

/// <summary>
/// Gives the fields <see cref="P:FSharp.Gremlin.VertexProperty`2.Value"/> and
/// <see cref="P:FSharp.Gremlin.VertexProperty`2.Meta"/> one name, which the graph model refuses.
/// </summary>
type OneNameForValueAndMeta () =
    inherit JsonNamingPolicy ()

    /// <inheritdoc />
    override _.ConvertName name =
        match name with
        | "Value"
        | "Meta" -> "x"
        | name -> name

/// The set-ups of the serializer that the tests make graph models with.
[<RequireQualifiedAccess>]
module Profiles =

    /// FSharp.SystemTextJson as it is made.
    let fsharpDefaults () : JsonSerializerOptions = JsonFSharpOptions.Default().ToJsonSerializerOptions()

    /// FSharp.SystemTextJson with camel case names.
    let camelCase () : JsonSerializerOptions =
        JsonFSharpOptions.Default().ToJsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// FSharp.SystemTextJson with snake case names.
    let snakeCase () : JsonSerializerOptions =
        JsonFSharpOptions.Default().ToJsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)

    /// <summary>
    /// The options of SkillGro: camel case names, unions with an internal tag named <c>kind</c>, enumerations by name,
    /// and cultures by name.
    /// </summary>
    let skillGro () : JsonSerializerOptions =
        let options =
            JsonFSharpOptions(
                JsonUnionEncoding.InternalTag
                ||| JsonUnionEncoding.NamedFields
                ||| JsonUnionEncoding.UnwrapFieldlessTags
                ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                ||| JsonUnionEncoding.UnwrapRecordCases
                ||| JsonUnionEncoding.UnwrapOption,
                unionTagName = "kind"
            )
                .ToJsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

        options.Converters.Add (JsonStringEnumConverter ())
        options.Converters.Add (CultureInfoConverter ())
        options

    /// <summary>
    /// The options of the Ecierge application, as the tests of the narrow layout of the Quotations library use them:
    /// an internal tag named <c>$type</c>, named fields, options that are always skipped, maps as objects, camel case
    /// names and enumerations by name.
    /// </summary>
    let ecierge () : JsonSerializerOptions =
        let options =
            JsonFSharpOptions(
                JsonUnionEncoding.InternalTag
                ||| JsonUnionEncoding.AllowUnorderedTag
                ||| JsonUnionEncoding.NamedFields
                ||| JsonUnionEncoding.UnwrapSingleCaseUnions
                ||| JsonUnionEncoding.UnwrapRecordCases
                ||| JsonUnionEncoding.UnwrapOption
                ||| JsonUnionEncoding.UnwrapFieldlessTags,
                unionTagName = "$type"
            )
                .WithSkippableOptionFields(SkippableOptionFields.Always, true)
                .WithMapFormat(MapFormat.Object)
                .ToJsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

        options.Converters.Add (JsonStringEnumConverter ())
        options

    /// <summary>
    /// The set-up named <paramref name="name"/>, for the rows of a test.
    /// </summary>
    let named (name : string) : JsonSerializerOptions =
        match name with
        | "defaults" -> fsharpDefaults ()
        | "camel case" -> camelCase ()
        | "snake case" -> snakeCase ()
        | "SkillGro" -> skillGro ()
        | "Ecierge" -> ecierge ()
        | _ -> invalidArg (nameof name) $"No set-up of the serializer has the name '{name}'."
