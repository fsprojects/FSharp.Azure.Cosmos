// The categories of the FSharp.Azure.Cosmos.Sql tests. Every test here is a fast, emulator-free unit test, so every
// test class also has UnitTestCategory, which --filter TestCategory=Unit selects. The attributes are sorted by name.
namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a test of the function catalog: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.CatalogTests"/>.
/// </summary>
type CatalogTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Catalog" |] :> IList<string>

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

/// <summary>
/// Categorizes a test of the printer and its inline mode:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.PrinterTests"/> and <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.InlineTests"/>.
/// </summary>
type PrinterTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Printer" |] :> IList<string>

/// <summary>
/// Categorizes a test of the syntax tree: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.SyntaxTests"/>.
/// </summary>
type SyntaxTreeTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Syntax tree" |] :> IList<string>

/// <summary>
/// Categorizes a test of the validator: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.ValidatorTests"/>.
/// </summary>
type ValidatorTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Validator" |] :> IList<string>
