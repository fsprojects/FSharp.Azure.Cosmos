// The categories of the FSharp.Azure.Cosmos.Sql tests. Every test here is a fast, emulator-free unit test, so every
// category also carries "Unit", which --filter TestCategory=Unit selects. The attributes are sorted by name.
namespace FSharp.Azure.Cosmos.Sql.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a unit test of the function catalog: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.CatalogTests"/>.
/// </summary>
type CatalogUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Catalog" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the collection types that the syntax tree is built from:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.EquatableArrayTests"/>,
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.EquatableArrayModuleTests"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.ImmutableArrayPatternsTests"/>.
/// </summary>
type CollectionsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Collections" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the generator helpers that the property tests and the test data are built from:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.GeneratorHelperTests"/>.
/// </summary>
type GeneratorsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Generators" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the conversion from the parse tree of the SDK's grammar into the syntax tree:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.SdkGrammarTests"/>.
/// </summary>
type GrammarUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Grammar" |] :> IList<string>

/// <summary>
/// Categorizes a law of the printer, checked as a property on generated syntax trees:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.PrinterLawsTests"/>.
/// </summary>
type PrinterLawsUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "PrinterLaws" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the printer and its inline mode:
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.PrinterTests"/> and <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.InlineTests"/>.
/// </summary>
type PrinterUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Printer" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the syntax tree: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.SyntaxTests"/>.
/// </summary>
type SyntaxTreeUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "SyntaxTree" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the validator: <see cref="T:FSharp.Azure.Cosmos.Sql.Tests.ValidatorTests"/>.
/// </summary>
type ValidatorUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Validator" |] :> IList<string>
