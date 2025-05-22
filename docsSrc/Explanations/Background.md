---
title: Background
category: Explanations
categoryindex: 3
index: 1
---

# Background

## Cosmos DB and F# Integration

Azure Cosmos DB is Microsoft's globally distributed, multi-model database service. While the .NET SDK provides excellent functionality, using it directly from F# can lead to code that doesn't feel idiomatic to the language.
Also `RequestOptions` pattern is not the best option is Cosmos DB operation features discoverability, where F# computation expressions shine and simplify operation definitions.

FSharp.Azure.Cosmos addresses this by providing F#-first abstractions over the Cosmos DB SDK, focusing on:

1. **Type-Safe Responses**: Using discriminated unions to handle response status codes as values instead of exceptions
2. **F# Computation Expressions**: Natural syntax for database operations like read, write, update, and delete
3. **Resource Management**: Better control over database resources through F# idioms

## Response Handling

Traditional .NET exception handling can be verbose and error-prone:
``` F#
try
    let! response = container.CreateItemAsync(item)
    // Handle success
with 
| :? CosmosException as ex when ex.StatusCode = HttpStatusCode.Conflict ->
    // Handle conflict
| :? CosmosException as ex when ex.StatusCode = HttpStatusCode.TooManyRequests ->
    // Handle rate limiting
```
FSharp.Azure.Cosmos transforms this into a more F#-like pattern:
``` F#
let! response = container.ExecuteAsync operation
match response with
| Created item -> // Handle success
| Conflict -> // Handle conflict
| TooManyRequests -> // Handle rate limiting
```
## Computation Expressions

The library provides computation expressions for all major operations:
``` F#
let createOperation = create {
    item person
    partitionKey person.TenantId
    consistencyLevel ConsistencyLevel.Eventual
    preTrigger "preTrigger"
    postTrigger "postTrigger"
}
```
This approach:
- Simplifies request options definition
- Allows to define an operation template and reuse it

## Query Extensions

Modern applications often need to handle large result sets efficiently. The library provides extensions for working with `FeedIterator` and `IQueryable` results using `TaskSeq`-based iteration
``` F#
let! people =
    container
        .GetItemLinqQueryable<Person>()
        .Where(fun p -> p.Name = name && p.TenantId = tenantId)
    |> CancellableTaskSeq.ofAsyncEnumerable cancellationToken
    |> TaskSeq.toListAsync
```
This approach ensures efficient query execution and resource management in F# while maintaining idiomatic practices.
