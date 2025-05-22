---
title: Getting Started
category: Tutorials
categoryindex: 1
index: 1
---

# Getting Started

## Installation

First, add the NuGet package to your project:
```
dotnet add package FSharp.Azure.Cosmos
```
## Basic Setup

Here's a minimal example to get started:
``` F#
open Microsoft.Azure.Cosmos
open FSharp.Azure.Cosmos
```

``` F#
// Create the client
let client = CosmosClient(connectionString = "your_connection_string")

// Get database and container
let database = client.GetDatabase("your_database")
let container = database.GetContainer("your_container")

// Define a simple record type
type Person = {
    TenantId : string
    Id: string
    Name: string
    Age: int
}

// Create an item using computation expression
let createPerson = task {
    let person = { TenantId = "Customer1"; Id = "1"; Name = "John"; Age = 30 }
    let operation = create {
        item person
        partitionKey person.TenantId
    }
    match! container.ExecuteAsync operation with
    | Created item -> printfn "Created: %A" item
    | Conflict ->  printfn "Item already exists"
    | _ -> ()
}

// Query items using TaskSeq
let queryPeople = task {
    let query = QueryDefinition "SELECT * FROM c WHERE c.age > 25"
    let! results = 
        container.GetItemQueryIterator<Person>(query)
        |> TaskSeq.ofFeedIterator
        |> TaskSeq.toArrayAsync
    
    printfn "Found people: %A" results
}
```
## Next Steps

- Check out the [How-To Guides](../How_Tos/Doing_A_Thing.html) for common scenarios
- Read the [Background](../Explanations/Background.html) for deeper understanding
- Browse the [API Reference](../reference/index.html) for detailed documentation
