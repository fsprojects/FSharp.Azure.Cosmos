// The types that the tests of the JSON layout serialize to see how the members of a document are named. They are made
// for the layout: every way to name a member is here once. A test writes a value of each type with the real serializer
// and compares what the layout says with what was written.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open System.Threading

/// An enumeration, which is written as a number or by the name of the value.
type StoredColour =
    | Red = 1
    | DarkBlue = 2

/// An enumeration of flags: a value can be several of them at once.
[<Flags>]
type StoredAccess =
    | None = 0
    | Read = 1
    | Write = 2

/// An enumeration whose values leave no gap from zero up to the last, so that the number after it is the smallest one
/// that no constant has and no combination of two constants is.
type StoredDense =
    | Zero = 0
    | One = 1
    | Two = 2
    | Three = 3

/// An enumeration of the numbers from one to eight: every combination of two of the first seven is a constant itself,
/// so the first combination that is no constant needs the eighth.
type StoredWide =
    | One = 1
    | Two = 2
    | Three = 3
    | Four = 4
    | Five = 5
    | Six = 6
    | Seven = 7
    | Eight = 8

/// A converter of texts that writes a text as it is, which is a converter that a member can name.
type StoredTextConverter () =
    inherit JsonConverter<string> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : string | null = reader.GetString ()

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : string | null, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue value

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

    /// A property that a derived class overrides.
    abstract Remark : string | null with get, set

/// A derived class with a member for every rule that System.Text.Json has for the name of a property of an object.
type StoredPerson (id : string, name : string) =
    inherit StoredDocument (id)

    /// A property named by the naming policy.
    member _.Name = name

    /// The override: it declares no name of its own, so the serializer writes it under the name of the policy,
    /// next to the property of the base class.
    override _.Tenant = "tenant of " + id

    /// The override of a property that can be set.
    override val Remark : string | null = null with get, set

    /// A property with a declared name.
    [<JsonPropertyName "nick_name">]
    member val Nick : string | null = null with get, set

    /// A property that is never written.
    [<JsonIgnore>]
    member val Secret : string | null = null with get, set

    /// A property that is never written and declares a name: it stands for a property that something else writes.
    [<JsonPropertyName "$type"; JsonIgnore>]
    member val Discriminator : string | null = null with get, set

    /// A nullable.
    member val Age = Nullable<int>() with get, set

    /// An option.
    member val Choice : int option = None with get, set

    /// A value option.
    member val ValueChoice : int voption = ValueNone with get, set

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

/// A class that can hold itself, with a floating-point number: the settings of the serializer that change how a cycle
/// and a number that is not finite are written need a value to show it.
type StoredCycle () =

    /// A floating-point number.
    member val Value = 0.0 with get, set

    /// The next object, which can be the object itself.
    member val Next : StoredCycle | null = null with get, set

