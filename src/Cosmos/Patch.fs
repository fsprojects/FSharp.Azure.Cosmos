[<AutoOpen>]
module FSharp.Azure.Cosmos.Patch

open System.Collections.Immutable
open System.Linq
open Microsoft.Azure.Cosmos

[<Struct>]
type PatchOperation<'T> = {
    Operations : PatchOperation list
    Id : string
    PartitionKey : PartitionKey
    RequestOptions : PatchItemRequestOptions
}

open System

type PatchBuilder<'T> (enableContentResponseOnWrite : bool) =
    member _.Yield _ =
        {
            Operations = []
            Id = String.Empty
            PartitionKey = PartitionKey.None
            RequestOptions = PatchItemRequestOptions (EnableContentResponseOnWrite = enableContentResponseOnWrite)
        }
        : PatchOperation<'T>

    /// <summary>Adds a <see cref="PatchOperation"/></summary>
    [<CustomOperation "operation">]
    member _.Operation (state : PatchOperation<'T>, operation) = { state with Operations = operation :: state.Operations }

    /// <summary>Adds the <see cref="PatchOperation"/></summary>
    [<CustomOperation "operations">]
    member _.Operations (state : PatchOperation<'T>, operations) = { state with Operations = state.Operations @ operations }

    /// Sets the Id of an item being patched
    [<CustomOperation "id">]
    member _.Id (state : PatchOperation<'T>, id) = { state with Id = id }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : PatchOperation<'T>, partitionKey : PartitionKey) = { state with PartitionKey = partitionKey }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : PatchOperation<'T>, partitionKey : string) = {
        state with
            PartitionKey = (PartitionKey partitionKey)
    }

    /// Sets the request options
    [<CustomOperation "requestOptions">]
    member _.RequestOptions (state : PatchOperation<'T>, options : PatchItemRequestOptions) =
        options.EnableContentResponseOnWrite <- state.RequestOptions.EnableContentResponseOnWrite
        { state with RequestOptions = options }

    /// <summary>Sets the eTag to <see cref="PatchItemRequestOptions.IfMatchEtag"/></summary>
    [<CustomOperation "eTag">]
    member _.ETag (state : PatchOperation<'T>, eTag : string) =
        state.RequestOptions.IfMatchEtag <- eTag
        state

    // ------------------------------------------- Patch request options -------------------------------------------
    /// Sets the filter predicate
    [<CustomOperation "filterPredicate">]
    member _.FilterPredicate (state : PatchOperation<'T>, filterPredicate : string) =
        state.RequestOptions.FilterPredicate <- filterPredicate
        state

    // ------------------------------------------- Request options -------------------------------------------
    /// <summary>Sets the operation <see cref="ConsistencyLevel"/></summary>
    [<CustomOperation "consistencyLevel">]
    member _.ConsistencyLevel (state : PatchOperation<_>, consistencyLevel : ConsistencyLevel Nullable) =
        state.RequestOptions.ConsistencyLevel <- consistencyLevel
        state

    /// Sets if the response should include the content of the item after the operation
    [<CustomOperation "enableContentResponseOnWrite">]
    member _.EnableContentResponseOnWrite (state : PatchOperation<_>, enableContentResponseOnWrite : bool) =
        state.RequestOptions.EnableContentResponseOnWrite <- enableContentResponseOnWrite
        state

    /// Sets the indexing directive
    [<CustomOperation "indexingDirective">]
    member _.IndexingDirective (state : PatchOperation<_>, indexingDirective : IndexingDirective Nullable) =
        state.RequestOptions.IndexingDirective <- indexingDirective
        state

    /// Adds a trigger to be invoked before the operation
    [<CustomOperation "preTrigger">]
    member _.PreTrigger (state : PatchOperation<_>, trigger : string) =
        state.RequestOptions.AddPreTrigger trigger
        state

    /// Adds triggers to be invoked before the operation
    [<CustomOperation "preTriggers">]
    member _.PreTriggers (state : PatchOperation<_>, triggers : seq<string>) =
        state.RequestOptions.AddPreTriggers triggers
        state

    /// Adds a trigger to be invoked after the operation
    [<CustomOperation "postTrigger">]
    member _.PostTrigger (state : PatchOperation<_>, trigger : string) =
        state.RequestOptions.AddPostTrigger trigger
        state

    /// Adds triggers to be invoked after the operation
    [<CustomOperation "postTriggers">]
    member _.PostTriggers (state : PatchOperation<_>, triggers : seq<string>) =
        state.RequestOptions.AddPostTriggers triggers
        state

    /// Sets the session token
    [<CustomOperation "sessionToken">]
    member _.SessionToken (state : PatchOperation<_>, sessionToken : string) =
        state.RequestOptions.SessionToken <- sessionToken
        state

let patch<'T> = PatchBuilder<'T> (false)
let patchAndRead<'T> = PatchBuilder<'T> (true)

// https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb

/// Represents the result of a patch operation.
type PatchResult<'t> =
    | Ok of 't // 200
    | BadRequest of ResponseBody : string // 400
    | NotFound of ResponseBody : string // 404
    /// Precondition failed
    | ModifiedBefore of ResponseBody : string // 412 - need re-do
    | TooManyRequests of ResponseBody : string * RetryAfter : TimeSpan voption // 429

open System.Net

module CosmosException =

    let toPatchResult (ex : CosmosException) =
        match ex.StatusCode with
        | HttpStatusCode.BadRequest -> PatchResult.BadRequest ex.ResponseBody
        | HttpStatusCode.NotFound -> PatchResult.NotFound ex.ResponseBody
        | HttpStatusCode.PreconditionFailed -> PatchResult.ModifiedBefore ex.ResponseBody
        | HttpStatusCode.TooManyRequests -> PatchResult.TooManyRequests (ex.ResponseBody, ex.RetryAfter |> ValueOption.ofNullable)
        | _ -> raise ex

open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open CosmosException

type Microsoft.Azure.Cosmos.Container with

    /// <summary>
    /// Executes a patch operation and returns <see cref="ItemResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    member container.PlainExecuteAsync<'T> (operation : PatchOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        container.PatchItemAsync<'T> (
            operation.Id,
            operation.PartitionKey,
            operation.Operations.ToImmutableList (),
            operation.RequestOptions,
            cancellationToken = cancellationToken
        )

    /// <summary>
    /// Executes a patch operation, transforms success or failure, and returns <see cref="CosmosResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Patch operation</param>
    /// <param name="success">Result transform if success</param>
    /// <param name="failure">Error transform if failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteOverwriteAsync<'T, 'Result>
        (operation : PatchOperation<'T>, success, failure, [<Optional>] cancellationToken : CancellationToken)
        : Task<CosmosResponse<'Result>>
        =
        task {
            try
                let! response = container.PlainExecuteAsync<'T> (operation, cancellationToken)
                return CosmosResponse.fromItemResponse success response
            with HandleException ex ->
                return CosmosResponse.fromException failure ex
        }

    /// <summary>
    /// Executes a patch operation safely and returns <see cref="CosmosResponse{PatchResult{T}}"/>.
    /// Requires ETag to be set in <see cref="PatchItemRequestOptions"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    member container.ExecuteAsync<'T> (operation : PatchOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        if String.IsNullOrEmpty operation.RequestOptions.IfMatchEtag then
            invalidArg "eTag" "Safe patch requires ETag"

        container.ExecuteOverwriteAsync (operation, PatchResult.Ok, toPatchResult, cancellationToken)

    /// <summary>
    /// Executes a patch operation and returns <see cref="CosmosResponse{PatchResult{T}}"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    member container.ExecuteOverwriteAsync<'T>
        (operation : PatchOperation<'T>, [<Optional>] cancellationToken : CancellationToken)
        =
        container.ExecuteOverwriteAsync (operation, PatchResult.Ok, toPatchResult, cancellationToken)
