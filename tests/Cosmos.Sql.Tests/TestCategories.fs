// The categories of the FSharp.Azure.Cosmos.Sql tests. Every test here is a fast, emulator-free unit test, so every
// category also carries "Unit", which --filter TestCategory=Unit selects. The attributes are sorted by name.
namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a unit test of the collection types that the syntax tree is built from:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.EquatableArrayTests"/>.
/// </summary>
type CollectionsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Collections" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the syntax tree: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.SyntaxTests"/>.
/// </summary>
type SyntaxTreeUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "SyntaxTree" |] :> IList<string>
