namespace Microsoft.Azure.Cosmos

open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Microsoft.Azure.Cosmos

/// <summary>
/// Enumerates the items of every page of a <see cref="FeedIterator{T}" />.
/// </summary>
/// <remarks>
/// Implemented by hand rather than with a <c>taskSeq { }</c> computation expression: <c>taskSeq</c> has no
/// dynamic implementation, so it throws <see cref="NotImplementedException" /> whenever the compiler does not
/// turn it into a static state machine, which is the case for assemblies built without optimizations (Debug).
/// </remarks>
[<Sealed>]
type internal FeedIteratorAsyncEnumerator<'T> (iterator : FeedIterator<'T>, cancellationToken : CancellationToken) =

    let mutable page : IEnumerator<'T> voption = ValueNone
    let mutable current = Unchecked.defaultof<'T>

    let disposePage () =
        page |> ValueOption.iter _.Dispose()
        page <- ValueNone

    interface IAsyncEnumerator<'T> with

        /// <inheritdoc />
        member _.Current = current

        /// <inheritdoc />
        member _.MoveNextAsync () =
            let moveNext = task {
                let mutable found = false
                let mutable exhausted = false

                while not (found || exhausted) do
                    match page with
                    | ValueSome items when items.MoveNext () ->
                        cancellationToken.ThrowIfCancellationRequested ()
                        current <- items.Current
                        found <- true
                    | _ when iterator.HasMoreResults ->
                        disposePage ()
                        let! response = iterator.ReadNextAsync cancellationToken
                        page <- ValueSome (response.GetEnumerator ())
                    | _ -> exhausted <- true

                return found
            }

            ValueTask<bool>(moveNext)

        /// <inheritdoc />
        member _.DisposeAsync () =
            disposePage ()
            ValueTask.CompletedTask

/// <summary>
/// Wraps a <see cref="FeedIterator{T}" /> as an <see cref="IAsyncEnumerable{T}" />.
/// </summary>
/// <remarks>
/// Each call to <see cref="GetAsyncEnumerator" /> creates a fresh <see cref="FeedIteratorAsyncEnumerator{T}" />
/// over the same underlying iterator, so re-enumerating continues from wherever that iterator currently is
/// rather than restarting it.
/// </remarks>
[<Sealed>]
type internal FeedIteratorAsyncEnumerable<'T> (iterator : FeedIterator<'T>, cancellationToken : CancellationToken) =

    interface IAsyncEnumerable<'T> with

        /// <summary>
        /// Creates the enumerator, resolving which cancellation token it observes.
        /// </summary>
        /// <remarks>
        /// Mirrors the <see cref="EnumeratorCancellationAttribute" /> contract the previous <c>taskSeq { }</c>
        /// implementation got from the compiler for free: <paramref name="enumeratorCancellationToken" /> -
        /// typically supplied by
        /// <see cref="System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation" /> on an
        /// <c>await foreach</c> - is honored only when the caller of <see cref="AsAsyncEnumerable" /> left its
        /// own token at the default, so an explicit token passed there always wins.
        /// </remarks>
        /// <param name="enumeratorCancellationToken">
        /// The token supplied to <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator" />, typically via
        /// <see cref="System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation" />.
        /// </param>
        member _.GetAsyncEnumerator (enumeratorCancellationToken : CancellationToken) =
            let effectiveCancellationToken =
                if cancellationToken = CancellationToken.None then
                    enumeratorCancellationToken
                else
                    cancellationToken

            new FeedIteratorAsyncEnumerator<'T> (iterator, effectiveCancellationToken)

[<AutoOpen>]
module FeedIteratorExtensions =

    // See https://github.com/Azure/azure-cosmos-dotnet-v3/issues/903
    type FeedIterator<'T> with

        /// <summary>
        /// Converts the iterator to an async sequence of items, spanning every page.
        /// </summary>
        /// <param name="cancellationToken">
        /// Checked between items and honored by <see cref="FeedIteratorAsyncEnumerable{T}.GetAsyncEnumerator" />
        /// only when it is not the default - see <see cref="FeedIteratorAsyncEnumerable{T}" /> for what happens
        /// when a token is supplied to <see cref="FeedIteratorAsyncEnumerable{T}.GetAsyncEnumerator" /> instead
        /// (for example via <see cref="System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation" />).
        /// </param>
        member iterator.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) =
            FeedIteratorAsyncEnumerable (iterator, cancellationToken) :> IAsyncEnumerable<_>

open System.Linq
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq

[<AutoOpen>]
module QueryableExtensions =

    type IQueryable<'T> with

        /// <summary>
        /// Executes the query and converts the resulting <see cref="FeedIterator{T}" /> to an async sequence of
        /// items.
        /// </summary>
        /// <param name="cancellationToken">Forwarded to <see cref="FeedIterator{T}.AsAsyncEnumerable" />.</param>
        member inline query.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) =
            query.ToFeedIterator().AsAsyncEnumerable<'T>(cancellationToken)
