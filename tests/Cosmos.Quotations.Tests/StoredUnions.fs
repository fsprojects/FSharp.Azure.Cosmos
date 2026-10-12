// The unions that the tests of the JSON layout serialize to see how a union is written. A test writes a value of
// every case with the real serializer and looks in the document for what the layout says: the mark of the case, which
// no value of another case may have, and the values of the fields at the places that the layout gives.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.Reflection

/// A union with a case for every way to declare a case and its fields that the layout describes.
type StoredShape =
    /// A case without fields.
    | Blank
    /// A case with one field that the source names.
    | Circle of radius : int
    /// A case with two fields that the source does not name.
    | Rect of int * string
    /// A case whose one field is a record.
    | Signed of StoredAuthor
    /// A case with a name declared the way FSharp.SystemTextJson declares one, and a field that is an option.
    | [<JsonName "poly">] Polygon of sides : int * label : string option
    /// A case with a name declared the way System.Text.Json declares one.
    | [<JsonPropertyName "dot">] Point

/// A union without any field.
type StoredSide =
    /// A case that is written by its name.
    | Front
    /// A case with a name that it declares.
    | [<JsonName "reverse">] Back

/// A union with a case that declares the name of a field, which the layout does not describe.
type StoredPairing =
    /// A case that declares the name of a field, and whose fields are of one type.
    | [<JsonName("first", Field = "Item1")>] Pair of int * int
    /// A case as any other.
    | Single of int

/// A union without any field, with cases that declare a number and a boolean in place of a name.
type StoredLevel =
    /// A case that is written by its name.
    | Low
    /// A case that is written as a number.
    | [<JsonName 7>] High
    /// A case that is written as a boolean.
    | [<JsonName true>] Sure

/// A union with one case that declares a number in place of its name, next to a case that is written by its name.
type StoredNumbered =
    /// A case that is written as a number.
    | [<JsonName 7>] Seven
    /// A case that is written by its name.
    | Eight

/// A union with one case that declares a boolean in place of its name, next to a case that is written by its name.
type StoredBoolean =
    /// A case that is written as a boolean.
    | [<JsonName true>] Affirmed
    /// A case that is written by its name.
    | Denied

/// A union with a case that is represented by null.
[<CompilationRepresentation(CompilationRepresentationFlags.UseNullAsTrueValue)>]
type StoredGrade =
    /// The case that is null.
    | Ungraded
    /// A case with a value.
    | Graded of points : int

/// A union with one case that has one field, which is how an application wraps a value.
type StoredTicket = StoredTicket of Guid

/// A union with one case that has several fields.
type StoredRange = StoredRange of first : int * last : int

/// A union with one case that has no field.
type StoredToken = | StoredToken

/// A union with a case whose one field is a record that has a private representation.
type StoredVault =
    /// The case with the record.
    | Held of StoredKey
    /// A case without fields.
    | Vacant

/// A struct union.
[<Struct>]
type StoredSwitch =
    /// A case without fields.
    | On
    /// A case with a field.
    | Off of reason : string

/// A union whose cases only its own module can use.
type StoredSecret =
    private
    | Code of code : int
    | Unlocked

    /// A value of the case with a field.
    static member OfCode (code : int) : StoredSecret = Code code

    /// The value of the case without a field.
    static member Open : StoredSecret = Unlocked

/// A generic union.
type StoredOutcome<'Value> =
    /// A case with a field of the type argument.
    | Fine of 'Value
    /// A case with a field of a fixed type.
    | Failed of reason : string

/// A union with options of its own in an attribute that sets the whole encoding.
[<JsonFSharpConverter(UnionEncoding =
                          (JsonUnionEncoding.ExternalTag
                           ||| JsonUnionEncoding.NamedFields),
                      UnionTagName = "kind")>]
type StoredStamp =
    /// A case without fields.
    | Unstamped
    /// A case with fields.
    | Stamped of text : string * size : int

/// <summary>
/// A union with options of its own in an attribute that sets a part of the encoding. FSharp.SystemTextJson replaces
/// the whole encoding of such options by the common one, so the part is lost, and the names stay.
/// </summary>
[<JsonFSharpConverter(UnionNamedFields = true, UnionTagName = "kind", UnionFieldsName = "data")>]
type StoredLabel =
    /// A case without fields.
    | Unlabelled
    /// A case with fields.
    | Labelled of text : string * size : int

