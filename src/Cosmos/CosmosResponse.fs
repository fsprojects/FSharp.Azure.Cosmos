namespace FSharp.Azure.Cosmos

open System
open System.Net
open Microsoft.Azure.Cosmos

/// <summary>
/// Represents the response from a Cosmos DB operation.
/// </summary>
type CosmosResponse<'T> = {
    HttpStatusCode : HttpStatusCode
    Headers : Headers
    Result : 'T
    Diagnostics : CosmosDiagnostics
    Exception : Exception voption
} with

    member this.RequestCharge = this.Headers.RequestCharge
    member this.ActivityId = this.Headers.ActivityId
    member this.ETag = this.Headers.ETag

module CosmosResponse =

    let fromItemResponse (successFn : 'T -> 'Result) (response : ItemResponse<'T>) = {
        HttpStatusCode = response.StatusCode
        Headers = response.Headers
        Result = successFn response.Resource
        Diagnostics = response.Diagnostics
        Exception = ValueNone
    }

    let fromFeedResponse (successFn : FeedResponse<'T> -> 'Result) (response : FeedResponse<'T>) = {
        HttpStatusCode = response.StatusCode
        Headers = response.Headers
        Result = successFn response
        Diagnostics = response.Diagnostics
        Exception = ValueNone
    }

    let fromException resultFn (ex : CosmosException) = {
        HttpStatusCode = ex.StatusCode
        Headers = ex.Headers
        Result = resultFn ex
        Diagnostics = ex.Diagnostics
        Exception = ValueSome ex
    }

    let toException<'T> (response : CosmosResponse<'T>) = response.Exception.Value
