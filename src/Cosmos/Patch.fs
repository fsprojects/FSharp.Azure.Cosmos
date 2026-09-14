[<AutoOpen>]
module FSharp.Azure.Cosmos.Patch

open System.Collections.Immutable
open System.Linq
open System.Threading
open System.Threading.Tasks
open Microsoft.Azure.Cosmos

[<Struct>]
type PatchOperation<'T> = {
    Operations : PatchOperation list
    Id : string
    PartitionKey : PartitionKey
    RequestOptions : PatchItemRequestOptions
}

[<Struct>]
type PatchConcurrentlyOperation<'T, 'E> = {
    Id : string
    PartitionKey : PartitionKey
    RequestOptions : PatchItemRequestOptions
    Update : 'T -> Task<Result<PatchOperation list, 'E>>
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
    member _.Operations (state : PatchOperation<'T>, operations) = {
        state with
            Operations = [ yield! state.Operations; yield! operations ]
    }

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

type PatchConcurrentlyBuilder<'T, 'E> (enableContentResponseOnWrite : bool) =
    member _.Yield _ =
        {
            Id = String.Empty
            PartitionKey = PartitionKey.None
            RequestOptions = PatchItemRequestOptions (EnableContentResponseOnWrite = enableContentResponseOnWrite)
            Update =
                fun _ ->
                    raise
                    <| MissingMethodException ("Update function is not set for concurrent patch operation")
        }
        : PatchConcurrentlyOperation<'T, 'E>

    /// Sets the Id of an item being patched
    [<CustomOperation "id">]
    member _.Id (state : PatchConcurrentlyOperation<_, _>, id) = { state with Id = id }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : PatchConcurrentlyOperation<_, _>, partitionKey : PartitionKey) = {
        state with
            PartitionKey = partitionKey
    }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : PatchConcurrentlyOperation<_, _>, partitionKey : string) = {
        state with
            PartitionKey = PartitionKey partitionKey
    }

    /// Sets the request options
    [<CustomOperation "requestOptions">]
    member _.RequestOptions (state : PatchConcurrentlyOperation<_, _>, options : PatchItemRequestOptions) =
        options.EnableContentResponseOnWrite <- state.RequestOptions.EnableContentResponseOnWrite
        { state with RequestOptions = options }

    /// Sets the function that computes patch operations from the current item
    [<CustomOperation "update">]
    member _.Update (state : PatchConcurrentlyOperation<_, _>, update : 'T -> Task<Result<PatchOperation list, 'E>>) = {
        state with
            Update = update
    }

    // ------------------------------------------- Patch request options -------------------------------------------
    /// Sets the filter predicate
    [<CustomOperation "filterPredicate">]
    member _.FilterPredicate (state : PatchConcurrentlyOperation<_, _>, filterPredicate : string) =
        state.RequestOptions.FilterPredicate <- filterPredicate
        state

    // ------------------------------------------- Request options -------------------------------------------
    /// <summary>Sets the operation <see cref="ConsistencyLevel"/></summary>
    [<CustomOperation "consistencyLevel">]
    member _.ConsistencyLevel (state : PatchConcurrentlyOperation<_, _>, consistencyLevel : ConsistencyLevel Nullable) =
        state.RequestOptions.ConsistencyLevel <- consistencyLevel
        state

    /// Sets if the response should include the content of the item after the operation
    [<CustomOperation "enableContentResponseOnWrite">]
    member _.EnableContentResponseOnWrite (state : PatchConcurrentlyOperation<_, _>, enableContentResponseOnWrite : bool) =
        state.RequestOptions.EnableContentResponseOnWrite <- enableContentResponseOnWrite
        state

    /// Sets the indexing directive
    [<CustomOperation "indexingDirective">]
    member _.IndexingDirective (state : PatchConcurrentlyOperation<_, _>, indexingDirective : IndexingDirective Nullable) =
        state.RequestOptions.IndexingDirective <- indexingDirective
        state

    /// Adds a trigger to be invoked before the operation
    [<CustomOperation "preTrigger">]
    member _.PreTrigger (state : PatchConcurrentlyOperation<_, _>, trigger : string) =
        state.RequestOptions.AddPreTrigger trigger
        state

    /// Adds triggers to be invoked before the operation
    [<CustomOperation "preTriggers">]
    member _.PreTriggers (state : PatchConcurrentlyOperation<_, _>, triggers : seq<string>) =
        state.RequestOptions.AddPreTriggers triggers
        state

    /// Adds a trigger to be invoked after the operation
    [<CustomOperation "postTrigger">]
    member _.PostTrigger (state : PatchConcurrentlyOperation<_, _>, trigger : string) =
        state.RequestOptions.AddPostTrigger trigger
        state

    /// Adds triggers to be invoked after the operation
    [<CustomOperation "postTriggers">]
    member _.PostTriggers (state : PatchConcurrentlyOperation<_, _>, triggers : seq<string>) =
        state.RequestOptions.AddPostTriggers triggers
        state

    /// Sets the session token
    [<CustomOperation "sessionToken">]
    member _.SessionToken (state : PatchConcurrentlyOperation<_, _>, sessionToken : string) =
        state.RequestOptions.SessionToken <- sessionToken
        state

