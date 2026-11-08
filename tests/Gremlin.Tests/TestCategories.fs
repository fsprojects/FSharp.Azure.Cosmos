// The categories of the FSharp.Gremlin tests, one for each component, sorted by name. The categories that all test
// projects use, Unit and Cosmos DB Emulator, are in the test infrastructure project.
namespace FSharp.Gremlin.Tests

open System.Collections.Generic

open Microsoft.VisualStudio.TestTools.UnitTesting

type GraphModelTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Graph model" |] :> IList<string>

type SerializerProfileTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Serializer profile" |] :> IList<string>
