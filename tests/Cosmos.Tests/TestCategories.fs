// Every TestCategoryBaseAttribute descendant used to categorize tests in this project, one per operation or
// component, in the same order as the string constants they replace used to be declared in. An abstract
// MSTest attribute whose TestCategories list is picked up by --filter TestCategory=... exactly like
// [<TestCategory>]'s, but self-sufficient: no separate string constant needed to know what to pass it.
namespace FSharp.Azure.Cosmos.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

type BuildersTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Builders" |] :> IList<string>

type CreateTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Create" |] :> IList<string>

type ReadTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Read" |] :> IList<string>

type ReadManyTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "ReadMany" |] :> IList<string>

type UpsertTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Upsert" |] :> IList<string>

type ReplaceTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Replace" |] :> IList<string>

type PatchTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Patch" |] :> IList<string>

type DeleteTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Delete" |] :> IList<string>

type ReadExtensionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "ReadExtensions" |] :> IList<string>

/// Categorizes an emulator-backed integration test as covering `IterationExtensions`.
type IterationExtensionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "IterationExtensions" |] :> IList<string>

type ValidationTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Validation" |] :> IList<string>

/// Categorizes a test as needing the Cosmos DB Emulator, the way the shared integration test base class does.
type CosmosDbEmulatorTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Cosmos DB Emulator" |] :> IList<string>

/// Categorizes `IterationExtensionsUnitTests` as both a fast, emulator-free unit test and coverage for the
/// `IterationExtensions` component.
type IterationExtensionsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Unit"; "IterationExtensions" |] :> IList<string>