let patch<'T> = PatchBuilder<'T>(false)
let patchAndRead<'T> = PatchBuilder<'T>(true)

let patchConcurrenly<'T, 'E> = PatchConcurrentlyBuilder<'T, 'E>(false)
let patchConcurrenlyAndRead<'T, 'E> = PatchConcurrentlyBuilder<'T, 'E>(true)

// https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb

/// Represents the result of a patch operation.
type PatchResult<'t> =
    | Ok of 't // 200
    | BadRequest of ResponseBody : string // 400
    | NotFound of ResponseBody : string // 404
    /// Precondition failed
    | ModifiedBefore of ResponseBody : string // 412 - need re-do
    | TooManyRequests of ResponseBody : string * RetryAfter : TimeSpan voption // 429

/// Represents the result of a concurrent patch operation.
type PatchConcurrentResult<'T, 'E> =
    | Ok of 'T // 200
    | BadRequest of ResponseBody : string // 400
    | NotFound of ResponseBody : string // 404
    /// Precondition failed
    | ModifiedBefore of ResponseBody : string // 412 - need re-do
    | TooManyRequests of ResponseBody : string * RetryAfter : TimeSpan voption // 429
    | CustomError of Error : 'E

open System.Net

module CosmosException =

    let toPatchResult (ex : CosmosException) =
        match ex.StatusCode with
        | HttpStatusCode.BadRequest -> PatchResult.BadRequest ex.ResponseBody
        | HttpStatusCode.NotFound -> PatchResult.NotFound ex.ResponseBody
        | HttpStatusCode.PreconditionFailed -> PatchResult.ModifiedBefore ex.ResponseBody
        | HttpStatusCode.TooManyRequests -> PatchResult.TooManyRequests (ex.ResponseBody, ex.RetryAfter |> ValueOption.ofNullable)
        | _ -> raise ex

    let toPatchConcurrentlyErrorResult (ex : CosmosException) =
        match ex.StatusCode with
        | HttpStatusCode.BadRequest -> PatchConcurrentResult.BadRequest ex.ResponseBody
        | HttpStatusCode.NotFound -> PatchConcurrentResult.NotFound ex.ResponseBody
        | HttpStatusCode.PreconditionFailed -> PatchConcurrentResult.ModifiedBefore ex.ResponseBody
        | HttpStatusCode.TooManyRequests ->
            PatchConcurrentResult.TooManyRequests (ex.ResponseBody, ex.RetryAfter |> ValueOption.ofNullable)
        | _ -> raise ex

open System.Runtime.InteropServices
open CosmosException

