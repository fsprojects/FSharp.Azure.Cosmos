namespace Microsoft.Azure.Cosmos

open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading
open Microsoft.Azure.Cosmos
open FSharp.Control

[<AutoOpen>]
module FeedIteratorExtensions =

    // See https://github.com/Azure/azure-cosmos-dotnet-v3/issues/903
    type FeedIterator<'T> with

        /// Converts the iterator to an async sequence of items.
        member iterator.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) = taskSeq {
            while iterator.HasMoreResults do
                let! page = iterator.ReadNextAsync (cancellationToken)

                for item in page do
                    cancellationToken.ThrowIfCancellationRequested ()
                    yield item
        }

open System.Linq
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq

[<AutoOpen>]
module QueryableExtensions =

    type IQueryable<'T> with

        member inline query.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) =
            query.ToFeedIterator().AsAsyncEnumerable<'T> (cancellationToken)
