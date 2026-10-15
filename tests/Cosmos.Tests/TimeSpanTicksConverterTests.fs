namespace FSharp.Azure.Cosmos.Tests

open System
open System.Collections.Generic
open System.Text.Json
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

// The record is in a module of its own. Thus, the record type inference of the other test files does not see its
// labels. The module is public because System.Text.Json does not write the properties of an internal record.
module TimeSpanTicksConverterTestData =

    [<CLIMutable>]
    type DurationDocument = { Id : string; Duration : TimeSpan }

[<TestClass; UnitTestCategory; JsonConverterTestCategory; TimeSpanTestCategory>]
type TimeSpanTicksConverterTests () =

    // A Cosmos DB document has camel case names, and Cosmos DB requires the name id
    static let options =
        let options = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        options.Converters.Add (TimeSpanTicksConverter ())
        options

    static let defaultText (value : TimeSpan) = JsonSerializer.Serialize (value, JsonSerializerOptions ())

    // The generic overload of Deserialize returns 'T | null. For a value type, the compiler cannot express that and
    // warns (FS3265). Thus, the two helpers use the overload with a Type, which returns obj | null.
    static let readTimeSpan (json : string) : TimeSpan =
        JsonSerializer.Deserialize (json, typeof<TimeSpan>, options)
        |> nonNull
        |> unbox<TimeSpan>

    static let readNullableTimeSpan (json : string) : Nullable<TimeSpan> =
        match JsonSerializer.Deserialize (json, typeof<Nullable<TimeSpan>>, options) with
        | null -> Nullable ()
        | value -> Nullable (unbox<TimeSpan> value)

    [<TestMethod>]
    [<DataRow(0L, "0", DisplayName = "zero")>]
    [<DataRow(1L, "1", DisplayName = "one tick")>]
    [<DataRow(-1L, "-1", DisplayName = "minus one tick")>]
    [<DataRow(36000000000L, "36000000000", DisplayName = "one hour")>]
    [<DataRow(-864000000000L, "-864000000000", DisplayName = "minus one day")>]
    [<DataRow(9223372036854775807L, "9223372036854775807", DisplayName = "the largest TimeSpan")>]
    [<DataRow(-9223372036854775808L, "-9223372036854775808", DisplayName = "the smallest TimeSpan")>]
    member _.``Write stores the ticks as a number and Read returns the same TimeSpan`` (ticks : int64, expectedJson : string) =
        let value = TimeSpan.FromTicks ticks

        let json = JsonSerializer.Serialize (value, options)
        Assert.AreEqual (expectedJson, json, "Write should store the ticks as a JSON number.")

        Assert.AreEqual (value, readTimeSpan json, "Read should return the TimeSpan that Write stored.")

    [<TestMethod>]
    [<DataRow("\"1.02:00:00\"", 936000000000L, DisplayName = "a day and two hours")>]
    [<DataRow("\"-00:00:01\"", -10000000L, DisplayName = "minus one second")>]
    [<DataRow("\"00:00:00.5000000\"", 5000000L, DisplayName = "half a second")>]
    [<DataRow("\"36000000000\"", 36000000000L, DisplayName = "the ticks as text")>]
    member _.``Read accepts the text form of System.Text.Json and the ticks as text`` (json : string, expectedTicks : int64) =
        Assert.AreEqual (TimeSpan.FromTicks expectedTicks, readTimeSpan json, "Read should return the TimeSpan of the text.")

    [<TestMethod>]
    member _.``Read accepts what the default converter writes`` () =
        let values = [|
            TimeSpan.Zero
            TimeSpan.FromHours 26.0
            TimeSpan.FromDays (-10.0)
            TimeSpan.FromTicks 1L
            TimeSpan.FromMilliseconds 1500.0
            TimeSpan.MinValue
            TimeSpan.MaxValue
        |]

        for value in values do
            let text = defaultText value
            Assert.AreEqual (value, readTimeSpan text, $"Read should return {text} as {value}.")

    [<TestMethod>]
    [<DataRow("true", DisplayName = "a boolean")>]
    [<DataRow("null", DisplayName = "null")>]
    [<DataRow("1.5", DisplayName = "a fraction")>]
    [<DataRow("1e3", DisplayName = "an exponent")>]
    [<DataRow("9223372036854775808", DisplayName = "one tick above the largest TimeSpan")>]
    [<DataRow("-9223372036854775809", DisplayName = "one tick below the smallest TimeSpan")>]
    [<DataRow("\"abc\"", DisplayName = "text that is not a TimeSpan")>]
    [<DataRow("\"\"", DisplayName = "empty text")>]
    [<DataRow("\"26:00:00\"", DisplayName = "hours above 23 in the text form")>]
    [<DataRow("[]", DisplayName = "an array")>]
    [<DataRow("{}", DisplayName = "an object")>]
    member _.``Read throws JsonException for a JSON value that is not a TimeSpan`` (json : string) =
        Assert.ThrowsExactly<JsonException>(
            (fun () -> readTimeSpan json |> ignore),
            $"Read should throw JsonException for {json}."
        )
        |> ignore

    [<TestMethod>]
    member _.``Nullable TimeSpan stores null and the ticks`` () =
        Assert.AreEqual (
            "null",
            JsonSerializer.Serialize (Nullable<TimeSpan>(), options),
            "An empty Nullable should be stored as null."
        )

        Assert.AreEqual (
            "36000000000",
            JsonSerializer.Serialize (Nullable (TimeSpan.FromHours 1.0), options),
            "A Nullable that has a value should be stored as the ticks."
        )

        Assert.IsFalse ((readNullableTimeSpan "null").HasValue, "null should be read as an empty Nullable.")

        Assert.AreEqual (
            Nullable (TimeSpan.FromHours 1.0),
            readNullableTimeSpan "36000000000",
            "A number should be read as a Nullable that has a value."
        )

    [<TestMethod>]
    member _.``A TimeSpan is stored as the key of a dictionary and read back`` () =
        let key = TimeSpan.FromHours 1.0
        let dictionary = Dictionary<TimeSpan, string>()
        dictionary[key] <- "one hour"

        let json = JsonSerializer.Serialize (dictionary, options)
        Assert.AreEqual ("""{"36000000000":"one hour"}""", json, "The key should be the ticks as text.")

        let readDictionary =
            JsonSerializer.Deserialize<Dictionary<TimeSpan, string>>(json, options)
            |> nonNull

        Assert.AreEqual ("one hour", readDictionary[key], "The key should be read back as the same TimeSpan.")

        let legacy =
            JsonSerializer.Deserialize<Dictionary<TimeSpan, string>>("""{"01:00:00":"one hour"}""", options)
            |> nonNull

        Assert.AreEqual ("one hour", legacy[key], "A key in the text form of System.Text.Json should be read too.")

    [<TestMethod>]
    member _.``A document that has a TimeSpan is stored with the ticks and read back`` () =
        let document : TimeSpanTicksConverterTestData.DurationDocument = { Id = "document"; Duration = TimeSpan.FromDays 2.0 }

        let json = JsonSerializer.Serialize (document, options)
        Assert.AreEqual ("""{"id":"document","duration":1728000000000}""", json, "The duration should be a number.")

        let readDocument =
            JsonSerializer.Deserialize<TimeSpanTicksConverterTestData.DurationDocument>(json, options)
            |> nonNull

        Assert.AreEqual (document, readDocument, "The document should be read back unchanged.")

    [<TestMethod>]
    member _.``The text of the default converter does not sort in time order`` () =
        let durations = [|
            TimeSpan.FromDays 2.0
            TimeSpan.FromDays 10.0
            TimeSpan.FromHours 23.0
            TimeSpan.FromHours 1.0
            TimeSpan.FromDays 1.0
        |]

        let byTime = durations |> Array.sort

        let byDefaultText =
            durations
            |> Array.sortWith (fun first second -> String.CompareOrdinal (defaultText first, defaultText second))

        CollectionAssert.AreNotEqual (
            byTime,
            byDefaultText,
            "The text of the default converter should not sort in time order, which is why the converter exists."
        )
