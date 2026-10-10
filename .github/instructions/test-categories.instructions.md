---
applyTo: "tests/**/*.fs"
---

# Test category attributes

* Declare each test category as a `TestCategoryBaseAttribute` descendant. Do not write the category as a string in `[<TestCategory "...">]`.
* Make each attribute give one category. Do not put two categories into one attribute.
* Use the `TestCategoryAttribute` suffix in the name of each attribute.
* The category is the name without the suffix, written as separate words. Start the category with a capital letter. Write the other words in lower case. For example, `ValueCollectionsTestCategoryAttribute` gives the category `Value collections`.
* Keep the spelling of a product name or a type name in a category. For example, `CosmosDbEmulatorTestCategoryAttribute` gives `Cosmos DB Emulator`, and `IcedTasksProbeTestCategoryAttribute` gives `IcedTasks probe`.
* If a test class runs without the Azure Cosmos DB Emulator, put `UnitTestCategory` on the class.
* If a test class runs against the emulator, the class must have `CosmosDbEmulatorTestCategory`. `IntegrationTestBase` has this attribute. Thus, each class that inherits from `IntegrationTestBase` gets the category.
* Also put the attribute of the operation or component that the class examines on the class. For example, write `[<TestClass; UnitTestCategory; ValueCollectionsTestCategory>]`.
* Declare the attributes that all test projects use in `tests/Cosmos.Tests.Infrastructure/TestCategories.fs`. Declare the other attributes in the `TestCategories.fs` file of their test project.
* Keep the attributes of each `TestCategories.fs` file sorted alphabetically by type name. Put a new attribute at its sorted position. Do not put a new attribute at the end of the file.
