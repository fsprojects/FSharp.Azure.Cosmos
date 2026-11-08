// The categories of the FSharp.Azure.Cosmos.Gremlin tests, one for each component, sorted by name. The categories that
// all test projects use, Unit and Cosmos DB Emulator, are in the test infrastructure project.
namespace FSharp.Azure.Cosmos.Gremlin.Tests

open System.Collections.Generic

open Microsoft.VisualStudio.TestTools.UnitTesting

type ConnectionTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Connection" |] :> IList<string>
