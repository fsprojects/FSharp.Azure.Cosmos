// The types that the tests of the JSON layout serialize to see how values that are no objects are written:
// enumerations, tuples, collections and maps. A test writes a value of each type with the real serializer and
// compares what the layout says about every member with what was written.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open System.Text.Json.Serialization

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

/// A converter that writes any value as the text that the value gives of itself, which a layout cannot look into.
type StoredAsTextConverter<'Value> () =
    inherit JsonConverter<'Value> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : 'Value =
        reader.Skip ()
        Unchecked.defaultof<'Value>

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : 'Value, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue (string (box value))

/// <summary>
/// A converter of a pair that is derived from the base class of the tuple converters of FSharp.SystemTextJson and
/// writes the pair as text all the same. The base class is public, so an application can have such a converter.
/// </summary>
type StoredPairAsTextConverter () =
    inherit BaseJsonTupleConverter<int * string> ()

    /// <inheritdoc />
    override _.ReadCore (reader : byref<Utf8JsonReader>, _ : JsonSerializerOptions) : int * string =
        reader.Skip ()
        (0, "")

    /// <inheritdoc />
    override _.WriteCore (writer : Utf8JsonWriter, value : int * string, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue (string (box value))

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : int * string, _ : JsonSerializerOptions) : unit =
        writer.WriteStringValue (string (box value))

/// A converter factory that writes a value of any type as text; a member names it to be written that way.
type StoredAsTextConverterFactory () =
    inherit JsonConverterFactory ()

    /// <inheritdoc />
    override _.CanConvert (_ : Type) : bool = true

    /// <inheritdoc />
    override _.CreateConverter (typeToConvert : Type, _ : JsonSerializerOptions) : JsonConverter | null =
        match Activator.CreateInstance (typedefof<StoredAsTextConverter<_>>.MakeGenericType typeToConvert) with
        | :? JsonConverter as converter -> converter
        | _ -> null

/// <summary>
/// A converter of the enumeration of colours that writes the first value as its name and every other value as its
/// number, so that the enumeration is written in no one way.
/// </summary>
type StoredHalfNamedColourConverter () =
    inherit JsonConverter<StoredColour> ()

    /// <inheritdoc />
    override _.Read (reader : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : StoredColour =
        match reader.TokenType with
        | JsonTokenType.String -> StoredColour.Red
        | _ -> enum<StoredColour>(reader.GetInt32 ())

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : StoredColour, _ : JsonSerializerOptions) : unit =
        match value with
        | StoredColour.Red -> writer.WriteStringValue (nameof StoredColour.Red)
        | other -> writer.WriteNumberValue (int other)

/// <summary>
/// A converter of the enumeration of colours that refuses every value but the first, as a converter does that was
/// written for the values of its day: with the exception that says that a value is not supported.
/// </summary>
type StoredFirstColourConverter () =
    inherit JsonConverter<StoredColour> ()

    /// <inheritdoc />
    override _.Read (_ : byref<Utf8JsonReader>, _ : Type, _ : JsonSerializerOptions) : StoredColour = StoredColour.Red

    /// <inheritdoc />
    override _.Write (writer : Utf8JsonWriter, value : StoredColour, _ : JsonSerializerOptions) : unit =
        match value with
        | StoredColour.Red -> writer.WriteStringValue (nameof StoredColour.Red)
        | other -> raise (NotSupportedException $"The converter does not write the colour %d{int other}.")

