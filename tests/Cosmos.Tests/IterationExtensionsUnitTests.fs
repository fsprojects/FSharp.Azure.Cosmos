namespace FSharp.Azure.Cosmos.Tests

open System
open System.Collections.Generic
open System.Net
open System.Threading
open System.Threading.Tasks
open FSharp.Control
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

/// A `FeedResponse` fake implementing the members `Response<'T>`/`FeedResponse<'T>` declare abstract; only
/// `GetEnumerator` is actually read by `FeedIteratorAsyncEnumerator`, the rest exist to satisfy the base classes.
type private FakeFeedResponse<'T> (items : 'T list) =
    inherit FeedResponse<'T> ()

    override _.Count = items.Length
    override _.ContinuationToken = null
    override _.IndexMetrics = null
    override _.GetEnumerator () = (items :> 'T seq).GetEnumerator()
    override _.Headers = Headers ()
    override _.Resource = items :> 'T seq
    override _.StatusCode = HttpStatusCode.OK
    override _.Diagnostics = Unchecked.defaultof<CosmosDiagnostics>

/// A `FeedIterator` fake that serves a fixed sequence of pages without a Cosmos DB connection.
type private FakeFeedIterator<'T> (pages : 'T list list) =
    inherit FeedIterator<'T> ()

    let mutable remainingPages = pages

    member val ReadNextCallCount = 0 with get, set

    override _.HasMoreResults = not remainingPages.IsEmpty

    override this.ReadNextAsync (cancellationToken : CancellationToken) =
        cancellationToken.ThrowIfCancellationRequested ()
        this.ReadNextCallCount <- this.ReadNextCallCount + 1

        match remainingPages with
        | [] -> raise (InvalidOperationException "ReadNextAsync called with no pages remaining.")
        | page :: rest ->
            remainingPages <- rest
            Task.FromResult (FakeFeedResponse<'T>(page) :> FeedResponse<'T>)

/// Regression coverage for the hand-written `FeedIterator.AsAsyncEnumerable`: it replaced a `taskSeq { }`
/// implementation that threw `NotImplementedException` in Debug builds, so these tests run against a fake
/// `FeedIterator` rather than the Cosmos DB Emulator, exercising the same Debug configuration CI builds and tests.
[<TestClass; IterationExtensionsUnitTestCategory>]
type IterationExtensionsUnitTests () =

    [<TestMethod>]
    member _.``AsAsyncEnumerable flattens items across pages in order`` () : Task = task {
        let iterator = new FakeFeedIterator<int> ([ [ 1; 2 ]; []; [ 3 ] ])

        let! items = iterator.AsAsyncEnumerable () |> TaskSeq.toArrayAsync

        CollectionAssert.AreEqual (
            [| 1; 2; 3 |],
            items,
            "AsAsyncEnumerable should flatten items across pages, including empty ones, in page order."
        )
    }

    [<TestMethod>]
    member _.``AsAsyncEnumerable stops reading once HasMoreResults is false`` () : Task = task {
        let iterator = new FakeFeedIterator<int> ([ [ 1 ] ])

        let! items = iterator.AsAsyncEnumerable () |> TaskSeq.toArrayAsync

        CollectionAssert.AreEqual (
            [| 1 |],
            items,
            "AsAsyncEnumerable should produce only the items already read once HasMoreResults is false."
        )

        Assert.AreEqual (
            1,
            iterator.ReadNextCallCount,
            "AsAsyncEnumerable should not call ReadNextAsync again once HasMoreResults is false."
        )
    }

    [<TestMethod>]
    member _.``AsAsyncEnumerable honors a token already cancelled before the first page is read`` () : Task = task {
        let iterator = new FakeFeedIterator<int> ([ [ 1; 2; 3 ] ])
        use cts = new CancellationTokenSource ()
        cts.Cancel ()

        let! _ =
            Assert.ThrowsExactlyAsync<OperationCanceledException>(
                Func<Task>(fun () -> task {
                    let! _ =
                        iterator.AsAsyncEnumerable (cts.Token)
                        |> TaskSeq.toListAsync
                    return ()
                }),
                "AsAsyncEnumerable should observe a token that is already cancelled when it reads the first page."
            )

        ()
    }

    [<TestMethod>]
    member _.``AsAsyncEnumerable observes cancellation between items of an already buffered page`` () : Task = task {
        let iterator = new FakeFeedIterator<int> ([ [ 1; 2 ] ])
        use cts = new CancellationTokenSource ()
        let enumerator : IAsyncEnumerator<int> =
            iterator.AsAsyncEnumerable(cts.Token).GetAsyncEnumerator(CancellationToken.None)

        let! movedToFirst = enumerator.MoveNextAsync ()
        Assert.IsTrue (movedToFirst, "The first item of an already buffered page should be produced.")
        Assert.AreEqual (1, enumerator.Current, "The first item of an already buffered page should be 1.")

        cts.Cancel ()

        let! _ =
            Assert.ThrowsExactlyAsync<OperationCanceledException>(
                Func<Task>(fun () -> enumerator.MoveNextAsync().AsTask() :> Task),
                "AsAsyncEnumerable should observe a cancelled token between items of an already buffered page."
            )

        ()
    }

    [<TestMethod>]
    member _.``AsAsyncEnumerable honors a token supplied only to GetAsyncEnumerator when it used the default token`` () : Task =
        task {
            // Mirrors what `WithCancellation` does on a real `await foreach`: the token reaches the sequence
            // only through GetAsyncEnumerator, since AsAsyncEnumerable was called without one.
            let iterator = new FakeFeedIterator<int> ([ [ 1; 2; 3 ] ])
            let enumerable = iterator.AsAsyncEnumerable ()
            use cts = new CancellationTokenSource ()
            cts.Cancel ()

            let! _ =
                Assert.ThrowsExactlyAsync<OperationCanceledException>(
                    Func<Task>(fun () -> task {
                        let enumerator = enumerable.GetAsyncEnumerator (cts.Token)
                        let! _ = enumerator.MoveNextAsync ()
                        return ()
                    }),
                    "AsAsyncEnumerable should observe a token supplied only through GetAsyncEnumerator when it was itself called with the default token."
                )

            ()
        }

    [<TestMethod>]
    member _.``AsAsyncEnumerable keeps its own explicit token over one supplied to GetAsyncEnumerator`` () : Task = task {
        let iterator = new FakeFeedIterator<int> ([ [ 1 ] ])
        use explicitCts = new CancellationTokenSource ()
        use enumeratorCts = new CancellationTokenSource ()
        enumeratorCts.Cancel ()

        let enumerator = iterator.AsAsyncEnumerable(explicitCts.Token).GetAsyncEnumerator(enumeratorCts.Token)

        let! movedToFirst = enumerator.MoveNextAsync ()

        Assert.IsTrue (
            movedToFirst,
            "A cancelled token passed only to GetAsyncEnumerator should not override an explicit token already passed to AsAsyncEnumerable."
        )
    }
