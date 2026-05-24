namespace FSharp.Azure.Cosmos

open System
open System.Net
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open Microsoft.Azure.Cosmos

module internal RequestOptions =

    let internal createOrUpdate setter requestOptions =
        let options =
            match requestOptions with
            | ValueSome options -> options
            | ValueNone -> ItemRequestOptions ()
        setter options
        options


[<AutoOpen>]
module Operations =

    let internal canHandleStatusCode statusCode =
        match statusCode with
        | HttpStatusCode.BadRequest
        | HttpStatusCode.NotFound
        | HttpStatusCode.Conflict
        | HttpStatusCode.PreconditionFailed
        | HttpStatusCode.RequestEntityTooLarge
        | HttpStatusCode.TooManyRequests -> true
        | _ -> false

    let internal unwrapCosmosException (ex : Exception) =
        match ex with
        | :? CosmosException as ex -> ValueSome ex
        | :? AggregateException as ex ->
            match ex.InnerException with
            | :? CosmosException as cex -> ValueSome cex
            | _ -> ValueNone
        | _ -> ValueNone

    let internal handleException (ex : Exception) =
        let cosmosException = unwrapCosmosException ex
        match cosmosException with
        | ValueSome ex when canHandleStatusCode ex.StatusCode -> ValueSome ex
        | _ -> ValueNone

    [<return : Struct>]
    let (|CosmosException|_|) (ex : Exception) = unwrapCosmosException ex

    [<return : Struct>]
    let (|HandleException|_|) (ex : Exception) = handleException ex

    let internal retryUpdate toErrorResult executeConcurrentlyAsync maxRetryCount currentAttemptCount (e : CosmosException) =
        match e.StatusCode with
        | HttpStatusCode.PreconditionFailed when currentAttemptCount >= maxRetryCount ->
            CosmosResponse.fromException toErrorResult e |> async.Return
        | HttpStatusCode.PreconditionFailed -> executeConcurrentlyAsync maxRetryCount (currentAttemptCount + 1)
        | _ -> CosmosResponse.fromException toErrorResult e |> async.Return

    let internal getRequestOptionsWithMaxItemCount1 requestOptions =
        requestOptions
        |> ValueOption.ofObj
        |> ValueOption.defaultWith QueryRequestOptions
        |> fun o ->
            o.MaxItemCount <- 1
            o

    type ItemRequestOptions with

        /// <summary>
        /// Adds a pre-trigger to request options.
        /// </summary>
        /// <param name="trigger">Trigger name.</param>
        member options.AddPreTrigger (trigger : string) =
            options.PreTriggers <- [|
                if not <| isNull options.PreTriggers then
                    yield! options.PreTriggers
                yield trigger
            |]

        /// <summary>
        /// Adds pre-triggers to request options.
        /// </summary>
        /// <param name="triggers">Trigger names.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="triggers"/> is <c>null</c>.</exception>
        member options.AddPreTriggers (triggers : string seq) =
            if obj.ReferenceEquals (triggers, null) then
                raise (ArgumentNullException (nameof triggers))
            options.PreTriggers <- [|
                if not <| isNull options.PreTriggers then
                    yield! options.PreTriggers
                yield! triggers
            |]

        member options.AddPostTrigger (trigger : string) =
            options.PostTriggers <- [|
                if not <| isNull options.PostTriggers then
                    yield! options.PostTriggers
                yield trigger
            |]

        /// <summary>
        /// Adds post-triggers to request options.
        /// </summary>
        /// <param name="triggers">Trigger names.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="triggers"/> is <c>null</c>.</exception>
        member options.AddPostTriggers (triggers : string seq) =
            if obj.ReferenceEquals (triggers, null) then
                raise (ArgumentNullException (nameof triggers))
            options.PostTriggers <- [| yield! options.PostTriggers; yield! triggers |]

    let internal countQuery = QueryDefinition ("SELECT VALUE COUNT(1) FROM c")
    let internal existsQuery = QueryDefinition ("SELECT VALUE COUNT(1) FROM item WHERE item.id = @Id")
    let internal getExistsQuery id = existsQuery.WithParameter ("@Id", id)

    type Microsoft.Azure.Cosmos.Container with

        /// <summary>
        /// Counts the number of items in the container with specified <see cref="QueryRequestOptions"/>.
        /// </summary>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.CountAsync (requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken) =
            container.GetItemQueryIterator<int> (countQuery, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
            |> CancellableTaskSeq.ofFeedIterator cancellationToken
            |> TaskSeq.tryHead
            |> Task.map (Option.defaultValue 0)

        /// <summary>
        /// Counts the number of items in the container partition with specified key.
        /// <para>
        /// If no partition key is provided, the count will be for the entire container.
        /// </para>
        /// </summary>
        /// <param name="partitionKey">Partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.CountAsync (partitionKey, [<Optional>] cancellationToken : CancellationToken) =
            container.CountAsync (QueryRequestOptions (PartitionKey = partitionKey), cancellationToken)

        /// <summary>
        /// Counts the number of items in the container partition with specified key.
        /// <para>
        /// If no partition key is provided, the count will be for the entire container.
        /// </para>
        /// </summary>
        /// <param name="partitionKey">Partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.CountAsync (partitionKey : string, [<Optional>] cancellationToken : CancellationToken) =
            if String.IsNullOrEmpty partitionKey then
                container.CountAsync (PartitionKey.None, cancellationToken = cancellationToken)
            else
                container.CountAsync (PartitionKey partitionKey, cancellationToken)

        /// <summary>
        /// Counts the number of items in the container with specified <see cref="QueryRequestOptions"/>.
        /// </summary>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.LongCountAsync
            (requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken)
            =
            container.GetItemQueryIterator<int64> (countQuery, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
            |> CancellableTaskSeq.ofFeedIterator cancellationToken
            |> TaskSeq.tryHead
            |> Task.map (Option.defaultValue 0)

        /// <summary>
        /// Counts the number of items in the container partition with specified key.
        /// <para>
        /// If no partition key is provided, the count will be for the entire container.
        /// </para>
        /// </summary>
        /// <param name="partitionKey">Partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.LongCountAsync (partitionKey, [<Optional>] cancellationToken : CancellationToken) =
            container.LongCountAsync (QueryRequestOptions (PartitionKey = partitionKey), cancellationToken)

        /// <summary>
        /// Counts the number of items in the container partition with specified key.
        /// <para>
        /// If no partition key is provided, the count will be for the entire container.
        /// </para>
        /// </summary>
        /// <param name="partitionKey">Partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.LongCountAsync (partitionKey : string, [<Optional>] cancellationToken : CancellationToken) =
            container.LongCountAsync (PartitionKey partitionKey, cancellationToken)

        /// <summary>
        /// Checks if an item with specified Id exists in the container.
        /// </summary>
        /// <param name="id">Item Id</param>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.ExistsAsync
            (id : string, [<Optional>] requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken)
            =
            task {
                let query = getExistsQuery id
                let! count =
                    container.GetItemQueryIterator<int> (
                        query,
                        requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions
                    )
                    |> CancellableTaskSeq.ofFeedIterator cancellationToken
                    |> TaskSeq.tryHead
                    |> Task.map (Option.defaultValue 0)
                return count = 1
            }

        /// <summary>
        /// Checks if an item with specified Id exists in the container partition with specified key.
        /// </summary>
        /// <param name="id">Item Id</param>
        /// <param name="partitionKey">Partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.ExistsAsync
            (id : string, partitionKey : PartitionKey, [<Optional>] cancellationToken : CancellationToken)
            =
            container.ExistsAsync (id, QueryRequestOptions (PartitionKey = partitionKey), cancellationToken)

        /// <summary>
        /// Checks if an item with specified Id exists in the container partition with specified key.
        /// </summary>
        /// <param name="deletedFieldName">Deleted marker field name.</param>
        /// <param name="id">Item Id</param>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deletedFieldName"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="deletedFieldName"/> does not start with a letter or underscore,
        /// or contains characters other than letters, digits, or underscores.
        /// </exception>
        member container.IsNotDeletedAsync
            (deletedFieldName : string)
            (id : string, [<Optional>] requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken)
            =
            if obj.ReferenceEquals (deletedFieldName, null) then
                nullArg (nameof deletedFieldName)

            task {
                let isAsciiLetter c = ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z')
                let isAsciiDigit c = '0' <= c && c <= '9'

                let isValidDeletedFieldName =
                    if String.IsNullOrWhiteSpace deletedFieldName then
                        false
                    else
                        let firstCharacter = deletedFieldName[0]
                        let hasValidStart = firstCharacter = '_' || isAsciiLetter firstCharacter
                        let hasValidBody =
                            deletedFieldName
                            |> Seq.forall (fun c -> c = '_' || isAsciiLetter c || isAsciiDigit c)

                        hasValidStart && hasValidBody

                if not isValidDeletedFieldName then
                    invalidArg
                        (nameof deletedFieldName)
                        "Deleted field name must start with a letter or underscore and contain only letters, digits, or underscores."

                let query =
                    QueryDefinition(
                        $"""SELECT VALUE COUNT(1)
                          FROM item
                          WHERE item.id = @Id
                          AND (NOT IS_DEFINED(item.{deletedFieldName}) OR IS_NULL(item.{deletedFieldName}))"""
                    )
                        .WithParameter ("@Id", id)
                let! count =
                    container.GetItemQueryIterator<int> (
                        query,
                        requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions
                    )
                    |> CancellableTaskSeq.ofFeedIterator cancellationToken
                    |> TaskSeq.tryHead
                    |> Task.map (Option.defaultValue 0)
                return count = 1
            }
