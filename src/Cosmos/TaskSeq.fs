namespace Microsoft.Azure.Cosmos

open System.Linq
open System.Threading
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq

module TaskSeq =

    /// <summary>
    /// Executes Cosmos DB query and asynchronously iterates Cosmos DB <see cref="FeedIterator{T}" />.
    /// </summary>
    /// <param name="iterator">Cosmos DB feed iterator</param>
    let ofFeedIterator<'T> (iterator : FeedIterator<'T>) = iterator.AsAsyncEnumerable<'T> ()

    /// <summary>
    /// Creates Cosmos DB <see cref="FeedIterator{T}" /> from <see cref="IQueryable{T}" />
    /// and asynchronously iterates it.
    /// </summary>
    /// <param name="query">Cosmos DB queryable</param>
    let ofCosmosDbQueryable<'T> (query : IQueryable<'T>) = query.ToFeedIterator().AsAsyncEnumerable<'T> ()

module CancellableTaskSeq =

    /// <summary>
    /// Executes Cosmos DB query and asynchronously iterates Cosmos DB <see cref="FeedIterator{T}" />.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="iterator">Cosmos DB feed iterator</param>
    let ofFeedIterator<'T> (cancellationToken : CancellationToken) (iterator : FeedIterator<'T>) =
        iterator.AsAsyncEnumerable<'T> (cancellationToken)

    /// <summary>
    /// Creates Cosmos DB <see cref="FeedIterator{T}" /> from <see cref="IQueryable{T}" />
    /// and asynchronously iterates it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="query">Cosmos DB queryable</param>
    let ofCosmosDbQueryable<'T> (cancellationToken : CancellationToken) (query : IQueryable<'T>) =
        query.ToFeedIterator().AsAsyncEnumerable<'T> (cancellationToken)