/// A class that System.Text.Json refuses to write: a struct is never null, so it cannot be ignored when it is.
type StoredRefused (id : string) =

    /// A property that is only read.
    member _.Id = id

    /// The member that the serializer refuses.
    [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
    member val Choice : int voption = ValueNone with get, set

/// A record that is the value of a field of another record.
type StoredAuthor = {
    /// The name of the author.
    Name : string
}

/// A record whose fields only its own file can reach. FSharp.SystemTextJson writes them all the same.
type StoredKey = private {
    /// A field that always has a value.
    Code : string
} with

    /// A key with the code.
    static member Of (code : string) : StoredKey = { Code = code }

/// A base class with a member that is never written and declares a name: it stands for a property that something else
/// writes.
[<AbstractClass>]
type StoredTagged () =

    /// The member.
    [<JsonPropertyName "$tag"; JsonIgnore>]
    abstract Tag : string

/// A class that overrides the member, which it does not write either, and declares no name for it.
type StoredTaggedAgain () =
    inherit StoredTagged ()

    /// <inheritdoc />
    [<JsonIgnore>]
    override _.Tag = "again"

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
    /// An option of an option.
    Nested : string option option
    /// A record.
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

/// A record with two fields whose names a naming policy makes one: camel case spells both as "title".
type StoredTwin = {
    /// A field.
    Title : string
    /// Another one, whose name differs from the first one by the case of a letter.
    title : string
}

/// A record with two fields that declare one name, each the way of one of the libraries.
type StoredNamedTwice = {
    /// A field with a name declared the way FSharp.SystemTextJson declares one.
    [<JsonName "same">]
    First : int
    /// A field with the same name, declared the way System.Text.Json declares one.
    [<JsonPropertyName "same">]
    Second : int
}

/// A record with a property that asks to be written under the name of a field.
type StoredClash = {
    /// A field.
    Total : int
} with

    /// A property that is written, and has the name of the field.
    [<JsonInclude; JsonPropertyName "Total">]
    member this.Sum = this.Total + 1

/// A record with a field that is never written and declares the name of one that is.
type StoredHidden = {
    /// A field.
    Visible : int
    /// A field that is never written, and stands for a property of the name of the other one.
    [<JsonPropertyName "Visible"; JsonIgnore>]
    Hidden : int
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
    /// A field that always has a value, and declares a name.
    [<JsonPropertyName "x_value">]
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

/// A class with a member that names a converter of its own.
type StoredMeeting () =

    /// A reference whose converter writes a text as it is.
    [<JsonConverter(typeof<StoredTextConverter>)>]
    member val Topic : string | null = null with get, set

    /// A reference that follows the options.
    member val Room : string | null = null with get, set

    /// An enumeration that follows the options.
    member val Colour = StoredColour.Red with get, set

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

/// A converter factory of an application that is derived from the one of FSharp.SystemTextJson. It makes the
/// converters that the library makes, and a class derived from the library can make them with other options than the
/// ones that it shows.
type StoredDerivedFactory () =
    inherit JsonFSharpConverter (JsonFSharpOptions.Default ())

/// A resolver of contracts of an application that has a contract for no type: the serializer refuses every type.
type StoredBareResolver () =

    interface IJsonTypeInfoResolver with

        /// <inheritdoc />
        member _.GetTypeInfo (_ : Type, _ : JsonSerializerOptions) : JsonTypeInfo | null = null

/// A record that asks for options of its own in an attribute.
[<JsonFSharpConverter(SkippableOptionFields = SkippableOptionFields.Always)>]
type StoredMarked = {
    /// A field that always has a value.
    Text : string
    /// An option that the options of the attribute leave out without a value.
    Note : string option
}

/// <summary>
/// A naming policy that counts how often it spells a name. A test sees by the count whether the layout worked an
/// answer out again. Every type argument has a count of its own: tests run at a time, and each test that counts names
/// a type that no other test names.
/// </summary>
type StoredCountingNamingPolicy<'Counted> () =
    inherit JsonNamingPolicy ()

    static let mutable spelled = 0

    /// How often the policies of this type have spelled a name.
    static member Spelled : int = Volatile.Read &spelled

    /// <inheritdoc />
    override _.ConvertName (name : string) : string =
        Interlocked.Increment &spelled |> ignore
        name

/// <summary>
/// A naming policy that counts how often it spells a name, like the one above, and holds every call at a gate until a
/// test opens it, so that the calls of several threads meet inside it. Only one test may use it.
/// </summary>
type StoredGatedNamingPolicy () =
    inherit JsonNamingPolicy ()

    static let mutable spelled = 0
    static let gate = new ManualResetEventSlim (true)

    /// How often the policies of this type have spelled a name.
    static member Spelled : int = Volatile.Read &spelled

    /// The gate that every call waits at; open until a test closes it.
    static member Gate : ManualResetEventSlim = gate

    /// <inheritdoc />
    override _.ConvertName (name : string) : string =
        Interlocked.Increment &spelled |> ignore
        gate.Wait ()
        name

/// A record whose field names are spelled by a policy that counts. Only one test may ask about it.
type StoredCounted = {
    /// A field.
    Title : string
}

/// A record whose field names are spelled by a policy that waits at a gate. Only one test may ask about it.
type StoredGated = {
    /// A field.
    Title : string
}

/// <summary>
/// A type of the model as the tests write it: with every member holding a value that no other member holds, so that
/// the member can be found in the document by its value.
/// </summary>
[<Struct>]
type StoredSample = {
    /// What the type is; the name of a test result holds it.
    Name : string
    /// The value.
    Value : obj
}

/// The values of the model.
module StoredSamples =

    /// The person with every member set.
    let person =
        let person =
            StoredPerson (
                "id-1",
                "name-1",
                Note = "note-1",
                Remark = "remark-1",
                Nick = "nick-1",
                Secret = "secret-1",
                Discriminator = "person+v1",
                Age = Nullable 12,
                Choice = Some 13,
                ValueChoice = ValueSome 14,
                Hidden = "hidden-1"
            )

        person.subject <- "subject-1"
        person.plainField <- "plain-1"
        { Name = "a class"; Value = person }

    /// The record.
    let entry = {
        Name = "a record"
        Value =
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
    }

    /// The struct record.
    let point = {
        Name = "a struct record"
        Value = ({ X = 51; Label = Some "label-1" } : StoredPoint)
    }

    /// A generic record.
    let box = {
        Name = "a generic record"
        Value = ({ Item = 71; Label = Some "label-1" } : StoredBox<int>)
    }

    /// An anonymous record.
    let anonymous = {
        Name = "an anonymous record"
        Value = {| Title = "title-1"; Choice = Some 61; ValueChoice = ValueSome 62 |}
    }

    /// The record that asks for options of its own.
    let marked = {
        Name = "a record with options in an attribute"
        Value = ({ Text = "text-1"; Note = Some "note-1" } : StoredMarked)
    }

    /// Every sample.
    let all =
        seq {
            person
            entry
            point
            box
            anonymous
            marked
        }
        |> Collections.Immutable.ImmutableArray.CreateRange