/// <summary>
/// A record that names the converter of FSharp.SystemTextJson in an attribute. The options that the attribute makes are
/// the record's own when the common options let an attribute give them.
/// </summary>
[<JsonFSharpConverter>]
type StoredSigner = {
    /// A field that always has a value.
    SignedAs : string
    /// An option.
    Alias : string option
}

/// A union with a case whose one field is that record.
type StoredDeal =
    /// The case with the record.
    | Dealt of StoredSigner
    /// A case without fields.
    | Undealt

/// A union that FSharp.SystemTextJson refuses: a case may have one attribute with its names, and this one has two.
type StoredTwice =
    /// The case with two attributes.
    | [<JsonName "one"; JsonName "two">] Named
    /// A case as any other.
    | Plain

/// A union whose cases declare one tag.
type StoredSameTag =
    /// A case with a tag.
    | [<JsonName "x">] Alpha
    /// A case with the same tag.
    | [<JsonName "x">] Beta of int

/// A union whose cases a naming policy writes with one tag: snake case spells both of them as "foo_bar".
type StoredFoldedTag =
    /// A case.
    | FooBar
    /// A case whose name differs from the first one by an underscore.
    | Foo_Bar of int

/// A union with a case of two fields whose names differ by an underscore, which snake case spells alike: "foo_bar".
type StoredFieldsAlike =
    /// A case with two fields.
    | Both of FooBar : int * Foo_Bar : int
    /// A case with one field, which nothing spells alike.
    | Alone of int

/// A union whose cases declare tags that differ by the case of the letters only.
type StoredCasedTag =
    /// A case with a tag in small letters.
    | [<JsonName "tag">] Lower
    /// A case with a tag in capital letters.
    | [<JsonName "TAG">] Upper of int

/// <summary>
/// A converter factory of an application that makes the converter of FSharp.SystemTextJson for a union itself, with a
/// naming policy for the tags that no options show: the tags are in capital letters.
/// </summary>
type StoredLoudUnionConverterFactory () =
    inherit JsonConverterFactory ()

    /// <inheritdoc />
    override _.CanConvert (typeToConvert : Type) : bool = FSharpType.IsUnion (typeToConvert, true)

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, _ : JsonSerializerOptions) : JsonConverter | null =
        JsonUnionConverter(JsonFSharpOptions.Default().WithUnionTagNamingPolicy JsonNamingPolicy.SnakeCaseUpper)
            .CreateConverter(typeToConvert, JsonSerializerOptions ())

/// A record with a member of a union type, for every union: a test asks about the union as the value of the member.
type StoredHolder<'Union> = {
    /// The union.
    Held : 'Union
}

/// A class with members of a union type.
type StoredDrawing () =

    /// A union that is written as the options say.
    member val Shape = Blank with get, set

    /// A union with a converter of its own, which is the one of FSharp.SystemTextJson with options of its own.
    [<JsonConverter(typeof<StoredLoudUnionConverterFactory>)>]
    member val Loud = Blank with get, set

    /// A union that the serializer never writes.
    [<JsonIgnore>]
    member val Hidden = Blank with get, set

/// A way to have FSharp.SystemTextJson write the name of the tag of a union twice in one object.
type StoredTagTwice = {
    /// What gives a case a second property of the name of the tag; the name of a test result holds it.
    Reason : string
    /// The name of the tag.
    TagName : string
    /// The naming policy of the serializer; none when the names are written as they are.
    PropertyNaming : JsonNamingPolicy | null
    /// The options of FSharp.SystemTextJson that the union is written with.
    Settings : JsonFSharpOptions
    /// A value of a case that is written with the name twice.
    Value : StoredShape
}

/// The values of a union type that the tests write: one of every case at least.
[<Struct>]
type StoredUnionSample = {
    /// What the union is; the name of a test result holds it.
    Name : string
    /// The union type.
    UnionType : Type
    /// The values.
    Values : ImmutableArray<objnull>
}

