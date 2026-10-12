// The unions that the tests of the JSON layout serialize to see how a union is written. A test writes a value of
// every case with the real serializer and looks in the document for what the layout says: the mark of the case, which
// no value of another case may have, and the values of the fields at the places that the layout gives.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open FSharp.Reflection

/// A union with a case for every way to declare a case and its fields.
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
    /// A case that declares the name of a field, and whose fields are of one type.
    | [<JsonName("first", Field = "Item1")>] Pair of int * int
    /// A case with a name declared the way System.Text.Json declares one.
    | [<JsonPropertyName "dot">] Point

/// A union without any field, with cases that declare a number and a boolean in place of a name.
type StoredLevel =
    /// A case that is written by its name.
    | Low
    /// A case that is written as a number.
    | [<JsonName 7>] High
    /// A case that is written as a boolean.
    | [<JsonName true>] Sure

/// A union with one case that has one field, which is how an application wraps a value.
type StoredTicket = StoredTicket of Guid

/// A union with one case that has several fields.
type StoredRange = StoredRange of first : int * last : int

/// A union with one case that has no field.
type StoredToken = | StoredToken

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
/// A union with options of its own in an attribute that unwrap a record in a case, and a case with one field. A test
/// asks about it by itself: such a record is written with the options of the union, which the layout of the record
/// does not follow.
/// </summary>
[<JsonFSharpConverter(UnionEncoding =
    (JsonUnionEncoding.AdjacentTag
     ||| JsonUnionEncoding.UnwrapRecordCases
     ||| JsonUnionEncoding.UnwrapSingleFieldCases))>]
type StoredLoan =
    /// A case whose one field is a record.
    | Lent of StoredAuthor
    /// A case without fields.
    | Returned

/// <summary>
/// A record that names the converter of FSharp.SystemTextJson in an attribute. Where no converter of the options
/// takes records, the attribute makes the converter of the record by itself, with options of the serializer of its
/// own.
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

/// <summary>
/// A union whose cases hold what can be without a value, one field of every kind, in every arrangement that the fields
/// of a case have: with names, without names, and alone. Two more cases hold values of the types that are null in
/// ways of their own, and of the types that are written in more than one way.
/// </summary>
type StoredParcel =
    /// Fields with names.
    | Packed of
        note : string option *
        weight : int voption *
        mark : Skippable<string> *
        count : Nullable<int> *
        sender : (string | null)
    /// Fields without names.
    | Loose of string option * int voption * Skippable<string> * Nullable<int> * (string | null)
    /// One field, an option.
    | WithNote of string option
    /// One field, a value option.
    | WithWeight of int voption
    /// One field, a skippable value.
    | WithMark of Skippable<string>
    /// One field, a nullable.
    | WithCount of Nullable<int>
    /// One field, a reference.
    | WithSender of (string | null)
    /// A record, a union and a union with a case that is null.
    | Enclosed of author : StoredAuthor * shape : StoredShape * grade : StoredGrade
    /// An enumeration, a tuple, a collection and a map.
    | Listed of colour : StoredColour * pair : struct (int * string) * tags : string list * scores : Map<string, int>
    /// A case without fields.
    | Unpacked

    /// A property that is no field of a case, of a type that can be null.
    member _.Remark : string | null = null

    /// A property that is no field of a case, of a type that is written in more than one way.
    member _.Shade : StoredColour = StoredColour.Red

/// A union with options of its own in an attribute, which name the fields and always skip an option without a value.
[<JsonFSharpConverter(UnionEncoding =
                          (JsonUnionEncoding.AdjacentTag
                           ||| JsonUnionEncoding.NamedFields
                           ||| JsonUnionEncoding.UnwrapOption),
                      SkippableOptionFields = SkippableOptionFields.Always)>]
type StoredSeal =
    /// A case with an option and a value option.
    | Sealed of by : string option * year : int voption
    /// A case without fields.
    | Unsealed

/// A union that FSharp.SystemTextJson refuses: a case may have one attribute with its names, and this one has two.
type StoredTwice =
    /// The case with two attributes.
    | [<JsonName "one"; JsonName "two">] Named
    /// A case as any other.
    | Plain

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

/// <summary>
/// A naming policy that counts how often it spells a name. A test sees by the count whether the layout worked an
/// answer out again. Only one test may use it.
/// </summary>
type StoredCountingNamingPolicy () =
    inherit JsonNamingPolicy ()

    static let mutable spelled = 0

    /// How often the policies of this type have spelled a name.
    static member Spelled : int = Volatile.Read &spelled

    /// <inheritdoc />
    override _.ConvertName (name : string) : string =
        Interlocked.Increment &spelled |> ignore
        name.ToUpperInvariant ()

