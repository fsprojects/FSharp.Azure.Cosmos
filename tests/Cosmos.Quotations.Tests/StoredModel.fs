// The types that the tests of the JSON layout serialize. They are made for the layout: every way to name a member and
// every way to write a member without a value is here once. A test writes two values of each type with the real
// serializer, one with every optional member present and one with every optional member absent, and compares what
// the layout says with what was written.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open System.Threading
open FSharp.Reflection

/// A converter that writes a collection of texts as one text, so that the serializer does not write its items.
type StoredJoinedTextsConverter () =
    inherit JsonConverter<string array> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : string array =
        match reader.GetString () with
        | null -> [||]
        | joined -> joined.Split ','

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : string array, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue (String.Join (',', value))

/// <summary>
/// A converter factory that hands a type to the converter that the options have for it anyway, as a factory of an
/// application does that only adds something around that converter.
/// </summary>
type StoredForwardingConverterFactory () =
    inherit JsonConverterFactory ()

    /// <inheritdoc />
    override _.CanConvert (_ : Type) : bool = true

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, options : JsonSerializerOptions) : JsonConverter | null =
        options.GetConverter typeToConvert

/// The base of a class hierarchy, which System.Text.Json writes as objects whatever converters there are.
[<AbstractClass>]
type StoredDocument (id : string) =

    /// A property that is only read.
    member _.Id = id

    /// A property that a derived class overrides. The name declared here belongs to this declaration only.
    [<JsonPropertyName "pk">]
    abstract Tenant : string

    /// A property of the base class that can be set.
    member val Note : string | null = null with get, set

    /// A property that a derived class overrides. The condition declared here belongs to this declaration only.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    abstract Remark : string | null with get, set

/// A derived class with a member for every rule that System.Text.Json has for a property of an object.
type StoredPerson (id : string, name : string) =
    inherit StoredDocument (id)

    /// A property named by the naming policy.
    member _.Name = name

    /// The override: it declares no name of its own, so the serializer writes it under the name of the policy,
    /// next to the property of the base class.
    override _.Tenant = "tenant of " + id

    /// The override: it declares no condition of its own, so the serializer writes it as the options say, and not
    /// as the declaration of the base class says.
    override val Remark : string | null = null with get, set

    /// A collection that is only read. The serializer writes it also when it ignores read-only properties.
    member _.Marks = [| "mark of " + id |]

    /// A collection that is only read and that a converter of the member writes as one text. To the serializer it is
    /// no collection then, so the serializer leaves it out when it ignores read-only properties.
    [<JsonConverter(typeof<StoredJoinedTextsConverter>)>]
    member _.Labels = [| "label of " + id |]

    /// A collection that is only read, with a converter of the member that hands it to the converter of the options.
    /// The serializer writes its items, so it is a collection: it is written also when read-only properties are
    /// ignored.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member _.Stamps = [| "stamp of " + id |]

    /// A property with a declared name.
    [<JsonPropertyName "nick_name">]
    member val Nick : string | null = null with get, set

    /// A property that is never written.
    [<JsonIgnore>]
    member val Secret : string | null = null with get, set

    /// A property that is never written and declares a name: it stands for a property that something else writes.
    [<JsonPropertyName "$type"; JsonIgnore>]
    member val Discriminator : string | null = null with get, set

    /// A reference that is left out when it is null, whatever the options say.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    member val Alias : string | null = null with get, set

    /// A nullable that is left out when it has no value, whatever the options say.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)>]
    member val Rank = Nullable<int>() with get, set

    /// A reference that is always written, whatever the options say.
    [<JsonIgnore(Condition = JsonIgnoreCondition.Never)>]
    member val Motto : string | null = null with get, set

    /// A nullable that follows the options.
    member val Age = Nullable<int>() with get, set

    /// An option that follows the options.
    member val Choice : int option = None with get, set

    /// A value option that follows the options.
    member val ValueChoice : int voption = ValueNone with get, set

    /// An option that is left out when it is null: an option without a value is null.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    member val ChoiceUnlessNull : int option = None with get, set

    /// A value option that is left out when it is the default value of its type, which is the one without a value.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)>]
    member val ValueChoiceUnlessDefault : int voption = ValueNone with get, set

    /// A property that is not public and is written because it says so.
    [<JsonInclude>]
    member val internal Hidden : string | null = null with get, set

    /// A field that is written because it says so.
    [<DefaultValue; JsonInclude>]
    val mutable subject : string | null

    /// A field that is written only when the options include fields.
    [<DefaultValue>]
    val mutable plainField : string | null

    /// The properties of the document that no member stands for.
    [<JsonExtensionData>]
    member val Extra = Dictionary<string, JsonElement>(StringComparer.Ordinal) with get, set

