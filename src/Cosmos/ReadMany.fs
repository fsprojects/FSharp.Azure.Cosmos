[<AutoOpen>]
module FSharp.Azure.Cosmos.ReadMany

open System
open System.Collections.Immutable
open Microsoft.Azure.Cosmos

[<Struct>]
type ReadManyOperation<'T> = {
    Items : ValueTuple<string, PartitionKey> ImmutableArray
    RequestOptions : ReadManyRequestOptions | null
}

type ReadManyBuilder<'T> () =
    member _.Yield _ = { Items = ImmutableArray.Empty; RequestOptions = null } : ReadManyOperation<'T>

    /// Sets the item being created
    [<CustomOperation "item">]
    member _.Item (state : ReadManyOperation<_>, id, partitionKey : PartitionKey) =
        let items = state.Items.Add (id, partitionKey)
        { state with Items = items }

    /// Sets the item being created
    [<CustomOperation "item">]
    member builder.Item (state : ReadManyOperation<_>, id, partitionKey : string) =
        builder.Item (state, id, PartitionKey partitionKey)

    /// Sets the items being created
    [<CustomOperation "items">]
    member _.Items (state : ReadManyOperation<_>, items : ValueTuple<string, PartitionKey> seq) =
        let items = state.Items.AddRange items
        { state with Items = items }

    /// Sets the items being created
    [<CustomOperation "items">]
    member builder.Items (state : ReadManyOperation<_>, items : ValueTuple<string, string> seq) =
        builder.Items (
            state,
            items
            |> Seq.map (fun struct (id, partitionKey) -> struct (id, PartitionKey partitionKey))
        )

    /// Sets the request options
    [<CustomOperation "requestOptions">]
    member _.RequestOptions (state : ReadManyOperation<_>, options : ReadManyRequestOptions) = {
        state with
            RequestOptions = options
    }

    /// <summary>Sets the eTag to <see cref="ReadManyRequestOptions.IfNoneMatchEtag"/></summary>
    [<CustomOperation "eTag">]
    member _.ETag (state : ReadManyOperation<_>, eTag : string) =
        match state.RequestOptions with
        | null ->
            let options = ReadManyRequestOptions (IfNoneMatchEtag = eTag)
            { state with RequestOptions = options }
        | options ->
            options.IfNoneMatchEtag <- eTag
            state

    // ------------------------------------------- Request options -------------------------------------------
    /// <summary>Sets the operation <see cref="ConsistencyLevel"/></summary>
    [<CustomOperation "consistencyLevel">]
    member _.ConsistencyLevel (state : ReadManyOperation<_>, consistencyLevel : ConsistencyLevel Nullable) =
        match state.RequestOptions with
        | null ->
            let options = ReadManyRequestOptions (ConsistencyLevel = consistencyLevel)
            { state with RequestOptions = options }
        | options ->
            options.ConsistencyLevel <- consistencyLevel
            state

    /// Sets the session token
    [<CustomOperation "sessionToken">]
    member _.SessionToken (state : ReadManyOperation<_>, sessionToken : string) =
        match state.RequestOptions with
        | null ->
            let options = ReadManyRequestOptions (SessionToken = sessionToken)
            { state with RequestOptions = options }
        | options ->
            options.SessionToken <- sessionToken
            state

let readMany<'T> = ReadManyBuilder<'T>()

// https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb

/// Represents the result of a read operation.
type ReadManyResult<'t> =
    | Ok of 't // 200
    | NotModified // 204
    | IncompatibleConsistencyLevel of ResponseBody : string // 400
    | NotFound of ResponseBody : string // 404

open System.Net

module CosmosException =

    let toReadResult badRequestCtor notFoundResultCtor (ex : CosmosException) =
        match ex.StatusCode with
        | HttpStatusCode.BadRequest -> badRequestCtor ex.ResponseBody
        | HttpStatusCode.NotFound -> notFoundResultCtor ex.ResponseBody
        | _ -> raise ex

open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open CosmosException

type Microsoft.Azure.Cosmos.Container with

    /// <summary>
    /// Executes a read many operation and returns <see cref="FeedResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Read many operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.PlainExecuteAsync<'T>
        (operation : ReadManyOperation<'T>, [<Optional>] cancellationToken : CancellationToken)
        =
        container.ReadManyItemsAsync<'T>(operation.Items, operation.RequestOptions, cancellationToken = cancellationToken)

    /// <summary>
    /// Executes a read many operation, transforms success or failure, and returns <see cref="CosmosResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="success">Result transform if success</param>
    /// <param name="failure">Error transform if failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsync<'T, 'Result>
        (operation : ReadManyOperation<'T>, success, failure, [<Optional>] cancellationToken : CancellationToken)
        : Task<CosmosResponse<'Result>>
        = task {
        try
            let! result = container.PlainExecuteAsync (operation, cancellationToken)
            return CosmosResponse.fromFeedResponse (success) result
        with HandleException ex ->
            return CosmosResponse.fromException (failure) ex
    }

    /// <summary>
    /// Executes a read many operation and returns <see cref="CosmosResponse{ReadManyResult{FeedResponse{T}}}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsync<'T>
        (operation : ReadManyOperation<'T>, [<Optional>] cancellationToken : CancellationToken)
        : Task<CosmosResponse<ReadManyResult<FeedResponse<'T>>>>
        = task {
        try
            let! response = container.PlainExecuteAsync (operation, cancellationToken)

            // A matching If-None-Match can come back as a successful 304 feed response...
            if response.StatusCode = HttpStatusCode.NotModified then
                return CosmosResponse.fromFeedResponse (fun _ -> ReadManyResult.NotModified) response
            else
                return CosmosResponse.fromFeedResponse ReadManyResult.Ok response
        with
        // ...or, depending on the SDK transport and emulator, as a thrown 304 CosmosException.
        | CosmosException ex when ex.StatusCode = HttpStatusCode.NotModified ->
            return CosmosResponse.fromException (fun _ -> ReadManyResult.NotModified) ex
        | HandleException ex ->
            return
                CosmosResponse.fromException (toReadResult ReadManyResult.IncompatibleConsistencyLevel ReadManyResult.NotFound) ex
    }
