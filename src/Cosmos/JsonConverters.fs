namespace FSharp.Azure.Cosmos

open System
open System.Globalization
open System.Text.Json
open System.Text.Json.Serialization

/// <summary>
/// Writes a <see cref="TimeSpan"/> as a JSON number, its number of ticks, and reads the same number.
/// </summary>
/// <remarks>
/// <para>
/// The converter of System.Text.Json writes a <see cref="TimeSpan"/> as text, for example <c>"2.00:00:00"</c>.
/// Cosmos DB compares text by character code. Thus <c>"2.00:00:00"</c> sorts after <c>"10.00:00:00"</c>.
/// A number sorts and compares in time order. Use this converter for each <see cref="TimeSpan"/> that a query
/// compares or sorts.
/// </para>
/// <para>
/// One tick is 100 nanoseconds. Cosmos DB defines a JSON number as a 64-bit floating-point number. Such a number holds
/// each whole number up to 2^53 exactly, which is about 28 years of ticks. A longer <see cref="TimeSpan"/> can lose
/// digits in a client that reads the number as a floating-point number.
/// </para>
/// <para>
/// The converter also reads the text that System.Text.Json writes, so a container that holds old documents stays
/// readable. An old document keeps its text, and the text does not sort in time order, until you write the document
/// again.
/// </para>
/// <para>
/// To use the converter, add it to <see cref="JsonSerializerOptions.Converters"/>. Then set the options in
/// <see cref="Microsoft.Azure.Cosmos.CosmosClientOptions.UseSystemTextJsonSerializerWithOptions"/>. The converter
/// also handles <see cref="Nullable{T}"/> values and the keys of a dictionary.
/// </para>
/// </remarks>
[<Sealed>]
type TimeSpanTicksConverter () =
    inherit JsonConverter<TimeSpan> ()

    // A text that holds a whole number is a number of ticks. This is the form of a dictionary key.
    // Any other text must have the form of System.Text.Json: [-][d.]hh:mm:ss[.fffffff]. It always has a colon.
    // The parser of the "c" format reads a text without a colon as a number of days, so the check keeps such a text out.
    static let tryParseText (text : string) : TimeSpan voption =
        match Int64.TryParse (text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
        | true, ticks -> ValueSome (TimeSpan.FromTicks ticks)
        | false, _ when text.Contains (':', StringComparison.Ordinal) ->
            match TimeSpan.TryParseExact (text, "c", CultureInfo.InvariantCulture) with
            | true, value -> ValueSome value
            | false, _ -> ValueNone
        | false, _ -> ValueNone

    static let parseText (text : string) : TimeSpan =
        match tryParseText text with
        | ValueSome value -> value
        | ValueNone ->
            raise (
                JsonException
                    $"The text '{text}' is not a number of ticks and not a TimeSpan in the form [-][d.]hh:mm:ss[.fffffff]."
            )

    /// <summary>
    /// Reads a <see cref="TimeSpan"/> from a whole number of ticks. Reads the text form of System.Text.Json as well.
    /// </summary>
    /// <exception cref="JsonException">
    /// The token is not a number or a text, the number is not a whole number that fits in 64 bits, or the text has an
    /// unknown form.
    /// </exception>
    override _.Read (reader : byref<Utf8JsonReader>, typeToConvert : Type, options : JsonSerializerOptions) : TimeSpan =
        match reader.TokenType with
        | JsonTokenType.Number ->
            match reader.TryGetInt64 () with
            | true, ticks -> TimeSpan.FromTicks ticks
            | false, _ -> raise (JsonException "A TimeSpan must be a whole number of ticks that fits in 64 bits.")
        | JsonTokenType.String ->
            match reader.GetString () with
            | null -> raise (JsonException "A TimeSpan must not be null.")
            | text -> parseText text
        | token -> raise (JsonException $"A TimeSpan must be a number or a text, but the JSON token is {token}.")

    /// <summary>
    /// Writes the number of ticks of <paramref name="value"/> as a JSON number.
    /// </summary>
    override _.Write (writer : Utf8JsonWriter, value : TimeSpan, options : JsonSerializerOptions) : unit =
        writer.WriteNumberValue value.Ticks

    /// <summary>
    /// Reads the key of a dictionary: the number of ticks as text. Reads the text form of System.Text.Json as well.
    /// </summary>
    /// <exception cref="JsonException">The key has an unknown form.</exception>
    override _.ReadAsPropertyName
        (reader : byref<Utf8JsonReader>, typeToConvert : Type, options : JsonSerializerOptions)
        : TimeSpan
        =
        match reader.GetString () with
        | null -> raise (JsonException "A TimeSpan key must not be null.")
        | text -> parseText text

    /// <summary>
    /// Writes the key of a dictionary: the number of ticks of <paramref name="value"/> as text.
    /// </summary>
    override _.WriteAsPropertyName (writer : Utf8JsonWriter, value : TimeSpan, options : JsonSerializerOptions) : unit =
        writer.WritePropertyName (value.Ticks.ToString CultureInfo.InvariantCulture)
