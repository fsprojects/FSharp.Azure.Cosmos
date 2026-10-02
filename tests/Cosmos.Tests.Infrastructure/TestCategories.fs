// The test categories every test project shares. A category attribute class rather than a string constant, so it is
// self-sufficient: --filter TestCategory=... picks its TestCategories up exactly like [<TestCategory>]'s. Categories of
// one test project's own operations and components stay in that project.
namespace FSharp.Azure.Cosmos.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a test as needing the Cosmos DB Emulator.
/// <see cref="T:FSharp.Azure.Cosmos.Tests.Integration.IntegrationTestBase`1"/> carries it, so every class derived from
/// it inherits it.
/// </summary>
type CosmosDbEmulatorTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Cosmos DB Emulator" |] :> IList<string>