/// A class with a member for every kind of value that is no object.
type StoredCatalog (id : string) =

    /// A property that is only read.
    member _.Id = id

    /// An enumeration that is written as the options say.
    member val Colour = StoredColour.Red with get, set

    /// An enumeration with a converter of its own, which writes the name of the value.
    [<JsonConverter(typeof<JsonStringEnumConverter>)>]
    member val NamedColour = StoredColour.Red with get, set

    /// An enumeration with a converter of its own, which writes the number also when the options write names.
    [<JsonConverter(typeof<JsonNumberEnumConverter<StoredColour>>)>]
    member val NumberedColour = StoredColour.Red with get, set

    /// Flags.
    member val Access = StoredAccess.None with get, set

    /// A nullable enumeration.
    member val MaybeColour = Nullable<StoredColour>() with get, set

    /// A nullable enumeration with a converter of its own, which writes the value under the nullable.
    [<JsonConverter(typeof<JsonStringEnumConverter>)>]
    member val MaybeNamedColour = Nullable<StoredColour>() with get, set

    /// An option of an enumeration: the value under the option is written as any enumeration is.
    member val Preferred : StoredColour option = None with get, set

    /// A reference tuple.
    member val Pair : int * string = (0, "") with get, set

    /// A struct tuple, of which System.Text.Json writes the fields only when the options include fields.
    member val StructPair : struct (int * string) = struct (0, "") with get, set

    /// A tuple of eight items, which holds the eighth in a tuple of its own.
    member val Octet : int * int * int * int * int * int * int * int = (0, 0, 0, 0, 0, 0, 0, 0) with get, set

    /// A value option of a tuple: the value under it is written as any tuple is.
    member val Spare : (int * string) voption = ValueNone with get, set

    /// An array.
    member val Numbers : int array = [||] with get, set

    /// An F# list.
    member val Names : string list = [] with get, set

    /// An F# set.
    member val Marks : Set<int> = Set.empty with get, set

    /// An immutable array.
    member val Codes : ImmutableArray<int> = ImmutableArray<int>.Empty with get, set

    /// A list that can change.
    member val Items : ResizeArray<int> = ResizeArray<int>() with get, set

    /// A set that can change.
    member val Keys : HashSet<string> = HashSet<string>(StringComparer.Ordinal) with get, set

    /// A sequence.
    member val Sequence : int seq = Seq.empty with get, set

    /// An array of bytes, which is written as one text and not as an array.
    member val Bytes : byte array = [||] with get, set

    /// An F# map with texts as keys.
    member val Scores : Map<string, int> = Map.empty with get, set

    /// An F# map with numbers as keys.
    member val ById : Map<int, string> = Map.empty with get, set

    /// A dictionary.
    member val Lookup : Dictionary<string, int> = Dictionary<string, int>(StringComparer.Ordinal) with get, set

    /// A dictionary that is only read.
    member val Index : IReadOnlyDictionary<string, int> = Dictionary<string, int>(StringComparer.Ordinal) with get, set

/// A record with a field for every kind of value that is no object.
type StoredCatalogEntry = {
    /// An enumeration that is written as the options say.
    Colour : StoredColour
    /// An enumeration whose field names a converter. System.Text.Json follows it, and FSharp.SystemTextJson does not
    /// read it.
    [<JsonConverter(typeof<JsonStringEnumConverter>)>]
    NamedColour : StoredColour
    /// An enumeration that is never written.
    [<JsonIgnore>]
    Draft : StoredColour
    /// Flags.
    Access : StoredAccess
    /// A nullable enumeration.
    MaybeColour : Nullable<StoredColour>
    /// An option of an enumeration.
    Preferred : StoredColour option
    /// A reference tuple.
    Pair : int * string
    /// A struct tuple.
    StructPair : struct (int * string)
    /// A tuple of eight items.
    Octet : int * int * int * int * int * int * int * int
    /// A value option of a tuple.
    Spare : (int * string) voption
    /// An array.
    Numbers : int array
    /// An F# list.
    Names : string list
    /// An F# set.
    Marks : Set<int>
    /// An immutable array.
    Codes : ImmutableArray<int>
    /// An array of bytes.
    Bytes : byte array
    /// An F# map with texts as keys.
    Scores : Map<string, int>
    /// An F# map with numbers as keys.
    ById : Map<int, string>
    /// A dictionary.
    Lookup : Dictionary<string, int>
} with

    /// A property that is no field and says that it is written, with a converter of its own.
    [<JsonInclude; JsonConverter(typeof<JsonStringEnumConverter>)>]
    member this.IncludedColour = this.Colour

    /// A property that is no field: FSharp.SystemTextJson writes it only when its options include such properties.
    member this.OtherColour = this.Colour

/// A class whose members name a converter that writes them as text, whatever they are.
type StoredSealedCatalog () =

    /// An enumeration that the converter of the member writes as a text.
    [<JsonConverter(typeof<StoredAsTextConverterFactory>)>]
    member val Colour = StoredColour.Red with get, set

    /// An option of an enumeration that the converter of the member writes: it writes the option, and nothing shows
    /// what it writes for the enumeration under it.
    [<JsonConverter(typeof<StoredAsTextConverterFactory>)>]
    member val Chosen : StoredColour option = None with get, set

    /// A tuple that the converter of the member writes.
    [<JsonConverter(typeof<StoredAsTextConverterFactory>)>]
    member val Pair : int * string = (0, "") with get, set

    /// A list that the converter of the member writes.
    [<JsonConverter(typeof<StoredAsTextConverterFactory>)>]
    member val Names : string list = [] with get, set

    /// A map that the converter of the member writes.
    [<JsonConverter(typeof<StoredAsTextConverterFactory>)>]
    member val Scores : Map<string, int> = Map.empty with get, set