let rec executeConcurrentlyAsync<'value, 'error>
    (ct : CancellationToken)
    (container : Container)
    (operation : PatchConcurrentlyOperation<'value, 'error>)
    (retryAttempts : int)
    : Task<CosmosResponse<PatchConcurrentResult<'value, 'error>>> = task {
    try
        let! response =
            container.ReadItemAsync<'value>(operation.Id, operation.PartitionKey, cancellationToken = ct)
        let! patchOperationsResult = operation.Update response.Resource

        match patchOperationsResult with
        | Result.Error e -> return CosmosResponse.fromItemResponse (fun _ -> CustomError e) response
        | Result.Ok patchOperations ->
            // Unlike replace, start from the builder's own options instead of fresh ones, so that
            // filterPredicate, triggers and EnableContentResponseOnWrite (patchConcurrenlyAndRead) stay effective.
            // Each attempt works on a copy: the options object belongs to the caller, who may reuse the same
            // operation later or run it concurrently, so the per-attempt eTag must never be written back into it,
            // and the SDK keeps reading the options while it builds the request.
            let attemptOptions = operation.RequestOptions.ShallowCopy () :?> PatchItemRequestOptions
            attemptOptions.IfMatchEtag <- response.ETag

            let! response =
                container.PatchItemAsync<'value>(
                    operation.Id,
                    operation.PartitionKey,
                    patchOperations.ToImmutableList (),
                    attemptOptions,
                    cancellationToken = ct
                )

            return CosmosResponse.fromItemResponse Ok response
    with
    // Any count at or below the last attempt is exhausted, so a non-positive count passed to this public function
    // stops after one attempt instead of decrementing forever while the item keeps failing the precondition.
    | HandleException ex when
        ex.StatusCode = HttpStatusCode.PreconditionFailed
        && retryAttempts <= 1
        ->
        return CosmosResponse.fromException toPatchConcurrentlyErrorResult ex
    | HandleException ex when ex.StatusCode = HttpStatusCode.PreconditionFailed ->
        return! executeConcurrentlyAsync ct container operation (retryAttempts - 1)
    | HandleException ex -> return CosmosResponse.fromException toPatchConcurrentlyErrorResult ex
}

type Microsoft.Azure.Cosmos.Container with

    /// <summary>
    /// Executes a patch operation and returns <see cref="ItemResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    member container.PlainExecuteAsync<'T> (operation : PatchOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        container.PatchItemAsync<'T>(
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
        = task {
        try
            let! response = container.PlainExecuteAsync<'T>(operation, cancellationToken)
            return CosmosResponse.fromItemResponse success response
        with HandleException ex ->
            return CosmosResponse.fromException failure ex
    }

    /// <summary>
    /// Executes a patch operation safely and returns <see cref="CosmosResponse{PatchResult{T}}"/>.
    /// <para>
    /// Requires ETag to be set in <see cref="PatchItemRequestOptions"/>.
    /// </para>
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

    /// <summary>
    /// Executes a patch operation by computing patch operations from the current item
    /// and returns <see cref="CosmosResponse{PatchConcurrentResult{T, E}}"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="maxRetryCount">Max retry count. Must be greater than zero. Default is 10.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRetryCount"/> is less than one.</exception>
    member container.ExecuteConcurrentlyAsync<'T, 'E>
        (
            operation : PatchConcurrentlyOperation<'T, 'E>,
            [<Optional; DefaultParameterValue(DefaultRetryCount)>] maxRetryCount : int,
            [<Optional>] cancellationToken : CancellationToken
        )
        =
        if maxRetryCount < 1 then
            raise (
                ArgumentOutOfRangeException (nameof maxRetryCount, maxRetryCount, "Max retry count must be greater than zero.")
            )

        executeConcurrentlyAsync<'T, 'E> cancellationToken container operation maxRetryCount

    /// <summary>
    /// Executes a patch operation by computing patch operations from the current item
    /// and returns <see cref="CosmosResponse{PatchConcurrentResult{T, E}}"/>.
    /// </summary>
    /// <param name="operation">Patch operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    member container.ExecuteConcurrentlyAsync<'T, 'E>
        (operation : PatchConcurrentlyOperation<'T, 'E>, [<Optional>] cancellationToken : CancellationToken)
        =
        executeConcurrentlyAsync<'T, 'E> cancellationToken container operation DefaultRetryCount
