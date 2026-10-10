// Every TestCategoryBaseAttribute descendant used to categorize tests in this project, one per operation or
// component, sorted by name. An abstract MSTest attribute whose TestCategories list is picked up by
// --filter TestCategory=... exactly like [<TestCategory>]'s, but self-sufficient: no separate string constant needed
// to know what to pass it.
// The categories every test project shares, such as the Cosmos DB Emulator one, live in the test infrastructure project.
namespace FSharp.Azure.Cosmos.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

type BuildersTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Builders" |] :> IList<string>

type CreateTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Create" |] :> IList<string>

type DeleteTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Delete" |] :> IList<string>

/// <summary>
/// Categorizes <see cref="IcedTasksProbeTests"/> as both a fast, emulator-free unit test and the probe that gates the
/// use of IcedTasks computation expressions in library code.
/// </summary>
type IcedTasksProbeTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "IcedTasksProbe" |] :> IList<string>

/// <summary>
/// Categorizes an emulator-backed integration test as covering the iteration extensions:
/// <see cref="Microsoft.Azure.Cosmos.FeedIteratorExtensions"/> and <see cref="Microsoft.Azure.Cosmos.QueryableExtensions"/>.
/// </summary>
type IterationExtensionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "IterationExtensions" |] :> IList<string>

/// <summary>
/// Categorizes <see cref="IterationExtensionsUnitTests"/> as both a fast, emulator-free unit test and coverage for the
/// iteration extensions: <see cref="Microsoft.Azure.Cosmos.FeedIteratorExtensions"/> and
/// <see cref="Microsoft.Azure.Cosmos.QueryableExtensions"/>.
/// </summary>
type IterationExtensionsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "IterationExtensions" |] :> IList<string>

type PatchTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Patch" |] :> IList<string>

type ReadExtensionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "ReadExtensions" |] :> IList<string>

type ReadManyTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "ReadMany" |] :> IList<string>

type ReadTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Read" |] :> IList<string>

type ReplaceTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Replace" |] :> IList<string>

/// <summary>
/// Categorizes <see cref="ResponseMessageTests"/> as both a fast, emulator-free unit test and coverage for the
/// <see cref="FSharp.Azure.Cosmos.ResponseMessageModule"/> module.
/// </summary>
type ResponseMessageUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "ResponseMessage" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the shared test infrastructure as fast, emulator-free unit tests:
/// <see cref="DatabaseIdentifierTests"/>, <see cref="PartitionBudgetTests"/> and <see cref="LeftoverSweepTests"/>.
/// </summary>
type TestInfrastructureUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "TestInfrastructure" |] :> IList<string>

type UpsertTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Upsert" |] :> IList<string>

type ValidationTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Validation" |] :> IList<string>

/// <summary>
/// Categorizes <see cref="ValueCollectionsTests"/> as both a fast, emulator-free unit test and coverage for the
/// collection functions of <see cref="T:FSharp.Azure.Cosmos.ValueCollections"/>, which every project compiles.
/// </summary>
type ValueCollectionsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "ValueCollections" |] :> IList<string>
