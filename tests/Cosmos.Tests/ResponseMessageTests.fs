namespace FSharp.Azure.Cosmos.Tests

open System
open System.Net
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Azure.Cosmos

open FSharp.Azure.Cosmos

[<TestClass; ResponseMessageUnitTestCategory>]
type ResponseMessageTests () =

    [<TestMethod>]
    member _.``SubStatusCode returns the sub-status header value`` () =
        use response = new ResponseMessage (HttpStatusCode.NotFound)
        response.Headers.Set ("x-ms-substatus", "1003")

        Assert.AreEqual (
            SubStatusCodes.OwnerResourceNotFound,
            response.SubStatusCode,
            "SubStatusCode should return the x-ms-substatus header value."
        )
        Assert.AreEqual (
            SubStatusCodes.OwnerResourceNotFound,
            ResponseMessage.getSubStatusCode response,
            "getSubStatusCode should return the x-ms-substatus header value."
        )

    [<TestMethod>]
    member _.``SubStatusCode returns 0 when the response has no sub-status header`` () =
        use response = new ResponseMessage (HttpStatusCode.NotFound)

        Assert.AreEqual (
            SubStatusCodes.Unknown,
            response.SubStatusCode,
            "SubStatusCode should return 0 without an x-ms-substatus header."
        )

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow("not-a-number", DisplayName = "not a number")>]
    member _.``SubStatusCode returns 0 when the sub-status header is not a number`` (headerValue : string) =
        use response = new ResponseMessage (HttpStatusCode.NotFound)
        response.Headers.Set ("x-ms-substatus", headerValue)

        Assert.AreEqual (
            SubStatusCodes.Unknown,
            response.SubStatusCode,
            "SubStatusCode should return 0 for a malformed x-ms-substatus header."
        )

    [<TestMethod>]
    member _.``getSubStatusCode throws ArgumentNullException for a null response`` () =
        Assert.ThrowsExactly<ArgumentNullException>(
            (fun () ->
                ResponseMessage.getSubStatusCode Unchecked.defaultof<ResponseMessage>
                |> ignore
            ),
            "getSubStatusCode should throw ArgumentNullException for a null response."
        )
        |> ignore