/// A class that System.Text.Json refuses to write: a struct is never null, so it cannot be ignored when it is.
type StoredRefused (id : string) =

    /// A property that is only read.
    member _.Id = id

    /// The member that the serializer refuses.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    member val Choice : int voption = ValueNone with get, set

/// A class with the skippable values of FSharp.SystemTextJson, which System.Text.Json alone cannot write.
type StoredTask (id : string) =

    /// A property that is only read.
    member _.Id = id

    /// A skippable value that follows the options.
    member val Estimate : Skippable<int> = Skip with get, set

    /// A skippable value that is left out when it is the default value of its type, which is the skipped one.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)>]
    member val Spent : Skippable<int> = Skip with get, set

/// A record that is the value of a field of another record.
type StoredAuthor = {
    /// The name of the author.
    Name : string
}

/// A record with a field for every rule that FSharp.SystemTextJson has for a field of a record.
type StoredEntry = {
    /// A field named by the naming policy.
    Title : string
    /// A field with a name declared the way System.Text.Json declares one.
    [<JsonPropertyName "body_text">]
    Body : string
    /// A field with a name declared the way FSharp.SystemTextJson declares one, which System.Text.Json does not read.
    [<JsonName "label">]
    Tag : string
    /// A field with a name that is given for a field of a union case. It is not the name of this field, which is
    /// named by the naming policy.
    [<JsonName("other", Field = "Item")>]
    Subtitle : string
    /// A field that is never written.
    [<JsonIgnore>]
    Draft : string
    /// A field that is never written and declares a name: it stands for a property that something else writes.
    [<JsonPropertyName "$type"; JsonIgnore>]
    Discriminator : string
    /// A field that System.Text.Json leaves out when it is null and that FSharp.SystemTextJson never writes: it does
    /// not read the condition.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    Dropped : string | null
    /// A reference that can be null.
    Comment : string | null
    /// A nullable.
    Count : Nullable<int>
    /// An option.
    Choice : int option
    /// A value option.
    ValueChoice : int voption
    /// An option of an option, whose inner absence cannot be told from the outer one when options are unwrapped.
    Nested : string option option
    /// A record, which FSharp.SystemTextJson writes as null when it is null, also when null values are ignored.
    Author : StoredAuthor
} with

    /// A property that is not a field. FSharp.SystemTextJson writes it only when its options include properties.
    member this.Summary = this.Title + "!"

    /// A property that is not a field and asks to be written.
    [<JsonInclude>]
    member this.Shout = this.Title.ToUpperInvariant ()

    /// A property that is not a field and declares a name. Where FSharp.SystemTextJson does not write it, it stands
    /// for a property that something else writes.
    [<JsonPropertyName "first_letter">]
    member this.Initial = this.Title.Substring (0, 1)

    /// A property that is not public. FSharp.SystemTextJson looks at the public properties of a record only, so it
    /// does not write this one, whatever its options say.
    member internal this.Aside = "aside of " + this.Title

    /// A property that is not public and asks to be written. System.Text.Json writes it for an object, and
    /// FSharp.SystemTextJson does not for a record.
    [<JsonInclude>]
    member internal this.Whisper = "whisper of " + this.Title

/// A record with the skippable values of FSharp.SystemTextJson.
type StoredEstimate = {
    /// A field that always has a value.
    Title : string
    /// A skippable value: it is left out when it is skipped, whatever the options say.
    Estimate : Skippable<int>
    /// A skippable option: skipped, without a value or with one.
    Remark : Skippable<string option>
}

/// A generic record, which can be given options for all the types that are made of it.
type StoredBox<'Item> = {
    /// What the box holds.
    Item : 'Item
    /// An option in a generic record.
    Label : string option
}

