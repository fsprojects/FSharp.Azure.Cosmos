// The categories of the FSharp.Azure.Cosmos.Sql tests. Every test here is a fast, emulator-free unit test, so every
// test class also has UnitTestCategory, which --filter TestCategory=Unit selects. The attributes are sorted by name.
namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a test of the collection types that the syntax tree is built from:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.EquatableArrayTests"/>,
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.EquatableArrayModuleTests"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.ImmutableArrayPatternsTests"/>.
/// </summary>
type CollectionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Collections" |] :> IList<string>
