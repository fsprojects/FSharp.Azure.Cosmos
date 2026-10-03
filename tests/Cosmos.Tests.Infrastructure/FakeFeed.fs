namespace FSharp.Azure.Cosmos.Tests

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open Microsoft.Azure.Cosmos

/// <summary>
/// A <see cref="FeedResponse{T}"/> fake implementing the members <see cref="Response{T}"/> and
/// <see cref="FeedResponse{T}"/> declare abstract; only <see cref="FeedResponse{T}.GetEnumerator"/> is actually read
/// by <see cref="Microsoft.Azure.Cosmos.FeedIteratorAsyncEnumerator{T}"/>, the rest exist to satisfy the base classes.
/// </summary>
type FakeFeedResponse<'T> (items : 'T list) =
    inherit FeedResponse<'T> ()

    /// <inheritdoc />
    override _.Count = items.Length

    /// <inheritdoc />
    override _.ContinuationToken = null

    /// <inheritdoc />
    override _.IndexMetrics = null

    /// <inheritdoc />
    override _.GetEnumerator () = (items :> 'T seq).GetEnumerator()

    /// <inheritdoc />
    override _.Headers = Headers ()

    /// <inheritdoc />
    override _.Resource = items :> 'T seq

    /// <inheritdoc />
    override _.StatusCode = HttpStatusCode.OK

    /// <inheritdoc />
    override _.Diagnostics = Unchecked.defaultof<CosmosDiagnostics>

/// <summary>
/// A <see cref="FeedIterator{T}"/> fake that serves a fixed sequence of pages without a Cosmos DB connection.
/// </summary>
type FakeFeedIterator<'T> (pages : 'T list list) =
    inherit FeedIterator<'T> ()

    let mutable remainingPages = pages

    /// <summary>
    /// How many times <see cref="FeedIterator{T}.ReadNextAsync"/> has been called.
    /// </summary>
    member val ReadNextCallCount = 0 with get, set

    /// <inheritdoc />
    override _.HasMoreResults = not remainingPages.IsEmpty

    /// <inheritdoc />
    override this.ReadNextAsync (cancellationToken : CancellationToken) =
        cancellationToken.ThrowIfCancellationRequested ()
        this.ReadNextCallCount <- this.ReadNextCallCount + 1

        match remainingPages with
        | [] -> raise (InvalidOperationException "ReadNextAsync called with no pages remaining.")
        | page :: rest ->
            remainingPages <- rest
            Task.FromResult (FakeFeedResponse<'T>(page) :> FeedResponse<'T>)