/// A struct record.
[<Struct>]
type StoredPoint = {
    /// A field that always has a value.
    X : int
    /// An option in a struct record.
    Label : string option
}

/// A converter that writes a point as text, which a layout cannot look into.
type StoredPointAsTextConverter () =
    inherit JsonConverter<StoredPoint> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : StoredPoint =
        reader.Skip ()
        { X = 0; Label = None }

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : StoredPoint, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue $"%d{value.X}"

/// A converter of strings that asks for null and writes a text for it, as a converter of an application can.
type StoredNullAsDashConverter () =
    inherit JsonConverter<string> ()

    /// <inheritdoc />
    override _.HandleNull = true

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : string | null =
        match reader.GetString () with
        | "-" -> null
        | text -> text

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : string | null, _ : JsonSerializerOptions) : unit =
        match value with
        | null -> writer.WriteStringValue "-"
        | value -> writer.WriteStringValue value

/// A converter of strings that leaves null to the serializer, which writes it as null.
type StoredTextConverter () =
    inherit JsonConverter<string> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : string | null = reader.GetString ()

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : string | null, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue value

/// A converter that writes a span as text.
type StoredSpanAsTextConverter () =
    inherit JsonConverter<StoredSpan> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : StoredSpan =
        reader.Skip ()
        { From = 0; To = 0; Note = None }

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : StoredSpan, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue $"%d{value.From}-%d{value.To}"

/// A record that names its converter in an attribute of the type. The serializer asks the converters of the options
/// first, so the named converter writes the record only when none of them takes it.
and [<JsonConverter(typeof<StoredSpanAsTextConverter>)>] StoredSpan = {
    /// A field that always has a value.
    From : int
    /// Another one.
    To : int
    /// An option.
    Note : string option
}

/// <summary>
/// A converter factory of an application that is made of the one of FSharp.SystemTextJson and writes a point itself.
/// A layout that only asked for the factory would take the point for a record of FSharp.SystemTextJson.
/// </summary>
type StoredPointFactory () =
    inherit JsonFSharpConverter (JsonFSharpOptions.Default ())

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, options : JsonSerializerOptions) : JsonConverter =
        if Type.(=) (typeToConvert, typeof<StoredPoint>) then
            StoredPointAsTextConverter ()
        else
            base.CreateConverter (typeToConvert, options)

/// A class with members that name a converter of their own.
type StoredMeeting () =

    /// A nullable that the converter of the member writes.
    [<JsonConverter(typeof<JsonStringEnumConverter>)>]
    member val Day = Nullable<DayOfWeek>() with get, set

    /// A reference whose converter leaves null to the serializer.
    [<JsonConverter(typeof<StoredTextConverter>)>]
    member val Topic : string | null = null with get, set

    /// A reference whose converter asks for null and writes a text for it.
    [<JsonConverter(typeof<StoredNullAsDashConverter>)>]
    member val Host : string | null = null with get, set

    /// An option whose converter is the one that the options have for it anyway.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Alias : string option = None with get, set

    /// A reference that follows the options.
    member val Room : string | null = null with get, set

/// A class that inherits its members from one that System.Text.Json writes as an object. A test has a converter of
/// the options write it as text.
type StoredTalk () =
    inherit StoredMeeting ()

/// A converter that writes a talk as text, which a layout cannot look into.
type StoredTalkAsTextConverter () =
    inherit JsonConverter<StoredTalk> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : StoredTalk =
        reader.Skip ()
        StoredTalk ()

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, _ : StoredTalk, _ : JsonSerializerOptions) : unit = writer.WriteStringValue "talk"

/// Converters as an application can make them.
module StoredConverters =

    /// <summary>
    /// The converter of FSharp.SystemTextJson for a record type, made with options that nothing shows to a layout:
    /// every option is left out without a value, and the names are in snake case.
    /// </summary>
    let quietRecordConverter (recordType : Type) : JsonConverter | null =
        let converterType = typedefof<JsonRecordConverter<_>>.MakeGenericType recordType

        // The converter takes the options of the serializer when it is made, and these are not the ones of the caller.
        let converter =
            Activator.CreateInstance (
                converterType,
                JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower),
                JsonFSharpOptions.Default().WithSkippableOptionFields SkippableOptionFields.Always
            )

        match converter with
        | :? JsonConverter as converter -> converter
        | _ -> null

