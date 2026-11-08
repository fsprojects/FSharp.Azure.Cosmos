namespace FSharp.Azure.Cosmos.Tests

open System
open System.IO
open System.Reflection
open System.Reflection.Emit
open System.Runtime.CompilerServices
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Reflection

open FSharp.Azure.Cosmos

// The records are in a module of their own. Thus, the record type inference of the other test files does not see their
// labels. A record in a module is a nested type, so the factory must take more than the public types of a namespace.
module RecordConverterTestData =

    type Address = { Street : string; City : string }

    type Customer = {
        Id : string
        Name : string
        Nickname : string option
        Middle : string voption
        Age : int
        Score : decimal voption
        Rank : Nullable<int>
        Address : Address
        Tags : string list
        Draft : Skippable<string>
    }

    /// A record of the Ecierge application: its converter wrote the two fields under the names of the naming policy.
    type ModelCleanupContext = {
        PropertyId : string
        [<JsonPropertyName "id">]
        ModelId : string
        OldManualId : Guid voption
        [<JsonPropertyName "_etag">]
        ETag : string
    }

    type Named = {
        Id : string
        [<JsonName("first", "second")>]
        WithJsonName : int
        [<JsonPropertyName "property">]
        WithPropertyName : int
        [<JsonName "both"; JsonPropertyName "other">]
        WithBoth : int
        // A name with a field is the name of a field of a union case, not of this field
        [<JsonName("forUnionField", Field = "Case")>]
        WithFieldName : int
    }

    type Ignored = {
        Id : string
        [<JsonIgnore>]
        Count : int
        [<JsonIgnore>]
        Note : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.Never)>]
        Always : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)>]
        NotNull : string voption
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)>]
        NotDefault : int
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)>]
        ReadOnly : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)>]
        WriteOnly : string
    }

    type SameName = {
        [<JsonPropertyName "name">]
        First : string
        [<JsonPropertyName "name">]
        Second : string
    }

    /// The converter writes the two fields, but reads only the second field.
    type WrittenTwice = {
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)>]
        [<JsonPropertyName "name">]
        First : string
        [<JsonPropertyName "name">]
        Second : string
    }

    /// The converter reads the two fields, but writes neither field.
    type ReadTwice = {
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)>]
        [<JsonPropertyName "name">]
        First : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)>]
        [<JsonPropertyName "name">]
        Second : string
    }

    /// A new field that replaces an old field under the same name.
    type Replaced = {
        Id : string
        [<JsonIgnore>]
        [<JsonPropertyName "price">]
        PriceInCents : int
        [<JsonPropertyName "price">]
        Price : decimal
    }

    /// A record with a generic field. A test gives the field a struct type that the test makes at run time.
    type Holder<'Value> = {
        Name : string
        [<JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)>]
        Value : 'Value
    }

    /// The naming policy gives Name the name of Title. The declared name wins.
    type PolicyAndDeclared = {
        [<JsonPropertyName "x">]
        Name : string
        [<JsonPropertyName "name">]
        Title : string
    }

    [<Struct>]
    type Point = { X : int; Y : int }

    type PrivateRecord = private {
        Value : string
    } with

        static member Create (value : string) = { Value = value }

        member this.Text = this.Value

    type Unwritable = { Id : string; Kind : Type }

    /// Reads null as an empty list, as the converters of the Ecierge application do.
    type EmptyListForNullConverter<'Item> () =
        inherit JsonConverter<'Item list> ()

        /// <inheritdoc />
        override _.HandleNull = true

        /// <inheritdoc />
        override _.Read (reader, _, options) =
            match reader.TokenType with
            | JsonTokenType.Null -> []
            | _ ->
                JsonSerializer.Deserialize<'Item array>(&reader, options)
                |> nonNull
                |> Array.toList

        /// <inheritdoc />
        override _.Write (writer, value, options) = JsonSerializer.Serialize (writer, List.toArray value, options)

open RecordConverterTestData

