---
title: How To store a duration
category: How To Guides
categoryindex: 2
index: 4
---

# How To store a duration so that a query sorts it

Cosmos DB has no type for a duration. System.Text.Json writes a `TimeSpan` as text, for example `"2.00:00:00"`.
Cosmos DB compares text by character code. Thus `"2.00:00:00"` sorts after `"10.00:00:00"`, and `"23:00:00"` sorts
after both. A query with `ORDER BY` or with `>` on such a field gives the wrong result.

`TimeSpanTicksConverter` writes the number of ticks instead. One tick is 100 nanoseconds. Cosmos DB sorts and
compares a number in time order.

## Use the converter

Add the converter to the options of System.Text.Json. Then give the options to the client. The camel case naming
policy writes the field `Id` as `id`, which Cosmos DB requires:

``` F#
open System.Text.Json
open Microsoft.Azure.Cosmos
open FSharp.Azure.Cosmos

let serializerOptions = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
serializerOptions.Converters.Add (TimeSpanTicksConverter ())

let client =
    new CosmosClient (
        endpoint,
        key,
        CosmosClientOptions (UseSystemTextJsonSerializerWithOptions = serializerOptions)
    )
```

Each `TimeSpan` that the client writes is now a number. This record is stored as the JSON below it:

``` F#
type Job = { Id : string; PartitionKey : string; Duration : System.TimeSpan }

let job = { Id = "job-1"; PartitionKey = "jobs"; Duration = System.TimeSpan.FromDays 2.0 }
```

``` JSON
{ "id": "job-1", "partitionKey": "jobs", "duration": 1728000000000 }
```

The client writes a parameter with the same converter. Thus a query can compare the field with a `TimeSpan`:

``` F#
let longJobsAsync (container : Container) (minimum : System.TimeSpan) = task {
    let query =
        QueryDefinition("SELECT VALUE c FROM c WHERE c.duration >= @minimum ORDER BY c.duration")
            .WithParameter ("@minimum", minimum)

    use iterator = container.GetItemQueryIterator<Job> query
    let jobs = ResizeArray<Job> ()

    while iterator.HasMoreResults do
        let! page = iterator.ReadNextAsync ()
        jobs.AddRange page

    return jobs.ToArray ()
}
```

The converter also handles `Nullable<TimeSpan>` and a `TimeSpan` as the key of a dictionary.

## Read the documents that you wrote before

The converter reads the text that System.Text.Json writes, for example `"1.02:00:00"`. Thus a container with old
documents stays readable. An old document keeps its text until you write it again. Cosmos DB sorts a number before a
text. Thus a query with `ORDER BY` returns the new documents in time order, then the old documents in the order of
their text. With `DESC` the two groups change places.

To move the old documents, read each one and write it again with a client that uses the converter.

## Know the limits

- The converter works with System.Text.Json only. Set it in `UseSystemTextJsonSerializerWithOptions`.
- Cosmos DB defines a JSON number as a 64-bit floating-point number. Such a number holds each whole number up to 2^53
  exactly, which is about 28 years of ticks. A longer `TimeSpan` can lose digits in a client that reads the number as
  a floating-point number.
- The number has no unit in the document. A query written by hand must use ticks. Write
  `c.duration >= 36000000000` for one hour, or use a `TimeSpan` parameter as above.