/// <summary>
/// An attribute of an application that makes the converter of FSharp.SystemTextJson for a record itself, with options
/// that nothing shows to a layout: every option is left out without a value, and the names are in snake case.
/// </summary>
type StoredOwnConverterAttribute () =
    inherit JsonConverterAttribute ()

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type) : JsonConverter | null = StoredConverters.quietRecordConverter typeToConvert

/// <summary>
/// A converter factory of an application that is made of the one of FSharp.SystemTextJson and makes the converter of
/// every record itself, with options that it does not show: its own options are the default ones.
/// </summary>
type StoredQuietFactory () =
    inherit JsonFSharpConverter (JsonFSharpOptions.Default ())

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, options : JsonSerializerOptions) : JsonConverter =
        if FSharpType.IsRecord typeToConvert then
            nonNull (StoredConverters.quietRecordConverter typeToConvert)
        else
            base.CreateConverter (typeToConvert, options)

/// <summary>
/// A resolver of contracts of an application that makes the contract of one type itself, without looking at the
/// converters of the options it is asked with. Every other contract is the work of the serializer.
/// </summary>
/// <param name="make">Makes the contract of the type for the options that ask.</param>
type StoredContractResolver<'Value> (make : JsonSerializerOptions -> JsonTypeInfo) =
    inherit DefaultJsonTypeInfoResolver ()

    /// <inheritdoc />
    override _.GetTypeInfo (valueType : Type, options : JsonSerializerOptions) : JsonTypeInfo =
        if Type.(=) (valueType, typeof<'Value>) then
            make options
        else
            base.GetTypeInfo (valueType, options)

/// <summary>
/// A converter factory that counts how often it makes a converter, and hands the type to the converter that the
/// options have for it. A test sees by the count whether the layout asked the serializer again.
/// </summary>
type StoredCountingConverterFactory () =
    inherit JsonConverterFactory ()

    static let mutable made = 0

    /// How often the factories of this type have made a converter.
    static member Made : int = Volatile.Read &made

    /// <inheritdoc />
    override _.CanConvert (_ : Type) : bool = true

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, options : JsonSerializerOptions) : JsonConverter | null =
        Interlocked.Increment &made |> ignore
        options.GetConverter typeToConvert

/// A class with a read-only collection whose converter factory counts its work. Only one test may ask about it.
type StoredCounted () =

    /// A property that can be set.
    member val Title = "" with get, set

    /// The collection.
    [<JsonConverter(typeof<StoredCountingConverterFactory>)>]
    member _.Stamps = [| "stamp" |]

/// <summary>
/// A converter factory that counts its work like the one above and holds every call at a gate until a test opens it,
/// so that the calls of several threads meet inside it.
/// </summary>
type StoredGatedConverterFactory () =
    inherit JsonConverterFactory ()

    static let mutable made = 0
    static let gate = new ManualResetEventSlim (true)

    /// How often the factories of this type have made a converter.
    static member Made : int = Volatile.Read &made

    /// The gate that every call waits at; open until a test closes it.
    static member Gate : ManualResetEventSlim = gate

    /// <inheritdoc />
    override _.CanConvert (_ : Type) : bool = true

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, options : JsonSerializerOptions) : JsonConverter | null =
        Interlocked.Increment &made |> ignore
        gate.Wait ()
        options.GetConverter typeToConvert

/// A class with a read-only collection whose converter factory waits at a gate. Only one test may ask about it.
type StoredGated () =

    /// A property that can be set.
    member val Title = "" with get, set

    /// The collection.
    [<JsonConverter(typeof<StoredGatedConverterFactory>)>]
    member _.Stamps = [| "stamp" |]

/// A record whose converter an attribute of the application makes.
[<StoredOwnConverter>]
type StoredCard = {
    /// A field that always has a value.
    FirstName : string
    /// An option.
    NickName : string option
}

/// A union with a case that is represented by null.
[<CompilationRepresentation(CompilationRepresentationFlags.UseNullAsTrueValue)>]
type StoredGrade =
    /// The case that is null.
    | Ungraded
    /// A case with a value.
    | Graded of points : int

