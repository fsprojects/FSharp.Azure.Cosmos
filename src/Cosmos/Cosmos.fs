namespace FSharp.Azure.Cosmos

open System
open System.Net
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open Microsoft.Azure.Cosmos

/// <summary>
/// Helpers for validating Cosmos DB item field names used in dynamically constructed queries.
/// </summary>
module CosmosName =

    let private isAsciiLetter c = ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z')
    let private isAsciiDigit c = '0' <= c && c <= '9'

    /// <summary>
    /// Validates that <paramref name="fieldName"/> is a syntactically valid Cosmos DB item field name:
    /// non-null, non-empty, starting with a letter or underscore, and containing only letters, digits,
    /// or underscores.
    /// </summary>
    /// <param name="paramName">Name of the caller's parameter to report in a thrown exception.</param>
    /// <param name="fieldName">Field name to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fieldName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldName"/> does not start with a letter or underscore,
    /// or contains characters other than letters, digits, or underscores.
    /// </exception>
    [<CompiledName "ValidateField">]
    let validateField (paramName : string) (fieldName : string) =
        if obj.ReferenceEquals (fieldName, null) then
            nullArg paramName

        let isValidFieldName =
            if String.IsNullOrWhiteSpace fieldName then
                false
            else
                let firstCharacter = fieldName[0]
                let hasValidStart = firstCharacter = '_' || isAsciiLetter firstCharacter
                let hasValidBody =
                    fieldName
                    |> Seq.forall (fun c -> c = '_' || isAsciiLetter c || isAsciiDigit c)

                hasValidStart && hasValidBody

        if not isValidFieldName then
            invalidArg
                paramName
                "Field name must start with a letter or underscore and contain only letters, digits, or underscores."

module internal RequestOptions =

    let internal createOrUpdate setter requestOptions =
        let options =
            match requestOptions with
            | ValueSome options -> options
            | ValueNone -> ItemRequestOptions ()
        setter options
        options

/// <summary>
/// Extensions for <see cref="ResponseMessage"/>, the response of the SDK's stream APIs.
/// </summary>
// ModuleSuffix: without it the module compiles to FSharp.Azure.Cosmos.ResponseMessage, which makes every
// ResponseMessage in C# code opening both namespaces ambiguous with Microsoft.Azure.Cosmos.ResponseMessage.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ResponseMessage =

    /// <summary>
    /// Gets the Cosmos DB sub-status code of a stream response, which
    /// <see cref="ResponseMessage"/> does not expose, unlike <see cref="CosmosException.SubStatusCode"/>.
    /// <para>
    /// The sub-status tells apart responses that share a status code: for example, a 404 without one is
    /// a missing item, while a 404 with <see cref="SubStatusCodes.OwnerResourceNotFound"/> is a missing
    /// database or container. The known values are in <see cref="SubStatusCodes"/>.
    /// </para>
    /// </summary>
    /// <param name="response">Stream response</param>
    /// <returns>The sub-status code, or 0 when the response has none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is <see langword="null"/>.</exception>
    [<CompiledName "GetSubStatusCode">]
    let getSubStatusCode (response : ResponseMessage) =
        ArgumentNullException.ThrowIfNull response
        match response.Headers["x-ms-substatus"] with
        | null -> SubStatusCodes.Unknown
        | value ->
            match Int32.TryParse value with
            | true, subStatusCode -> subStatusCode
            | false, _ -> SubStatusCodes.Unknown

