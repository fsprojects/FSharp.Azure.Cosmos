// The categories of the FSharp.Azure.Cosmos.Quotations tests. Every test here is a fast, emulator-free unit test, so
// every test class also has UnitTestCategory, which --filter TestCategory=Unit selects. The attributes are sorted
// by name.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes a test of the evaluator of the parts of a quotation that read no variable:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.EvaluatorTests"/>.
/// </summary>
type EvaluatorTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Evaluator" |] :> IList<string>

/// <summary>
/// Categorizes a test of the normalizer:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerSpellingTests"/>,
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerTests"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerErrorTests"/>.
/// </summary>
type NormalizerTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Normalizer" |] :> IList<string>

/// <summary>
/// Categorizes a test of the parameterizer, which tells the shape of a quotation from the values it was written
/// with: <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.ParameterizerShapeTests"/> and
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.ParameterizerTests"/>.
/// </summary>
type ParameterizerTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()

    /// <inheritdoc />
    override _.TestCategories = [| "Parameterizer" |] :> IList<string>