/// A union without such a case: a null of its type is no value of it.
type StoredMark =
    /// A case without a value.
    | Unmarked
    /// A case with a value.
    | Marked of points : int

/// A union without any field. Where a case without fields is written as its tag alone, FSharp.SystemTextJson writes
/// such a union by a converter that cannot write a null.
type StoredSide =
    /// One case.
    | Front
    /// Another case.
    | Back

/// A union that is written as the option it wraps. FSharp.SystemTextJson reads a value of it from a null, so a null
/// of its type is a value to the library.
type StoredNote = StoredNote of string option

/// A union that is written as the text it wraps. The library reads no value of it from a null, unless its options
/// let a field of any type be null.
type StoredCaption = StoredCaption of string

/// A union that is written as the number it wraps, which is null only where a field of any type may be.
type StoredNumber = StoredNumber of int

/// A union that is written as the value option it wraps, which is null where options are written as their values.
type StoredCount = StoredCount of int voption

/// A union that is written as the skippable value it wraps, which is null where the value under it can be.
type StoredHint = StoredHint of Skippable<string option>

/// A union that is written as a skippable text, which cannot be null.
type StoredRemark = StoredRemark of Skippable<string>

/// <summary>
/// A union that is written as a value option of an option. Where value options are not written as their values, the
/// library still takes the value option for a skippable value when it always skips options, and the option under it
/// can be null.
/// </summary>
type StoredDeep = StoredDeep of string option voption

/// A union that is written as another such union, which is null where that one is.
type StoredEnvelope = StoredEnvelope of StoredNote

/// A union that is written as the unit value, which is null.
type StoredNothing = StoredNothing of unit

/// A record with values of types of F# that can be null without being options. FSharp.SystemTextJson writes a null
/// of a record, a union or a tuple also where it leaves a null out, unless the null is a value to it: a case of the
/// union, or the null of a union that is written as the value it wraps where that value can be null.
type StoredPaper = {
    /// A field that always has a value.
    Heading : string
    /// A union with a case that is null.
    Grade : StoredGrade
    /// A union without one.
    Mark : StoredMark
    /// A union that wraps an option.
    Note : StoredNote
    /// A union that wraps a text.
    Caption : StoredCaption
    /// A list, which is a union.
    Lines : string list
    /// A set, which is a class.
    Tags : Set<string>
}