/// The values of the unions.
module StoredUnionSamples =

    /// A sample of a union type.
    let private sample (name : string) (values : 'Union seq) : StoredUnionSample = {
        Name = name
        UnionType = typeof<'Union>
        Values = values |> Seq.map box |> ImmutableArray.CreateRange
    }

    /// Every union that is described whenever the serializer writes it, with a value of every case.
    let all =
        seq {
            sample
                "a union with every kind of case"
                (seq {
                    Blank
                    Circle 3
                    Rect (4, "rect")
                    Signed { Name = "signer" }
                    Polygon (5, Some "label")
                    Polygon (6, None)
                    Point
                })
            sample
                "a union without fields"
                (seq {
                    Front
                    Back
                })
            sample "a union that wraps a value" (Seq.singleton (StoredTicket (Guid "0f8fad5b-d9cb-469f-a165-70867728950e")))
            sample "a union with one case of several fields" (Seq.singleton (StoredRange (1, 9)))
            sample "a union with one case without a field" (Seq.singleton StoredToken)
            sample
                "a union with tags that differ by the case of the letters"
                (seq {
                    Lower
                    Upper 4
                })
            sample
                "a union with a record that has a private representation"
                (seq {
                    Held (StoredKey.Of "key-1")
                    Vacant
                })
            sample
                "a struct union"
                (seq {
                    On
                    Off "reason"
                })
            sample
                "a union with private cases"
                (seq {
                    StoredSecret.OfCode 11
                    StoredSecret.Open
                })
            sample
                "a generic union of a number"
                (seq {
                    Fine 12
                    Failed "failure"
                })
            sample
                "a generic union of a text"
                (seq {
                    Fine "fine"
                    Failed "failure"
                })
            // The attributes ask for options of their own, which the common options do not allow: they are ignored.
            sample
                "a union with the whole encoding in an attribute"
                (seq {
                    Unstamped
                    Stamped ("stamp", 13)
                })
            sample
                "a union with a part of the encoding in an attribute"
                (seq {
                    Unlabelled
                    Labelled ("label", 14)
                })
            sample
                "a union with a record that has options in an attribute"
                (seq {
                    Dealt { SignedAs = "signer"; Alias = Some "alias" }
                    Undealt
                })
        }
        |> ImmutableArray.CreateRange

    /// The unions that the layout does not describe whatever the options are, with the reason.
    let notDescribed : ImmutableArray<struct (string * Type)> =
        seq {
            struct ("its tags are a number and a boolean", typeof<StoredLevel>)
            struct ("one of its tags is a number", typeof<StoredNumbered>)
            struct ("one of its tags is a boolean", typeof<StoredBoolean>)
            struct ("a case of it is represented by null", typeof<StoredGrade>)
            struct ("a case of it declares the name of a field", typeof<StoredPairing>)
            struct ("two of its cases declare one tag", typeof<StoredSameTag>)
        }
        |> ImmutableArray.CreateRange

    /// Every way to have the name of the tag written twice.
    let tagsTwice =
        seq {
            {
                Reason = "a field has the name of the tag"
                TagName = "radius"
                PropertyNaming = null
                Settings = JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields().WithUnionTagName "radius"
                Value = Circle 3
            }
            {
                Reason = "a member of the record of a case has the name of the tag"
                TagName = "Name"
                PropertyNaming = null
                Settings =
                    JsonFSharpOptions
                        .Default()
                        .WithUnionInternalTag()
                        .WithUnionNamedFields()
                        .WithUnionUnwrapRecordCases()
                        .WithUnionTagName
                        "Name"
                Value = Signed { Name = "signer" }
            }
            {
                Reason = "a member of the record of a case has the name of the tag after the naming policy has spelled it"
                TagName = "name"
                PropertyNaming = JsonNamingPolicy.CamelCase
                Settings =
                    JsonFSharpOptions
                        .Default()
                        .WithUnionInternalTag()
                        .WithUnionNamedFields()
                        .WithUnionUnwrapRecordCases()
                        .WithUnionTagName
                        "name"
                Value = Signed { Name = "signer" }
            }
        }
        |> ImmutableArray.CreateRange