/// A way to have FSharp.SystemTextJson write the name of the tag of a union twice in one object.
type StoredTagTwice = {
    /// What gives a case a second property of the name of the tag; the name of a test result holds it.
    Reason : string
    /// The name of the tag.
    TagName : string
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

/// <summary>
/// Two values of one case of a union: one in which every field that can be without a value has one, and one in
/// which none of them has. A test sees in the two documents how a field is written without a value.
/// </summary>
[<Struct>]
type StoredCasePair = {
    /// The union type.
    UnionType : Type
    /// The value with its fields present.
    Present : objnull
    /// The value with its fields absent.
    Absent : objnull
}

/// The values of the unions.
module StoredUnionSamples =

    /// A pair of values of one case.
    let private pair (present : 'Union) (absent : 'Union) : StoredCasePair = {
        UnionType = typeof<'Union>
        Present = box present
        Absent = box absent
    }

    /// Every case that has a field that can be without a value, with the field present and absent.
    let pairs =
        seq {
            pair
                (Packed (Some "note", ValueSome 21, Include "mark", Nullable 22, "sender"))
                (Packed (None, ValueNone, Skip, Nullable (), null))
            pair
                (Loose (Some "note", ValueSome 23, Include "mark", Nullable 24, "sender"))
                (Loose (None, ValueNone, Skip, Nullable (), null))
            pair (WithNote (Some "note")) (WithNote None)
            pair (WithWeight (ValueSome 25)) (WithWeight ValueNone)
            pair (WithMark (Include "mark")) (WithMark Skip)
            pair (WithCount (Nullable 26)) (WithCount (Nullable ()))
            pair (WithSender "sender") (WithSender null)
            pair
                (Enclosed ({ Name = "author" }, Circle 27, Graded 28))
                (Enclosed (Unchecked.defaultof<StoredAuthor>, Unchecked.defaultof<StoredShape>, Ungraded))
            pair (StoredNote (Some "note")) (StoredNote None)
            pair (Sealed (Some "sealer", ValueSome 2026)) (Sealed (None, ValueNone))
        }
        |> ImmutableArray.CreateRange

    /// The values of the pairs that are of one union type.
    let private ofPairs<'Union> : 'Union seq =
        pairs
        |> Seq.filter (fun pair -> Type.(=) (pair.UnionType, typeof<'Union>))
        |> Seq.collect (fun pair -> seq {
            pair.Present
            pair.Absent
        })
        |> Seq.cast<'Union>

    /// A sample of a union type.
    let private sample (name : string) (values : 'Union seq) : StoredUnionSample = {
        Name = name
        UnionType = typeof<'Union>
        Values = values |> Seq.map box |> ImmutableArray.CreateRange
    }

    /// Every way to have the name of the tag written twice: for the fields, and for one field among the tag.
    let tagsTwice =
        seq {
            {
                Reason = "the fields have the name of the tag"
                TagName = "kind"
                Settings = JsonFSharpOptions.Default().WithUnionTagName("kind").WithUnionFieldsName "kind"
                Value = Rect (4, "rect")
            }
            {
                Reason = "the one field that stands in place of the fields has the name of the tag"
                TagName = "kind"
                Settings =
                    JsonFSharpOptions.Default().WithUnionUnwrapSingleFieldCases().WithUnionTagName("kind").WithUnionFieldsName
                        "kind"
                Value = Circle 3
            }
            {
                Reason = "a field has the name of the tag"
                TagName = "radius"
                Settings = JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields().WithUnionTagName "radius"
                Value = Circle 3
            }
            {
                Reason = "a member of the record of a case has the name of the tag"
                TagName = "Name"
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
        }
        |> ImmutableArray.CreateRange

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
                    Pair (7, 8)
                    Point
                })
            sample
                "a union without fields"
                (seq {
                    Low
                    High
                    Sure
                })
            sample "a union that wraps a value" (Seq.singleton (StoredTicket (Guid "0f8fad5b-d9cb-469f-a165-70867728950e")))
            sample "a union with one case of several fields" (Seq.singleton (StoredRange (1, 9)))
            sample "a union with one case without a field" (Seq.singleton StoredToken)
            sample
                "a union with a case that is null"
                (seq {
                    Ungraded
                    Graded 2
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
                "a union with fields that can be without a value"
                (seq {
                    yield! ofPairs<StoredParcel>
                    Listed (
                        StoredColour.DarkBlue,
                        struct (29, "pair"),
                        List.singleton "tag",
                        Map.ofSeq (Seq.singleton ("score", 30))
                    )
                    Unpacked
                })
            sample "a union that wraps an option" ofPairs<StoredNote>
            sample
                "a union with options of its own that skip an option"
                (seq {
                    yield! ofPairs<StoredSeal>
                    Unsealed
                })
        }
        |> ImmutableArray.CreateRange