[<AutoOpen>]
module Operations =

    open ResponseMessage

    type ResponseMessage with

        /// <summary>
        /// Gets the Cosmos DB sub-status code of the response, or 0 when the response has none.
        /// <para>
        /// C# callers use <see cref="ResponseMessageModule.GetSubStatusCode"/> instead,
        /// since F# extension properties are not visible to C#.
        /// </para>
        /// </summary>
        member response.SubStatusCode = getSubStatusCode response

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
                match options.PreTriggers with
                | null -> ()
                | existing -> yield! existing
                yield trigger
            |]

        /// <summary>
        /// Adds pre-triggers to request options.
        /// </summary>
        /// <param name="triggers">Trigger names.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="triggers"/> is <see langword="null"/>.</exception>
        member options.AddPreTriggers (triggers : string seq) =
            if obj.ReferenceEquals (triggers, null) then
                raise (ArgumentNullException (nameof triggers))
            options.PreTriggers <- [|
                match options.PreTriggers with
                | null -> ()
                | existing -> yield! existing
                yield! triggers
            |]

        member options.AddPostTrigger (trigger : string) =
            options.PostTriggers <- [|
                match options.PostTriggers with
                | null -> ()
                | existing -> yield! existing
                yield trigger
            |]

        /// <summary>
        /// Adds post-triggers to request options.
        /// </summary>
        /// <param name="triggers">Trigger names.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="triggers"/> is <see langword="null"/>.</exception>
        member options.AddPostTriggers (triggers : string seq) =
            if obj.ReferenceEquals (triggers, null) then
                raise (ArgumentNullException (nameof triggers))
            options.PostTriggers <- [|
                match options.PostTriggers with
                | null -> ()
                | existing -> yield! existing
                yield! triggers
            |]

    let internal countQuery = QueryDefinition ("SELECT VALUE COUNT(1) FROM c")
    // A new definition per call: QueryDefinition.WithParameter mutates the instance and returns it, so a shared
    // definition lets concurrent calls overwrite each other's @Id and count a different item.
    let internal getExistsQuery (id : string) =
        QueryDefinition("SELECT VALUE COUNT(1) FROM item WHERE item.id = @Id").WithParameter("@Id", id)

    /// Reads the sub-status code of a stream response; 0 when the response has none, as for a missing item.
    let internal getSubStatusCode (response : ResponseMessage) =
        match response.Headers["x-ms-substatus"] with
        | null -> 0
        | value ->
            match Int32.TryParse value with
            | true, subStatusCode -> subStatusCode
            | false, _ -> 0

    type Microsoft.Azure.Cosmos.Container with

        /// <summary>
        /// Counts the number of items in the container with specified <see cref="QueryRequestOptions"/>.
        /// </summary>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.CountAsync (requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken) =
            container.GetItemQueryIterator<int>(countQuery, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
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
            container.GetItemQueryIterator<int64>(countQuery, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
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
        /// <para>
        /// Without a <paramref name="requestOptions"/> partition key, the query spans every partition: the
        /// same Id can exist in more than one logical partition, so any positive count is treated as a match.
        /// </para>
        /// </summary>
        /// <param name="id">Item Id</param>
        /// <param name="requestOptions">Request options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        member container.ExistsAsync
            (id : string, [<Optional>] requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken)
            = task {
            let query = getExistsQuery id
            let! count =
                container.GetItemQueryIterator<int>(query, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
                |> CancellableTaskSeq.ofFeedIterator cancellationToken
                |> TaskSeq.tryHead
                |> Task.map (Option.defaultValue 0)
            return count > 0
        }

        /// <summary>
        /// Checks if an item with specified Id exists in the container partition with specified key.
        /// <para>
        /// A full partition key and an Id identify at most one item, so the check is a point read: it is exact
        /// and costs a single request unit.
        /// </para>
        /// <para>
        /// A prefix of a hierarchical partition key cannot be point-read; the service rejects it as a bad request,
        /// and the check falls back to a query scoped to that prefix, matching the Id anywhere beneath it.
        /// </para>
        /// </summary>
        /// <param name="id">Item Id</param>
        /// <param name="partitionKey">Full partition key, or a prefix of a hierarchical partition key</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="CosmosException">
        /// Thrown when the check fails for a reason other than the item not existing, for example throttling,
        /// authorization or a missing container.
        /// </exception>
        member container.ExistsAsync
            (id : string, partitionKey : PartitionKey, [<Optional>] cancellationToken : CancellationToken)
            = task {
            use! response = container.ReadItemStreamAsync (id, partitionKey, cancellationToken = cancellationToken)
            match response.StatusCode, getSubStatusCode response with
            | HttpStatusCode.NotFound, 0 -> return false
            | HttpStatusCode.BadRequest, _ ->
                // A prefix of a hierarchical key is rejected as a bad request, but the sub-status differs between
                // backends (1001 from the service and the Windows emulator, 0 from the Linux vNext emulator), so any
                // 400 falls back to the query that preceded the point read. A request that is really invalid fails
                // there too and is propagated.
                return! container.ExistsAsync (id, QueryRequestOptions (PartitionKey = partitionKey), cancellationToken)
            | _ ->
                // Any other failure (throttling, auth, a missing container reported as 404 with a sub-status)
                // must not be reported as a missing item
                response.EnsureSuccessStatusCode () |> ignore
                return true
        }

        /// <summary>
        /// Checks whether an item with the specified Id exists and is not marked as deleted.
        /// <para>
        /// The item is treated as not deleted when the <paramref name="deletedFieldName"/> field is absent,
        /// <see langword="null"/>, or <c>false</c>. Any other value, such as <c>true</c> or a deletion timestamp,
        /// marks the item as deleted.
        /// </para>
        /// </summary>
        /// <param name="deletedFieldName">Name of the item field that marks the item as deleted.</param>
        /// <param name="id">Item Id</param>
        /// <param name="requestOptions">Query request options, for example to scope the query to a partition key.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns><c>true</c> when the item exists and is not marked as deleted; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deletedFieldName"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="deletedFieldName"/> does not start with a letter or underscore,
        /// or contains characters other than letters, digits, or underscores.
        /// </exception>
        member container.IsNotDeletedAsync
            (deletedFieldName : string)
            (id : string, [<Optional>] requestOptions : QueryRequestOptions, [<Optional>] cancellationToken : CancellationToken)
            =
            CosmosName.validateField (nameof deletedFieldName) deletedFieldName

            task {
                // Bracket notation, not item.{deletedFieldName}: a validated field name can still be a reserved
                // Cosmos SQL keyword (e.g. "value"), which dot notation would turn into an invalid query.
                let query =
                    QueryDefinition(
                        $"SELECT VALUE COUNT(1) \
                         FROM item \
                         WHERE item.id = @Id \
                         AND (NOT IS_DEFINED(item[\"{deletedFieldName}\"]) OR IS_NULL(item[\"{deletedFieldName}\"]) OR item[\"{deletedFieldName}\"] = false)"
                    )
                        .WithParameter("@Id", id)
                let! count =
                    container.GetItemQueryIterator<int>(query, requestOptions = getRequestOptionsWithMaxItemCount1 requestOptions)
                    |> CancellableTaskSeq.ofFeedIterator cancellationToken
                    |> TaskSeq.tryHead
                    |> Task.map (Option.defaultValue 0)
                return count > 0
            }
