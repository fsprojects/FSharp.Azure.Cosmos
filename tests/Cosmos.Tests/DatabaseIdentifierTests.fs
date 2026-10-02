namespace FSharp.Azure.Cosmos.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests.Integration

/// <summary>
/// Emulator-free coverage of <see cref="DatabaseIdentifier"/>: the identifiers under which every
/// <see cref="DatabaseTestApplicationFactory"/> creates its database.
/// </summary>
[<TestClass; TestInfrastructureUnitTestCategory>]
type DatabaseIdentifierTests () =
    inherit TestBase ()

    static let methodIdentifier (className : string) (testName : string) =
        DatabaseIdentifier.create {
            ClassName = className
            TestName = ValueSome testName
            TestData = null
            DisplayName = null
        }

    static let rowIdentifier (testData : objnull array) =
        DatabaseIdentifier.create {
            ClassName = "Tests.Class"
            TestName = ValueSome "Data-driven test"
            TestData = testData
            DisplayName = null
        }

    [<TestMethod>]
    member _.``create returns the same identifier in every process`` () =
        // Expected values computed independently in PowerShell, which sanitises, cuts and encodes on its own and hashes
        // with SHA-256 over the UTF-16LE text. A per-process hash such as String.GetHashCode would fail here on the next
        // run, and leftovers could then never be reused or swept.
        Assert.AreEqual (
            "fsac-test-669e7a45_Create_execute_persists_item",
            methodIdentifier
                "FSharp.Azure.Cosmos.Tests.Integration.CreateOperationIntegrationTests"
                "Create execute persists item",
            "The identifier should be the prefix, the hash of the class and test names and the sanitised test name."
        )

        // The row hash covers 15:System.Object[]27:13:System.String9:deletedAt13:System.String12:letters only
        Assert.AreEqual (
            "fsac-test-764f2105_IsNotDeletedAsync_evaluates_valid_deleted_field_name_a26a2c80",
            DatabaseIdentifier.create {
                ClassName = "FSharp.Azure.Cosmos.Tests.Integration.ReadExtensionsIntegrationTests"
                TestName = ValueSome "IsNotDeletedAsync evaluates valid deleted field name shapes in the query"
                TestData = [| "deletedAt" |]
                DisplayName = "letters only"
            },
            "The identifier of a data row should end with the cut test name and the stable hash of the row."
        )

    [<TestMethod>]
    [<DataRow("/", DisplayName = "slash")>]
    [<DataRow("\\", DisplayName = "backslash")>]
    [<DataRow("?", DisplayName = "question mark")>]
    [<DataRow("#", DisplayName = "number sign")>]
    [<DataRow(" ", DisplayName = "space")>]
    [<DataRow("\t", DisplayName = "tab")>]
    [<DataRow("%", DisplayName = "percent sign")>]
    [<DataRow("\"", DisplayName = "quotation mark")>]
    [<DataRow("<", DisplayName = "less-than sign")>]
    [<DataRow(">", DisplayName = "greater-than sign")>]
    [<DataRow("|", DisplayName = "vertical bar")>]
    [<DataRow(":", DisplayName = "colon")>]
    [<DataRow("*", DisplayName = "asterisk")>]
    member _.``create replaces an invalid character of the test name by an underscore`` (character : string) =
        let identifier = methodIdentifier "Tests.Class" $"before{character}after"

        Assert.EndsWith (
            "_before_after",
            identifier,
            StringComparison.Ordinal,
            "The invalid character should be replaced by an underscore."
        )

    [<TestMethod>]
    member _.``create keeps equally named methods of two classes apart`` () =
        Assert.AreNotEqual (
            methodIdentifier "Tests.FirstClass" "Same test name",
            methodIdentifier "Tests.SecondClass" "Same test name",
            "Equally named methods of two classes should get different identifiers."
        )

    [<TestMethod>]
    member _.``create keeps apart test names of one class that differ only in replaced characters`` () =
        // All three read a_b once sanitised; parallel tests sharing a database would delete it under each other
        let identifiers = [|
            methodIdentifier "Tests.Class" "a/b"
            methodIdentifier "Tests.Class" "a b"
            methodIdentifier "Tests.Class" "a_b"
        |]

        Assert.HasCount (
            identifiers.Length,
            Array.distinct identifiers,
            "Test names that read alike once sanitised should get different identifiers."
        )

    [<TestMethod>]
    member _.``create gives every data row of a method its own identifier`` () =
        // Without display names, so that the row values alone tell the rows apart
        let identifiers = [|
            rowIdentifier [| "" |]
            rowIdentifier [| " " |]
            rowIdentifier [| null |]
            rowIdentifier [| "null" |]
            rowIdentifier [| "a,b" |]
            rowIdentifier [| "a"; "b" |]
            rowIdentifier [| 1 |]
            rowIdentifier [| 1L |]
            rowIdentifier [| 1.0 |]
            rowIdentifier [| '1' |]
            rowIdentifier [| "1" |]
            rowIdentifier [| "1:1" |]
            rowIdentifier [| [| 1; 2 |] |]
            rowIdentifier [| [| 1 |]; [| 2 |] |]
            rowIdentifier [| [| box 1; box 2 |] |]
            // Lone surrogates, which UTF-8 would replace by one and the same U+FFFD. Built at run time, because the
            // compiler already replaces a lone surrogate in a string literal by U+FFFD.
            rowIdentifier [| String (char 0xD800, 1) |]
            rowIdentifier [| String (char 0xDC00, 1) |]
        |]

        Assert.HasCount (identifiers.Length, Array.distinct identifiers, "Every data row should get its own identifier.")

    [<TestMethod>]
    member _.``create keeps apart data rows whose values read alike but differ in type`` () =
        // Both read 1; one database for both rows would let them recreate or delete it under each other in parallel
        Assert.AreNotEqual (
            rowIdentifier [| 1 |],
            rowIdentifier [| 1L |],
            "Data rows of 1 as int and of 1 as int64 should get different identifiers."
        )

    [<TestMethod>]
    member _.``create cuts a long name to the maximum length and keeps apart names that differ only beyond the cut`` () =
        let commonStart = String.replicate 100 "a"
        let first = methodIdentifier "Tests.Class" $"{commonStart} first"
        let second = methodIdentifier "Tests.Class" $"{commonStart} second"

        Assert.HasCount (DatabaseIdentifier.MaxLength, first, "A long name should be cut to the maximum length.")
        Assert.HasCount (DatabaseIdentifier.MaxLength, second, "A long name should be cut to the maximum length.")
        Assert.AreNotEqual (first, second, "Names that differ only beyond the cut should get different identifiers.")

    [<TestMethod>]
    member _.``create names a class-level database after the class and apart from a method named like the class`` () =
        let classIdentifier =
            DatabaseIdentifier.create {
                ClassName = "Tests.Integration.Scenario"
                TestName = ValueNone
                TestData = null
                DisplayName = null
            }

        Assert.StartsWith (
            DatabaseIdentifier.Prefix,
            classIdentifier,
            StringComparison.Ordinal,
            "A class-level identifier should start with the common prefix."
        )

        Assert.EndsWith (
            "-Scenario",
            classIdentifier,
            StringComparison.Ordinal,
            "A class-level identifier should end with the class name."
        )

        Assert.AreNotEqual (
            methodIdentifier "Tests.Integration.Scenario" "Scenario",
            classIdentifier,
            "A class-level identifier should differ from the one of a method named like the class."
        )

    [<TestMethod>]
    [<DataRow("row value", DisplayName = "row display name")>]
    member this.``ofTestContext reads the class, the test name and the data row of the running test`` (_ : string) =
        let expected =
            DatabaseIdentifier.create {
                ClassName = "FSharp.Azure.Cosmos.Tests.DatabaseIdentifierTests"
                TestName = ValueSome "ofTestContext reads the class, the test name and the data row of the running test"
                TestData = [| "row value" |]
                DisplayName = "row display name"
            }

        Assert.AreEqual (
            expected,
            DatabaseIdentifier.ofTestContext this.TestContext,
            "The identifier should be built from the class, the test name, the data row and its display name."
        )
