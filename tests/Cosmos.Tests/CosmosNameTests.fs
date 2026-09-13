namespace FSharp.Azure.Cosmos.Tests

open System
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; ValidationTestCategory>]
type CosmosNameTests () =

    [<TestMethod>]
    [<DataRow("deletedAt", DisplayName = "letters only")>]
    [<DataRow("_deletedAt", DisplayName = "starts with underscore")>]
    [<DataRow("deletedAt1", DisplayName = "digit after first character")>]
    [<DataRow("a", DisplayName = "single letter")>]
    [<DataRow("_", DisplayName = "single underscore")>]
    member _.``ValidateField accepts valid field names`` (fieldName : string) = CosmosName.validateField "fieldName" fieldName

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow(" ", DisplayName = "whitespace")>]
    [<DataRow("1deletedAt", DisplayName = "starts with digit")>]
    [<DataRow("1deleted", DisplayName = "starts with digit, short name")>]
    [<DataRow("deleted-at", DisplayName = "contains hyphen")>]
    [<DataRow("deleted-field", DisplayName = "contains hyphen, another field name")>]
    [<DataRow("deleted.field", DisplayName = "contains dot")>]
    [<DataRow("deleted field", DisplayName = "contains space")>]
    member _.``ValidateField throws ArgumentException for invalid field names`` (fieldName : string) =
        Assert.ThrowsExactly<ArgumentException>(
            (fun () -> CosmosName.validateField "fieldName" fieldName),
            "ValidateField should throw ArgumentException for invalid field names."
        )
        |> ignore

    [<TestMethod>]
    member _.``ValidateField throws ArgumentNullException for null field name`` () =
        Assert.ThrowsExactly<ArgumentNullException>(
            (fun () -> CosmosName.validateField "fieldName" Unchecked.defaultof<string>),
            "ValidateField should throw ArgumentNullException for null field name."
        )
        |> ignore

    [<TestMethod>]
    member _.``ValidateField reports the caller supplied parameter name on failure`` () =
        let exn =
            Assert.ThrowsExactly<ArgumentException>(fun () -> CosmosName.validateField "customParam" "1invalid")

        Assert.AreEqual ("customParam", exn.ParamName, "ValidateField should report the supplied paramName on failure.")

    [<TestMethod>]
    member _.``ValidateField reports the caller supplied parameter name on null`` () =
        let exn =
            Assert.ThrowsExactly<ArgumentNullException>(fun () ->
                CosmosName.validateField "customParam" Unchecked.defaultof<string>
            )

        Assert.AreEqual ("customParam", exn.ParamName, "ValidateField should report the supplied paramName on null.")
