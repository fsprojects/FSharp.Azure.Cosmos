// The categories of the FSharp.Azure.Cosmos.Quotations tests. Every test here is a fast, emulator-free unit test, so
// every category also carries "Unit", which --filter TestCategory=Unit selects. The attributes are sorted by name.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a unit test of the evaluator of the parts of a quotation that read no variable:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.EvaluatorTests"/>.
/// </summary>
type EvaluatorUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Evaluator" |] :> IList<string>

/// <summary>
/// Categorizes a unit test of the normalizer:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerSpellingTests"/>,
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerTests"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerErrorTests"/>.
/// </summary>
type NormalizerUnitTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Unit"; "Normalizer" |] :> IList<string>
