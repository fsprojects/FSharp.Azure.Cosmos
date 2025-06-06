﻿[<AutoOpen>]
module FSharp.Azure.Cosmos.Read

open Microsoft.Azure.Cosmos

[<Struct>]
type ReadOperation<'T> = {
    Id : string
    PartitionKey : PartitionKey
    RequestOptions : ItemRequestOptions | null
}

open System

type ReadBuilder<'T> () =
    member _.Yield _ =
        { Id = String.Empty; PartitionKey = PartitionKey.None; RequestOptions = null } : ReadOperation<'T>

    /// Sets the item being created
    [<CustomOperation "id">]
    member _.Id (state : ReadOperation<_>, id) = { state with Id = id }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : ReadOperation<_>, partitionKey : PartitionKey) = { state with PartitionKey = partitionKey }

    /// Sets the partition key
    [<CustomOperation "partitionKey">]
    member _.PartitionKey (state : ReadOperation<_>, partitionKey : string) = {
        state with
            PartitionKey = PartitionKey partitionKey
    }

    /// Sets the request options
    [<CustomOperation "requestOptions">]
    member _.RequestOptions (state : ReadOperation<_>, options : ItemRequestOptions) = { state with RequestOptions = options }

    /// <summary>Sets the eTag to <see cref="ItemRequestOptions.IfNotMatchEtag"/></summary>
    [<CustomOperation "eTag">]
    member _.ETag (state : ReadOperation<_>, eTag : string) =
        match state.RequestOptions with
        | null ->
            let options = ItemRequestOptions (IfNoneMatchEtag = eTag)
            { state with RequestOptions = options }
        | options ->
            options.IfNoneMatchEtag <- eTag
            state

    // ------------------------------------------- Request options -------------------------------------------
    /// <summary>Sets the operation <see cref="ConsistencyLevel"/></summary>
    [<CustomOperation "consistencyLevel">]
    member _.ConsistencyLevel (state : ReadOperation<_>, consistencyLevel : ConsistencyLevel Nullable) =
        match state.RequestOptions with
        | null ->
            let options = ItemRequestOptions (ConsistencyLevel = consistencyLevel)
            { state with RequestOptions = options }
        | options ->
            options.ConsistencyLevel <- consistencyLevel
            state

    /// Sets the indexing directive
    [<CustomOperation "indexingDirective">]
    member _.IndexingDirective (state : ReadOperation<_>, indexingDirective : IndexingDirective Nullable) =
        match state.RequestOptions with
        | null ->
            let options = ItemRequestOptions (IndexingDirective = indexingDirective)
            { state with RequestOptions = options }
        | options ->
            options.IndexingDirective <- indexingDirective
            state

    /// Sets the session token
    [<CustomOperation "sessionToken">]
    member _.SessionToken (state : ReadOperation<_>, sessionToken : string) =
        match state.RequestOptions with
        | null ->
            let options = ItemRequestOptions (SessionToken = sessionToken)
            { state with RequestOptions = options }
        | options ->
            options.SessionToken <- sessionToken
            state

let read<'T> = ReadBuilder<'T> ()

// https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb

/// Represents the result of a read operation.
type ReadResult<'t> =
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
    /// Executes a read operation and returns <see cref="ItemResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.PlainExecuteAsync<'T> (operation : ReadOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        container.ReadItemAsync<'T> (
            operation.Id,
            operation.PartitionKey,
            operation.RequestOptions,
            cancellationToken = cancellationToken
        )

    /// <summary>
    /// Executes a read operation, transforms success or failure, and returns <see cref="CosmosResponse{T}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="success">Result transform if success</param>
    /// <param name="failure">Error transform if failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsync<'T, 'Result>
        (operation : ReadOperation<'T>, success, failure, [<Optional>] cancellationToken : CancellationToken)
        : Task<CosmosResponse<'Result>>
        =
        task {
            try
                let! result = container.PlainExecuteAsync (operation, cancellationToken)
                return CosmosResponse.fromItemResponse (success) result
            with HandleException ex ->
                return CosmosResponse.fromException (failure) ex
        }

    /// <summary>
    /// Executes a read operation and returns <see cref="CosmosResponse{ReadResult{T}}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsync<'T> (operation : ReadOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        let successFn result : ReadResult<'T> =
            if Object.Equals (result, Unchecked.defaultof<'T>) then
                ReadResult.NotModified
            else
                ReadResult.Ok result

        container.ExecuteAsync<'T, ReadResult<'T>> (
            operation,
            successFn,
            toReadResult ReadResult.IncompatibleConsistencyLevel ReadResult.NotFound,
            cancellationToken
        )

    /// <summary>
    /// Executes a read operation and returns <see cref="CosmosResponse{FSharpValueOption{T}}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsyncOption<'T> (operation : ReadOperation<'T>, [<Optional>] cancellationToken : CancellationToken) =
        container.ExecuteAsync<'T, 'T option> (
            operation,
            Some,
            toReadResult (fun message -> raise (invalidOp message)) (fun _ -> None),
            cancellationToken
        )

    /// <summary>
    /// Executes a read operation and returns <see cref="CosmosResponse{FSharpOption{T}}"/>.
    /// </summary>
    /// <param name="operation">Read operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    member container.ExecuteAsyncValueOption<'T>
        (operation : ReadOperation<'T>, [<Optional>] cancellationToken : CancellationToken)
        =
        container.ExecuteAsync<'T, 'T voption> (
            operation,
            ValueSome,
            toReadResult (fun message -> raise (invalidOp message)) (fun _ -> ValueNone),
            cancellationToken
        )

open System.Runtime.CompilerServices

[<AbstractClass; Sealed; Extension>]
type FeedIteratorExtensions private () =

    static let seqToReadResult (items : 'T seq) =
        match items |> Seq.tryHead with
        | Some item -> Ok item
        | None -> NotFound "Item not found"

    [<Extension>]
    static member FirstAsync (iterator : FeedIterator<'T>, [<Optional>] cancellationToken : CancellationToken) = task {
        try
            let! page = iterator.ReadNextAsync cancellationToken
            return CosmosResponse.fromFeedResponse seqToReadResult page
        with HandleException ex ->
            return CosmosResponse.fromException (toReadResult ReadResult.IncompatibleConsistencyLevel ReadResult.NotFound) ex
    }
