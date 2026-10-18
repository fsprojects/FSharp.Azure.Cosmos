---
name: test-data-seeding
description: >-
  Write a scenario for the emulator tests of this repository: a [<Struct>] TestData record and a TestApplicationFactory
  that creates the data in its constructor and writes the data in SeedDataAsync, and a test class that reads the data
  with a record pattern.
  Use this skill to write a test scenario, to add data to a scenario, or to make tests read seeded data.
  Triggers: "seed test data", "create a test scenario", "TestData", "TestApplicationFactory", "scenario",
  "integration test setup", "the same data in each test".
argument-hint: "Name of the scenario, the documents to write, the container"
---

# Test data seeding

## Purpose

A scenario writes the data that all tests of a class read. A scenario has two parts:

1. A `[<Struct>]` record with the name `TestData`. It holds the values that the tests read.
2. A class with the name `TestApplicationFactory`. It creates all data in its constructor and writes the data in `SeedDataAsync`.

The tests read the data from `TestData`. They do not write the data. Thus each test starts with the same known data, and the body of a test shows only what the test examines.

## When to use

* All tests of an emulator test class read the same documents.
* The tests of a class read documents that a scenario writes already. Use that scenario, or add the documents to it.

Do not use a scenario for a test that examines a write (create, replace, upsert, patch, or delete). This type of test writes its own data, for example with `OperationTestBase.SeedItemsAsync`.

## Operating mode

* Write the scenario directly. Do not ask for a confirmation.
* If a scenario writes the necessary documents already, use it. If it writes almost all of them, add the documents to it. Do not write a second scenario with the same data.
* Create all values in the constructor of the factory. `SeedDataAsync` only writes the values that the constructor created.
* Give fixed values to the documents. If the test project has Hedgehog generators, you can sample them with a fixed seed in the constructor.

## File and namespace

The file of a scenario is `tests/<project>/Scenarios/<Name>Scenario.fs`.

* Declare the namespace `FSharp.Azure.Cosmos.Tests.Integration.<Name>Scenario`.
* Give the types the names `TestData` and `TestApplicationFactory`. The namespaces keep the types of the scenarios separate.
* Add the file to the `.fsproj` file before the files of the tests that use the scenario:

  ```xml
  <Compile Include="Scenarios\ThreeItemsScenario.fs" />
  <Compile Include="ThreeItemsTests.fs" />
  ```

The scenarios of `tests/Cosmos.Tests/Scenarios/` are examples: `SingleItemScenario.fs` and `MultipleItemsScenario.fs`.

## TestData record

* Make the record a `[<Struct>]` record.
* Put into it only the values that the tests read. If no test reads a value of a document, keep the document in a private `let` binding of the factory.
* Do not put a value into `TestData` if the scenario does not write it. A test can use a value that is not in the database, for example the identifier of a missing item. Give this value a name that tells this fact: `MissingId`.
* Write a `///` comment for each field.

## TestApplicationFactory

```fsharp
namespace FSharp.Azure.Cosmos.Tests.Integration.ThreeItemsScenario

open System.Collections.Immutable
open System.Threading
open System.Threading.Tasks

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration

/// The values that the tests of this scenario read.
[<Struct>]
type TestData = {
    /// The partition of the items that the scenario writes.
    Partition : string
    /// The items that the scenario writes, in the sequence of their creation.
    Items : ImmutableArray<TestItem>
}

/// <summary>
/// Writes three items into one partition of the container of <see cref="OperationTestBase{T}"/>.
/// </summary>
type TestApplicationFactory (testContext : TestContext) =
    inherit OperationTestApplicationFactory (testContext)

    let partition = "integration"

    let items =
        seq { 1..3 }
        |> Seq.map (fun index -> {
            id = $"item-{index}"
            partitionKey = partition
            name = $"item-{index}"
            quantity = index
        })
        |> ImmutableArray.CreateRange

    /// The values that the tests read.
    member val TestData : TestData = { Partition = partition; Items = items }

    override this.SeedDataAsync (cancellationToken : CancellationToken) : Task = task {
        for seededItem in items do
            let! response =
                this.Container.ExecuteAsync (
                    create {
                        item seededItem
                        partitionKey seededItem.partitionKey
                    },
                    cancellationToken
                )

            CosmosAssert.IsOk (response.Result, $"Seed create should succeed for '{seededItem.id}'.")
    }
```