// Explicit type arguments need a module binding, not a binding of a class
[<AutoOpen>]
module private RecordConverterReading =

    // The overload with a Type, because the generic overload warns for a struct record (FS3265)
    let read<'Value> (options : JsonSerializerOptions) (json : string) : 'Value =
        JsonSerializer.Deserialize (json, typeof<'Value>, options)
        |> nonNull
        |> unbox<'Value>

/// <summary>
/// Checks how <see cref="T:FSharp.Azure.Cosmos.AllowNullFSharpRecordConverter`1"/> names, writes, leaves out and reads
/// the fields of a record, and the cases that the converter of the Ecierge application handled wrongly.
/// </summary>
[<TestClass; UnitTestCategory; JsonConverterTestCategory; RecordConverterTestCategory>]
type AllowNullFSharpRecordConverterTests () =

    // The set-up of a Cosmos DB document: camel case names. The factory comes before FSharp.SystemTextJson, which also
    // writes records.
    static let optionsFor
        (fsharpOptions : JsonFSharpOptions)
        (allowNullFields : bool)
        (configure : JsonSerializerOptions -> unit)
        =
        let options = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        options.Converters.Add (AllowNullFSharpRecordConverterFactory allowNullFields)
        configure options
        fsharpOptions.AddToJsonSerializerOptions options
        options

    static let optionsWith = optionsFor (JsonFSharpOptions.Default ())

    static let allowing = optionsWith true ignore
    static let strict = optionsWith false ignore

    static let disallowing =
        optionsWith true (fun options -> options.UnmappedMemberHandling <- JsonUnmappedMemberHandling.Disallow)

    // A struct with a constructor without parameters that sets its field Count to 1, as C# 10 can declare. F# cannot
    // declare such a constructor, so the test makes the type at run time.
    static let counterType =
        lazy
            let counter =
                AssemblyBuilder
                    .DefineDynamicAssembly(AssemblyName "RecordConverterTests.Counter", AssemblyBuilderAccess.Run)
                    .DefineDynamicModule("RecordConverterTests.Counter")
                    .DefineType(
                        "Counter",
                        TypeAttributes.Public
                        ||| TypeAttributes.Sealed
                        ||| TypeAttributes.SequentialLayout,
                        typeof<ValueType>
                    )

            let count = counter.DefineField ("Count", typeof<int>, FieldAttributes.Public)

            let il =
                counter.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator()

            il.Emit OpCodes.Ldarg_0
            il.Emit OpCodes.Ldc_I4_1
            il.Emit (OpCodes.Stfld, count)
            il.Emit OpCodes.Ret
            counter.CreateType ()

    static let propertyNames (json : string) =
        use document = JsonDocument.Parse json
        document.RootElement.EnumerateObject ()
        |> Seq.map _.Name
        |> Seq.toArray

    static let customer : Customer = {
        Id = "c1"
        Name = "Ann"
        Nickname = Some "A"
        Middle = ValueSome "M"
        Age = 30
        Score = ValueSome 4.5m
        Rank = Nullable 3
        Address = { Street = "Main"; City = "Kyiv" }
        Tags = [ "a"; "b" ]
        Draft = Include "d"
    }

    [<TestMethod>]
    member _.``A record that has every value makes the round trip`` () =
        let json = JsonSerializer.Serialize (customer, allowing)

        CollectionAssert.AreEqual (
            [| "id"; "name"; "nickname"; "middle"; "age"; "score"; "rank"; "address"; "tags"; "draft" |],
            propertyNames json,
            "The converter should write each field with a value, under the name of the naming policy."
        )

        Assert.AreEqual (customer, read<Customer> allowing json, "The converter should read the record that it wrote.")

    [<TestMethod>]
    member _.``Write names a field by JsonName, then JsonPropertyName, then the naming policy`` () =
        let named : Named = {
            Id = "n1"
            WithJsonName = 1
            WithPropertyName = 2
            WithBoth = 3
            WithFieldName = 4
        }

        CollectionAssert.AreEqual (
            [| "id"; "first"; "property"; "both"; "withFieldName" |],
            propertyNames (JsonSerializer.Serialize (named, allowing)),
            "The converter should write the first name of JsonName, then the name of JsonPropertyName, then the name of the naming policy."
        )

    [<TestMethod>]
    member _.``Write names the id of a record of Ecierge id, which the converter of Ecierge wrote as modelId`` () =
        let context : ModelCleanupContext = { PropertyId = "p1"; ModelId = "m1"; OldManualId = ValueNone; ETag = "e1" }

        CollectionAssert.AreEqual (
            [| "propertyId"; "id"; "_etag" |],
            propertyNames (JsonSerializer.Serialize (context, allowing)),
            "The converter should write a field with JsonPropertyName under that name, because Cosmos DB requires the name id."
        )

    [<TestMethod>]
    member _.``Read accepts the declared names, the other names of JsonName, and the name of the naming policy`` () =
        let named =
            read<Named> allowing """{"id":"n1","second":1,"withPropertyName":2,"other":3,"withFieldName":4}"""

        Assert.AreEqual (
            ({
                Id = "n1"
                WithJsonName = 1
                WithPropertyName = 2
                WithBoth = 3
                WithFieldName = 4
            }
            : Named),
            named,
            "The converter should read a field under each of its names, so that an old document stays readable."
        )

    [<TestMethod>]
    member _.``A declared name wins over the name that the naming policy gives another field`` () =
        Assert.AreEqual (
            ({ Name = "n"; Title = "t" } : PolicyAndDeclared),
            read<PolicyAndDeclared> allowing """{"x":"n","name":"t"}""",
            "The converter should read the name 'name' as Title, which declares it, and not as Name."
        )

    [<TestMethod>]
    [<DataRow(typeof<SameName>, DisplayName = "read and written")>]
    [<DataRow(typeof<WrittenTwice>, DisplayName = "written")>]
    [<DataRow(typeof<ReadTwice>, DisplayName = "read")>]
    member _.``Two fields with one name are refused when the converter reads both or writes both`` (recordType : Type) =
        let refusal =
            Assert.ThrowsExactly<InvalidOperationException>(
                Action (fun () ->
                    AllowNullFSharpRecordConverterFactory(true).CreateConverter(recordType, allowing)
                    |> ignore
                ),
                "The converter should refuse two fields of one name: a document cannot tell them apart, or it would have the name two times."
            )

        Assert.Contains ("'name'", refusal.Message, StringComparison.Ordinal, "The refusal should give the name.")

    [<TestMethod>]
    member _.``A field can take the name of a field that the converter ignores`` () =
        let product : Replaced = { Id = "p1"; PriceInCents = 1250; Price = 12.5m }
        let json = JsonSerializer.Serialize (product, allowing)

        Assert.AreEqual (
            """{"id":"p1","price":12.5}""",
            json,
            "The converter should write the new field under the name, and not the ignored field."
        )

        Assert.AreEqual (
            { product with PriceInCents = 0 },
            read<Replaced> allowing json,
            "The converter should read the name into the new field, and give the ignored field its default value."
        )

    [<TestMethod>]
    member _.``Write leaves out null, None, ValueNone, Skip and a Nullable without a value`` () =
        let json =
            JsonSerializer.Serialize (
                {
                    customer with
                        Name = Unchecked.defaultof<string>
                        Nickname = None
                        Middle = ValueNone
                        Score = ValueNone
                        Rank = Nullable ()
                        Draft = Skip
                },
                allowing
            )

        CollectionAssert.AreEqual (
            [| "id"; "age"; "address"; "tags" |],
            propertyNames json,
            "The converter should leave out each field without a value; the converter of Ecierge wrote ValueNone as null."
        )

    [<TestMethod>]
    member _.``Write obeys the condition of JsonIgnore`` () =
        let ignored : Ignored = {
            Id = "i1"
            Count = 5
            Note = "n"
            Always = Unchecked.defaultof<string>
            NotNull = ValueNone
            NotDefault = 0
            ReadOnly = "r"
            WriteOnly = "w"
        }

        let json = JsonSerializer.Serialize (ignored, allowing)

        CollectionAssert.AreEqual (
            [| "id"; "always"; "writeOnly" |],
            propertyNames json,
            "Always and WhenWriting should leave the field out, Never should write null, WhenWritingNull should leave out ValueNone, and WhenWritingDefault should leave out 0."
        )

        StringAssert.Contains (
            json,
            "\"always\":null",
            StringComparison.Ordinal,
            "Never should write a field without a value as null."
        )

        Assert.Contains (
            "notDefault",
            propertyNames (JsonSerializer.Serialize ({ ignored with NotDefault = 7 }, allowing)),
            "WhenWritingDefault should write a value that is not the default."
        )

    [<TestMethod>]
    member _.``Read obeys the condition of JsonIgnore, and an ignored field of a value type gets its default value`` () =
        // The ignored note holds an object with an id: the converter must skip the whole value of an ignored field. The
        // property of an ignored field is not an unmapped property, also when the options disallow unmapped properties.
        let json =
            """{"id":"i1","count":5,"note":{"id":"not the id"},"always":"a","notNull":"x","notDefault":7,"readOnly":"r","writeOnly":"w"}"""

        for options in [| allowing; strict; disallowing |] do
            let ignored = read<Ignored> options json

            Assert.AreEqual (
                ({
                    Id = "i1"
                    Count = 0
                    Note = Unchecked.defaultof<string>
                    Always = "a"
                    NotNull = ValueSome "x"
                    NotDefault = 7
                    ReadOnly = "r"
                    WriteOnly = Unchecked.defaultof<string>
                }
                : Ignored),
                ignored,
                "Always and WhenReading should not read the field, and an ignored int should be 0: FSharp.SystemTextJson 1.4.36 throws NullReferenceException there."
            )

    [<TestMethod>]
    [<DataRow("""{"id":"c1"}""", DisplayName = "the fields left out")>]
    [<DataRow("""{"id":"c1","name":null,"nickname":null,"middle":null,"age":null,"score":null,"rank":null,"address":null,"tags":null,"draft":null}""",
              DisplayName = "the fields null")>]
    member _.``A field without a value gets the value without a value when null fields are allowed`` (json : string) =
        let read = read<Customer> allowing json

        // One field at a time: the equality of F# records throws for a null list or record
        CollectionAssert.AreEqual (
            [|
                "name: null"
                "nickname: None"
                "middle: ValueNone"
                "age: 0"
                "score: ValueNone"
                "rank: no value"
                "address: null"
                "tags: null"
                "draft: Skip"
            |],
            [|
                $"""name: {if Object.ReferenceEquals (read.Name, null) then
                               "null"
                           else
                               read.Name}"""
                $"nickname: %A{read.Nickname}"
                $"middle: %A{read.Middle}"
                $"age: {read.Age}"
                $"score: %A{read.Score}"
                $"""rank: {if read.Rank.HasValue then
                               string read.Rank.Value
                           else
                               "no value"}"""
                $"""address: {if Object.ReferenceEquals (read.Address, null) then
                                  "null"
                              else
                                  "a value"}"""
                $"""tags: {if Object.ReferenceEquals (read.Tags, null) then
                               "null"
                           else
                               "a value"}"""
                $"draft: %A{read.Draft}"
            |],
            "A field without a value should get null, None, ValueNone, Skip, a Nullable without a value or 0; FSharp.SystemTextJson refuses null for an F# list, so the list is null; the converter of Ecierge threw for a record field that was null."
        )

    [<TestMethod>]
    [<DataRow(true, DisplayName = "null fields allowed")>]
    [<DataRow(false, DisplayName = "null fields not allowed")>]
    member _.``An option that the serializer cannot read from null gets the value without a value`` (allowNullFields : bool) =
        // Without UnwrapOption, FSharp.SystemTextJson writes a voption as a union case and refuses null for it
        let options =
            optionsFor (JsonFSharpOptions.Default().WithUnionEncoding JsonUnionEncoding.AdjacentTag) allowNullFields ignore

        Assert.AreEqual (
            ({ PropertyId = "p1"; ModelId = "m1"; OldManualId = ValueNone; ETag = "e1" } : ModelCleanupContext),
            read<ModelCleanupContext> options """{"propertyId":"p1","id":"m1","oldManualId":null,"_etag":"e1"}""",
            "A voption that is null should be ValueNone, also when the serializer refuses null for the voption."
        )

    [<TestMethod>]
    member _.``A field of a struct gets the default value of the struct, not the value of its constructor`` () =
        let counterType = counterType.Value
        let holderType = typedefof<Holder<_>>.MakeGenericType counterType
        let count = nonNull (counterType.GetField "Count")
        let value = nonNull (holderType.GetProperty "Value")
        let countIn (holder : objnull) = count.GetValue (value.GetValue holder)

        Assert.AreEqual (
            box 1,
            count.GetValue (Activator.CreateInstance counterType),
            "The constructor of the struct should set Count to 1."
        )

        Assert.AreEqual (
            box 0,
            countIn (JsonSerializer.Deserialize ("""{"name":"h"}""", holderType, allowing)),
            "A field that the document leaves out should get the default value of the struct, without a call of its constructor."
        )

        let holder =
            FSharpValue.MakeRecord (holderType, [| box "h"; RuntimeHelpers.GetUninitializedObject counterType |])

        CollectionAssert.AreEqual (
            [| "name" |],
            propertyNames (JsonSerializer.Serialize (holder, holderType, allowing)),
            "WhenWritingDefault should leave out the default value of the struct, not the value of its constructor."
        )

    [<TestMethod>]
    member _.``A field that is null gets the value that a converter reads from null`` () =
        // A converter like the ones of Ecierge that read null as an empty list
        let options =
            optionsWith true (fun options -> options.Converters.Add (EmptyListForNullConverter<string>()))

        Assert.AreEqual<string list>(
            [],
            (read<Customer> options """{"id":"c1","tags":null}""").Tags,
            "A field that is null should get the value that the converter of its type reads from null."
        )

    [<TestMethod>]
    member _.``A document must have each field that is not an option when null fields are not allowed`` () =
        let refusal =
            Assert.ThrowsExactly<JsonException>(
                Action (fun () ->
                    read<Customer> strict """{"id":"c1","age":1,"address":{"street":"s","city":"c"},"tags":[]}"""
                    |> ignore
                ),
                "The converter should refuse a document without a field that is not an option."
            )

        Assert.Contains ("has no field name", refusal.Message, StringComparison.Ordinal, "The refusal should name the field.")

        Assert.AreEqual (
            None,
            (read<Customer>
                strict
                """{"id":"c1","name":"n","nickname":null,"age":1,"address":{"street":"s","city":"c"},"tags":[]}""")
                .Nickname,
            "Null should be None for an option field; the converter of Ecierge refused it when null fields were not allowed."
        )

    [<TestMethod>]
    member _.``A document cannot have null for a field that is not an option when null fields are not allowed`` () =
        let refusal =
            Assert.ThrowsExactly<JsonException>(
                Action (fun () ->
                    read<Customer> strict """{"id":"c1","name":null,"age":1,"address":{"street":"s","city":"c"},"tags":[]}"""
                    |> ignore
                ),
                "The converter should refuse null for a field that is not an option."
            )

        Assert.Contains (
            "The field Name of the record Customer",
            refusal.Message,
            StringComparison.Ordinal,
            "The refusal should name the field and the record."
        )

    [<TestMethod>]
    member _.``Read skips the properties that are no field, unless the options disallow them`` () =
        let json = """{"id":"a1","_rid":"x","_ts":1,"street":"s","_attachments":{"a":[1,2]},"city":"c"}"""

        Assert.AreEqual (
            { Street = "s"; City = "c" },
            read<Address> allowing json,
            "The converter should skip the system properties of Cosmos DB."
        )

        Assert.ThrowsExactly<JsonException>(
            Action (fun () -> read<Address> disallowing json |> ignore),
            "The converter should refuse a property that is no field when the options disallow it."
        )
        |> ignore

    [<TestMethod>]
    member _.``Read skips the properties that are no field when the serializer reads a stream in parts`` () =
        // The serializer gives the converter a whole record, but not on the final block of the reader: there,
        // Utf8JsonReader.Skip throws, which the converter of Ecierge called
        let options = optionsWith true (fun options -> options.DefaultBufferSize <- 1)
        let json =
            """[{"street":"s","_attachments":{"a":[1,2,3]},"city":"c"},{"street":"t","_rid":"x","city":"d"}]"""
        use stream = new MemoryStream (Encoding.UTF8.GetBytes json)

        let addresses =
            JsonSerializer.Deserialize<Address array>(stream, options)
            |> nonNull

        CollectionAssert.AreEqual (
            [| { Street = "s"; City = "c" }; { Street = "t"; City = "d" } |],
            addresses,
            "The converter should skip a property that is no field also in the middle of a stream."
        )

    [<TestMethod>]
    member _.``Read compares the names without case when the options say so`` () =
        let options = optionsWith true (fun options -> options.PropertyNameCaseInsensitive <- true)

        Assert.AreEqual (
            { Street = "s"; City = "c" },
            read<Address> options """{"STREET":"s","City":"c"}""",
            "The converter should read a name in other case when the options compare names without case."
        )

    [<TestMethod>]
    member _.``A struct record and a record with a private representation make the round trip`` () =
        let point = { X = 1; Y = 2 }
        Assert.AreEqual (
            point,
            read<Point> allowing (JsonSerializer.Serialize (point, allowing)),
            "A struct record should make the round trip."
        )

        let hidden = PrivateRecord.Create "v"

        Assert.AreEqual (
            "v",
            (read<PrivateRecord> allowing (JsonSerializer.Serialize (hidden, allowing))).Text,
            "A record with a private representation should make the round trip."
        )

    [<TestMethod>]
    member _.``Write does not hide an error of the serializer`` () =
        Assert.ThrowsExactly<NotSupportedException>(
            Action (fun () ->
                JsonSerializer.Serialize (({ Id = "u1"; Kind = typeof<int> } : Unwritable), allowing)
                |> ignore
            ),
            "The converter should not write null for a value that the serializer cannot write, as the converter of Ecierge did."
        )
        |> ignore

    [<TestMethod>]
    member _.``The factory makes converters only for the record types that it accepts`` () =
        let factory =
            AllowNullFSharpRecordConverterFactory (
                true,
                Func<Type, bool>(fun recordType -> Type.(=) (recordType, typeof<Customer>))
            )

        Assert.IsTrue (factory.CanConvert typeof<Customer>, "The factory should take a record that the function accepts.")
        Assert.IsFalse (factory.CanConvert typeof<Address>, "The factory should leave a record that the function refuses.")

        Assert.IsFalse (
            AllowNullFSharpRecordConverterFactory(true).CanConvert typeof<string>,
            "The factory should take only F# records."
        )

        Assert.IsTrue (
            AllowNullFSharpRecordConverterFactory(true).CanConvert typeof<Point>,
            "The factory should take a record of a module, which is a nested type, and a struct record."
        )

    [<TestMethod>]
    member _.``FieldNames gives the name of each field that the converter writes`` () =
        let fieldNamesOf (recordType : Type) =
            // A type test from a class to an interface that the class does not implement needs an object
            match box (AllowNullFSharpRecordConverterFactory(true).CreateConverter(recordType, allowing)) with
            | :? IRecordFieldNames as described ->
                described.FieldNames
                |> Seq.map (fun pair -> $"{pair.Key}: {pair.Value}")
                |> Seq.toArray
            | _ -> raise (AssertFailedException $"The converter of {recordType.Name} should implement IRecordFieldNames.")

        CollectionAssert.AreEquivalent (
            [|
                "Id: id"
                "Always: always"
                "NotNull: notNull"
                "NotDefault: notDefault"
                "WriteOnly: writeOnly"
            |],
            fieldNamesOf typeof<Ignored>,
            "FieldNames should have each field that the converter can write, and no field that JsonIgnore keeps from the document."
        )

        CollectionAssert.AreEquivalent (
            [|
                "Id: id"
                "WithJsonName: first"
                "WithPropertyName: property"
                "WithBoth: both"
                "WithFieldName: withFieldName"
            |],
            fieldNamesOf typeof<Named>,
            "FieldNames should give the name that the converter writes a field under."
        )