/// <summary>
/// A class whose members name a converter factory that hands them to the converter that the options have for their
/// types, so each is written as any value of its type is.
/// </summary>
type StoredForwardedCatalog () =

    /// An enumeration.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Colour = StoredColour.Red with get, set

    /// A tuple.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Pair : int * string = (0, "") with get, set

    /// An array.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Numbers : int array = [||] with get, set

    /// An F# map.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Scores : Map<string, int> = Map.empty with get, set

    /// A dictionary.
    [<JsonConverter(typeof<StoredForwardingConverterFactory>)>]
    member val Lookup : Dictionary<string, int> = Dictionary<string, int>(StringComparer.Ordinal) with get, set

/// <summary>
/// A class whose members are only read. With options that ignore read-only members the serializer writes none of them
/// but the collection and the dictionary.
/// </summary>
type StoredReadOnlyCatalog () =

    /// An enumeration.
    member _.Colour = StoredColour.DarkBlue

    /// A tuple.
    member _.Pair : int * string = (1, "one")

    /// An array.
    member _.Numbers : int array = [| 1; 2 |]

    /// A dictionary.
    member _.Lookup : IReadOnlyDictionary<string, int> =
        Dictionary<string, int>(Seq.singleton (KeyValuePair ("a", 1)), StringComparer.Ordinal)

/// <summary>
/// A class with a member for every kind of value that is no object, each with a converter factory that counts its
/// work. Only one test may ask about it.
/// </summary>
type StoredCountedCatalog () =

    /// An enumeration.
    [<JsonConverter(typeof<StoredCountingConverterFactory<StoredCountedCatalog>>)>]
    member val Colour = StoredColour.Red with get, set

    /// A tuple.
    [<JsonConverter(typeof<StoredCountingConverterFactory<StoredCountedCatalog>>)>]
    member val Pair : int * string = (0, "") with get, set

    /// An array.
    [<JsonConverter(typeof<StoredCountingConverterFactory<StoredCountedCatalog>>)>]
    member val Numbers : int array = [||] with get, set

    /// A dictionary.
    [<JsonConverter(typeof<StoredCountingConverterFactory<StoredCountedCatalog>>)>]
    member val Lookup : Dictionary<string, int> = Dictionary<string, int>(StringComparer.Ordinal) with get, set

/// A record that a test has a converter of the options write as text, so that nothing is known about its fields.
type StoredAlbum = {
    /// An enumeration.
    Cover : StoredColour
    /// A tuple.
    Size : int * int
    /// A list.
    Tracks : string list
    /// A map.
    Plays : Map<string, int>
}

/// A value of the model of values, as the tests write it.
[<Struct>]
type StoredValueSample = {
    /// What the type is; the name of a test result holds it.
    Name : string
    /// The value, with something in every member.
    Value : obj
}