* If the scenario writes into the container of `OperationTestBase`, make the factory inherit from `OperationTestApplicationFactory`. Write the items with `this.Container`.
* Otherwise, make the factory inherit from `DatabaseTestApplicationFactory`. Override `Containers` and return a `ContainerSet` with the containers of the scenario: `ContainerSet.Create (ContainerProperties ("orders", "/customerId"))`. To add a container to the containers of a base class, call `base.Containers.Add other`. Get a container from the database of the fixture: `this.Client.GetContainer (this.DatabaseId, id)`.
* Do not create a container in `SeedDataAsync`. `InitializeAsync` of the fixture creates the `Containers` before `SeedDataAsync` runs.
* Declare each container once. A `ContainerSet` throws `ArgumentException` for a second declaration of an identifier. Without it, `CreateContainerIfNotExistsAsync` ignores all settings of the second declaration, except the partition key path.
* The fixture takes one partition of the emulator for each declared container. Override `ContainerCount` only if a test creates more containers.
* Write each item with its partition key.
* Examine the response of each write with `CosmosAssert.IsOk`. Thus a failed seed stops the test with a clear message.
* Give the cancellation token of `SeedDataAsync` to each call.
* If a client with other serializer options must write the documents, create the client in `SeedDataAsync` with `use`. Do not keep the client in the factory.
* In a scenario of `OperationTestApplicationFactory`, get the container of that client with `this.GetContainerOf client`. In another scenario, get it with `client.GetContainer (this.DatabaseId, id)`.
* For a static collection of values, pipe a sequence expression into `ImmutableArray.CreateRange`.
* Each test has a database of its own. Thus an identifier must be unique only in the scenario.

## Test class

```fsharp
namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Threading.Tasks

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Azure.Cosmos.Tests.Integration.ThreeItemsScenario

[<TestClass; IterationExtensionsTestCategory>]
type ThreeItemsTests () =
    inherit OperationTestBase<ThreeItemsScenario.TestApplicationFactory> ()

    override _.CreateApplication context = ThreeItemsScenario.TestApplicationFactory (context)

    [<TestMethod>]
    member this.``A query of the partition returns the seeded items`` () : Task = task {
        let { Partition = partition; Items = items } = this.Application.TestData
        let! container = this.GetContainer ()

        let options = QueryRequestOptions (PartitionKey = Nullable (PartitionKey partition))
        use iterator = container.GetItemQueryIterator<TestItem>("SELECT * FROM c", requestOptions = options)
        let ids = ResizeArray<string>()

        while iterator.HasMoreResults do
            let! page = iterator.ReadNextAsync this.CancellationToken
            ids.AddRange (page |> Seq.map _.id)

        CollectionAssert.AreEquivalent (
            items |> Seq.map _.id |> Seq.toArray,
            ids.ToArray (),
            "The query should return each seeded item one time."
        )
    }
```

* Make the test class inherit from `OperationTestBase<ThreeItemsScenario.TestApplicationFactory>`. If the tests do not use the container of the operation tests, make the test class inherit from `IntegrationTestBase<ThreeItemsScenario.TestApplicationFactory>`.
* Override `CreateApplication`, and return a new `TestApplicationFactory`.
* Refer to `TestApplicationFactory` through the namespace of the scenario: `ThreeItemsScenario.TestApplicationFactory`. Thus the test class shows which scenario it uses.
* Open the namespace of the scenario for its other types. Thus the record pattern finds the labels of `TestData`.
* Read the values with a record pattern. Read only the fields that the test uses. Do not write `let data = this.Application.TestData`.
* Do not create the documents of the scenario in a test. A test can create its own input values, for example the parameters of a query.
* Give the class its test categories (refer to `test-categories.instructions.md`).

## Lifetime

`IntegrationTestBase` creates a new factory for each test in its `[<TestInitialize>]` method. The factory creates the database of the test, and `SeedDataAsync` writes the data. The `[<TestCleanup>]` method deletes the database. Thus a test cannot change the data of another test.
