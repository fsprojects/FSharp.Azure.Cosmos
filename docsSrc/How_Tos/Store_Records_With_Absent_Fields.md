---
title: How To store F# records with fields that can be absent
category: How To Guides
categoryindex: 2
index: 5
---

# How To store F# records with fields that can be absent

A document in Cosmos DB often has no value for a field. An old document has no field that a newer version of the
record added. A field without a value takes less space when it is not written. FSharp.SystemTextJson writes each
field of a record, also a field without a value, and it refuses a document that does not have a field of the record.

`AllowNullFSharpRecordConverterFactory` makes a converter for F# records that does these things:

- It does not write a field without a value: `null`, `None`, `ValueNone` or `Skip`.
- It reads a document that leaves out a field or has `null` for it.
- It names a field as FSharp.SystemTextJson does: the first name of `JsonName`, then the name of `JsonPropertyName`,
  then the name of the naming policy.
- It obeys each condition of `JsonIgnore`.

The converter is in the package `FSharp.Azure.Cosmos.Json`. `FSharp.Azure.Cosmos` depends on that package.

## Use the converter

Put the factory before the converter of FSharp.SystemTextJson. The serializer asks the converters in sequence, and
FSharp.SystemTextJson also writes records. FSharp.SystemTextJson still writes the unions, the options and the lists:

``` F#
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.Azure.Cosmos
open FSharp.Azure.Cosmos

let serializerOptions = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
serializerOptions.Converters.Add (AllowNullFSharpRecordConverterFactory (allowNullFields = true))
JsonFSharpOptions.Default().AddToJsonSerializerOptions serializerOptions

let client =
    new CosmosClient (
        endpoint,
        key,
        CosmosClientOptions (UseSystemTextJsonSerializerWithOptions = serializerOptions)
    )
```

This record is stored as the JSON below it. The converter does not write `Nickname` and `Deleted`:

``` F#
type Customer = {
    Id : string
    PartitionKey : string
    Name : string
    Nickname : string voption
    Deleted : System.DateTimeOffset option
}

let customer = {
    Id = "c1"
    PartitionKey = "customers"
    Name = "Ann"
    Nickname = ValueNone
    Deleted = None
}
```

``` JSON
{ "id": "c1", "partitionKey": "customers", "name": "Ann" }
```

## Choose the records that the converter writes

The factory takes each F# record. To take only some records, for example the records of your application, give the
factory a function:

``` F#
AllowNullFSharpRecordConverterFactory (
    true,
    fun recordType ->
        match recordType.Namespace with
        | null -> false
        | name -> name.StartsWith ("MyApplication.", System.StringComparison.Ordinal)
)
```

## Choose what a document can leave out

With `allowNullFields = true`, a document can leave out each field or have `null` for it:

- A field that the document leaves out gets its value without a value: `null`, `None`, `ValueNone`, `Skip`, or the
  default value of a value type, for example `0`.
- For a field that is `null`, the serializer decides when it can read `null` as a value of the type. For example, a
  converter can read `null` as an empty list. When the serializer refuses `null` for the type, as FSharp.SystemTextJson
  does for an F# list, the field gets its value without a value.

With `allowNullFields = false`, a document can leave out only an option, a `ValueOption`, a `Skippable` or a
`Nullable`, and it can have `null` only for such a field. The converter refuses a document without another field.

Also an option can be a type for which the serializer refuses `null`. FSharp.SystemTextJson refuses `null` for a
`ValueOption` when its options do not have `UnwrapOption`, for example with `JsonUnionEncoding.AdjacentTag`. Such a
field gets `ValueNone`.

## Control a field with JsonIgnore

| Condition | Write | Read |
|---|---|---|
| `Always`, the default | no | no: the field gets its value without a value |
| `Never` | always, also `null` | yes |
| `WhenWritingNull` | not without a value, as without the attribute | yes |
| `WhenWritingDefault` | not without a value and not the default value, for example `0` | yes |
| `WhenWriting` | no | yes |
| `WhenReading` | not without a value | no |

FSharp.SystemTextJson 1.4.36 throws `NullReferenceException` when it reads a record that has a field of a value type
with `JsonIgnore` ([Tarmil/FSharp.SystemTextJson#217](https://github.com/Tarmil/FSharp.SystemTextJson/issues/217)).
This converter gives such a field its default value.

Two fields that the converter writes cannot have one name, and two fields that it reads cannot have one name. The
converter throws `InvalidOperationException` for such a record. A field with `Always` keeps no name, so a new field
can replace an old field under the same name:

``` F#
type Product = {
    Id : string
    // The old field: the converter does not read it and does not write it
    [<JsonIgnore>]
    [<JsonPropertyName "price">]
    PriceInCents : int
    [<JsonPropertyName "price">]
    Price : decimal
}
```

## Read the documents that you wrote before

The converter reads a field under each name of its attributes and also under the name of the naming policy. Thus a
document stays readable after you add `JsonPropertyName` to a field. The converter writes the field under the new name
when it writes the document again.

The converter skips the properties of a document that are no field of the record, for example `_rid`, `_self` and
`_ts` of Cosmos DB. To refuse such a property, set `UnmappedMemberHandling` of the options to `Disallow`.