/// The values of the model of values.
module StoredValueSamples =

    /// A dictionary with one key, made anew for every member: a dictionary can change, so no two members share one.
    let private lookup () : Dictionary<string, int> =
        Dictionary<string, int>(Seq.singleton (KeyValuePair ("a", 1)), StringComparer.Ordinal)

    /// The class.
    let catalog = {
        Name = "a class with values that are no objects"
        Value =
            StoredCatalog (
                "id-1",
                Colour = StoredColour.DarkBlue,
                NamedColour = StoredColour.DarkBlue,
                NumberedColour = StoredColour.DarkBlue,
                Access = (StoredAccess.Read ||| StoredAccess.Write),
                MaybeColour = Nullable StoredColour.Red,
                MaybeNamedColour = Nullable StoredColour.Red,
                Preferred = Some StoredColour.DarkBlue,
                Pair = (1, "one"),
                StructPair = struct (2, "two"),
                Octet = (1, 2, 3, 4, 5, 6, 7, 8),
                Spare = ValueSome (3, "three"),
                Numbers = [| 1; 2 |],
                Names = [ "a"; "b" ],
                Marks = Set.ofArray [| 1; 2 |],
                Codes = ImmutableArray.Create (1, 2),
                Items = ResizeArray<int> [| 1; 2 |],
                Keys = HashSet<string>([| "k" |], StringComparer.Ordinal),
                Sequence = [| 1; 2 |],
                Bytes = [| 1uy; 2uy |],
                Scores = Map.ofArray [| ("a", 1) |],
                ById = Map.ofArray [| (1, "a") |],
                Lookup = lookup (),
                Index = lookup ()
            )
    }

    /// The record.
    let entry = {
        Name = "a record with values that are no objects"
        Value =
            ({
                Colour = StoredColour.DarkBlue
                NamedColour = StoredColour.DarkBlue
                Draft = StoredColour.DarkBlue
                Access = StoredAccess.Read ||| StoredAccess.Write
                MaybeColour = Nullable StoredColour.Red
                Preferred = Some StoredColour.DarkBlue
                Pair = (1, "one")
                StructPair = struct (2, "two")
                Octet = (1, 2, 3, 4, 5, 6, 7, 8)
                Spare = ValueSome (3, "three")
                Numbers = [| 1; 2 |]
                Names = [ "a"; "b" ]
                Marks = Set.ofArray [| 1; 2 |]
                Codes = ImmutableArray.Create (1, 2)
                Bytes = [| 1uy; 2uy |]
                Scores = Map.ofArray [| ("a", 1) |]
                ById = Map.ofArray [| (1, "a") |]
                Lookup = lookup ()
            }
            : StoredCatalogEntry)
    }

    /// The class whose members name a converter that writes text.
    let sealedCatalog = {
        Name = "a class whose members name a converter of their own"
        Value =
            StoredSealedCatalog (
                Colour = StoredColour.DarkBlue,
                Chosen = Some StoredColour.DarkBlue,
                Pair = (1, "one"),
                Names = [ "a"; "b" ],
                Scores = Map.ofArray [| ("a", 1) |]
            )
    }

    /// The class whose members name a converter factory that hands them to the converter of the options.
    let forwardedCatalog = {
        Name = "a class whose members hand themselves to the converters of the options"
        Value =
            StoredForwardedCatalog (
                Colour = StoredColour.DarkBlue,
                Pair = (1, "one"),
                Numbers = [| 1; 2 |],
                Scores = Map.ofArray [| ("a", 1) |],
                Lookup = lookup ()
            )
    }

    /// The class whose members are only read.
    let readOnlyCatalog = {
        Name = "a class whose members are only read"
        Value = StoredReadOnlyCatalog ()
    }

    /// Every sample.
    let all =
        seq {
            catalog
            entry
            sealedCatalog
            forwardedCatalog
            readOnlyCatalog
        }
        |> ImmutableArray.CreateRange

    /// Values of every kind that are no members: what a collection or a tuple holds, with the type each is asked as.
    let alone : ImmutableArray<struct (string * objnull * Type)> =
        seq {
            struct ("an enumeration", box StoredColour.DarkBlue, typeof<StoredColour>)
            struct ("flags", box (StoredAccess.Read ||| StoredAccess.Write), typeof<StoredAccess>)
            struct ("a nullable enumeration", box StoredColour.Red, typeof<Nullable<StoredColour>>)
            struct ("a pair", box (1, "one"), typeof<int * string>)
            struct ("a struct pair", box (struct (2, "two")), typeof<struct (int * string)>)
            struct ("a tuple of eight", box (1, 2, 3, 4, 5, 6, 7, 8), typeof<int * int * int * int * int * int * int * int>)
            struct ("an array", box [| 1; 2 |], typeof<int array>)
            struct ("a list", box [ "a"; "b" ], typeof<string list>)
            struct ("a set", box (Set.ofArray [| 1; 2 |]), typeof<Set<int>>)
            struct ("an immutable array", box (ImmutableArray.Create (1, 2)), typeof<ImmutableArray<int>>)
            struct ("bytes", box [| 1uy; 2uy |], typeof<byte array>)
            struct ("a text", box "ab", typeof<string>)
            struct ("a map of texts", box (Map.ofArray [| ("a", 1) |]), typeof<Map<string, int>>)
            struct ("a map of numbers", box (Map.ofArray [| (1, "a") |]), typeof<Map<int, string>>)
            struct ("a dictionary", box (lookup ()), typeof<Dictionary<string, int>>)
        }
        |> ImmutableArray.CreateRange