/// <summary>
/// A record that asks for options of its own in an attribute, with a union of one case. The options that the
/// attribute makes inherit the encoding of unions, so the common options say whether the union is written as the
/// value it wraps.
/// </summary>
[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type StoredInherited = {
    /// The union.
    Wrapped : StoredNote
    /// A field that always has a value.
    Stated : string
}

/// A class with a property of any type, which System.Text.Json writes as an object.
type StoredSlot<'Item> () =

    /// What the slot holds: nothing, unless it is set.
    member val Item : 'Item = Unchecked.defaultof<'Item> with get, set

    /// A property that always has a value.
    member val Count = 1 with get, set

/// A record that asks for options of its own in an attribute.
[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type StoredMarked = {
    /// A field that always has a value.
    Text : string
    /// An option that the options of the attribute leave out without a value.
    Note : string option
    /// A reference that can be null. A converter that the attribute makes does not know that the options ignore null.
    Remark : string | null
}

/// <summary>
/// A type of the model as the tests write it: once with every optional member present, every member holding a value
/// that no other member holds, and once with every optional member absent.
/// </summary>
[<Struct>]
type StoredSample = {
    /// What the type is; the name of a test result holds it.
    Name : string
    /// The value with every optional member present.
    Present : obj
    /// The value with every optional member absent.
    Absent : obj
    /// Whether the value can be written without FSharp.SystemTextJson: it has no skippable value.
    IsPlain : bool
}

/// The values of the model.
module StoredSamples =

    /// The person with every member set.
    let person =
        let present =
            StoredPerson (
                "id-1",
                "name-1",
                Note = "note-1",
                Remark = "remark-1",
                Nick = "nick-1",
                Secret = "secret-1",
                Discriminator = "person+v1",
                Alias = "alias-1",
                Rank = Nullable 11,
                Motto = "motto-1",
                Age = Nullable 12,
                Choice = Some 13,
                ValueChoice = ValueSome 14,
                ChoiceUnlessNull = Some 15,
                ValueChoiceUnlessDefault = ValueSome 17,
                Hidden = "hidden-1"
            )

        present.subject <- "subject-1"
        present.plainField <- "plain-1"

        {
            Name = "a class"
            Present = present
            Absent = StoredPerson ("id-2", "name-2")
            IsPlain = true
        }

    /// The task with its skippable values.
    let task = {
        Name = "a class with skippable values"
        Present = StoredTask ("id-1", Estimate = Include 21, Spent = Include 22)
        Absent = StoredTask "id-2"
        IsPlain = false
    }

    /// The record.
    let entry = {
        Name = "a record"
        Present =
            ({
                Title = "title-1"
                Body = "body-1"
                Tag = "tag-1"
                Subtitle = "subtitle-1"
                Draft = "draft-1"
                Discriminator = "entry+v1"
                Dropped = "dropped-1"
                Comment = "comment-1"
                Count = Nullable 31
                Choice = Some 32
                ValueChoice = ValueSome 33
                Nested = Some (Some "nested-1")
                Author = { Name = "author-1" }
            }
            : StoredEntry)
        Absent =
            ({
                Title = "title-2"
                Body = "body-2"
                Tag = "tag-2"
                Subtitle = "subtitle-2"
                Draft = "draft-2"
                Discriminator = "entry+v1"
                Dropped = null
                Comment = null
                Count = Nullable ()
                Choice = None
                ValueChoice = ValueNone
                Nested = None
                Author = Unchecked.defaultof<StoredAuthor>
            }
            : StoredEntry)
        IsPlain = true
    }

    /// The record with skippable values.
    let estimate = {
        Name = "a record with skippable values"
        Present = ({ Title = "title-1"; Estimate = Include 41; Remark = Include (Some "remark-1") } : StoredEstimate)
        Absent = ({ Title = "title-2"; Estimate = Skip; Remark = Skip } : StoredEstimate)
        IsPlain = false
    }

    /// The struct record.
    let point = {
        Name = "a struct record"
        Present = ({ X = 51; Label = Some "label-1" } : StoredPoint)
        Absent = ({ X = 52; Label = None } : StoredPoint)
        IsPlain = true
    }

    /// A generic record.
    let box = {
        Name = "a generic record"
        Present = ({ Item = 71; Label = Some "label-1" } : StoredBox<int>)
        Absent = ({ Item = 72; Label = None } : StoredBox<int>)
        IsPlain = true
    }

    /// An anonymous record.
    let anonymous = {
        Name = "an anonymous record"
        Present = {| Title = "title-1"; Choice = Some 61; ValueChoice = ValueSome 62 |}
        Absent = {|
            Title = "title-2"
            Choice = (None : int option)
            ValueChoice = (ValueNone : int voption)
        |}
        IsPlain = true
    }

    /// The record that asks for options of its own.
    let marked = {
        Name = "a record with options in an attribute"
        Present = ({ Text = "text-1"; Note = Some "note-1"; Remark = "remark-1" } : StoredMarked)
        Absent = ({ Text = "text-2"; Note = None; Remark = null } : StoredMarked)
        IsPlain = true
    }

    /// The record with the union that has a case that is null.
    let paper = {
        Name = "a record with values of types of F# that can be null"
        Present =
            ({
                Heading = "heading-1"
                Grade = Graded 71
                Mark = Marked 72
                Note = StoredNote (Some "note-71")
                Caption = StoredCaption "caption-71"
                Lines = List.singleton "line-71"
                Tags = Set.singleton "tag-71"
            }
            : StoredPaper)
        Absent =
            ({
                Heading = "heading-2"
                Grade = Ungraded
                Mark = Unchecked.defaultof<StoredMark>
                Note = Unchecked.defaultof<StoredNote>
                Caption = Unchecked.defaultof<StoredCaption>
                Lines = Unchecked.defaultof<string list>
                Tags = Unchecked.defaultof<Set<string>>
            }
            : StoredPaper)
        IsPlain = false
    }

    /// Every sample.
    let all =
        seq {
            person
            task
            entry
            estimate
            point
            box
            anonymous
            marked
            paper
        }
        |> Collections.Immutable.ImmutableArray.CreateRange
