namespace FSharp.Azure.Cosmos.Tests

open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Tasks.Sources
open Microsoft.VisualStudio.TestTools.UnitTesting
open IcedTasks

/// Asynchronous values and failures the IcedTasks probes await.
module private ProbeAwaitables =

    /// <summary>
    /// Produces <paramref name="value"/> after <see cref="Task.Yield"/> has suspended the producer once, so an awaiting
    /// computation expression usually finds the task still running and has to resume later.
    /// </summary>
    let yielded (value : 'T) : Task<'T> = task {
        do! Task.Yield ()
        return value
    }

    /// <summary>
    /// Fails with an <see cref="InvalidOperationException"/> carrying <paramref name="message"/> after
    /// <see cref="Task.Yield"/> has suspended the producer once.
    /// </summary>
    let failsAfterYield (message : string) : Task<int> = task {
        do! Task.Yield ()
        return raise (InvalidOperationException message)
    }

/// The results every probe expects, whichever computation expression produced them.
module private ProbeExpectations =

    /// The values the sequence-and-loop probes collect: two awaited in sequence, three in a for loop, three in a while loop.
    let collected = [| 1..8 |]

    /// The log the awaitable-kinds probes write when every kind of awaitable, pending or already completed, resumes them
    /// with its value.
    let awaitableKinds = [|
        "Task"
        "Task<int>: 1"
        "ValueTask"
        "ValueTask<int>: 2"
        "completed Task"
        "completed Task<int>: 3"
        "completed ValueTask"
        "completed ValueTask<int>: 4"
    |]

    /// The log the try/finally probes write: each finally block runs exactly once, right after its own try block, and
    /// once per iteration inside a loop.
    let tryFinally = [|
        "inner try"
        "inner finally"
        "outer try awaited 1"
        "loop try awaited 1"
        "loop finally 1"
        "loop try awaited 2"
        "loop finally 2"
        "outer finally"
        "after try/finally"
    |]

    /// The log the use probes write: each resource is disposed exactly once, after the body, in reverse order of acquisition.
    let disposal = [|
        "body started"
        "body awaited 1"
        "use! IAsyncDisposable disposed"
        "use! IDisposable disposed"
        "use IAsyncDisposable disposed"
        "use IDisposable disposed"
    |]

    /// The log the exception probes write: a handler runs only for a failure, the awaited failure is caught, and the
    /// raised one propagates through each disposal and the finally block exactly once.
    let exceptions = [|
        "no failure: 1"
        "caught: awaited failure"
        "raising"
        "use IAsyncDisposable disposed"
        "use IDisposable disposed"
        "finally"
    |]

    /// The message of the exception the exception probes raise after an await.
    [<Literal>]
    let RaisedMessage = "raised after an await"

    /// The log the multi-start probes write over two starts: each start runs the whole body, from its first statement.
    let twoStarts = [| "started"; "finished"; "started"; "finished" |]

    /// How long the overlapping-start probes wait for two starts to return before they report a start as blocked.
    let startTimeLimit = TimeSpan.FromSeconds 10.0

/// Probes that fail in one configuration, kept and skipped there with the reason, so the other configuration still runs them.
module private KnownProbeFailures =

    /// <summary>
    /// Why the probes that start one <c>cancellableTask { }</c> or <c>cancellableValueTask { }</c> value more than once
    /// are skipped in Debug.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without optimizations the .NET SDK 10.0.401 compiler lowers no resumable computation expression to a state machine
    /// (dotnet/fsharp#20466), not even <c>task { }</c>, so every builder runs its dynamic implementation. In IcedTasks
    /// 0.11.9 the dynamic implementation of these two builders, and of <c>coldTask { }</c>, creates the state machine and
    /// its resumption record once per value, outside the function that starts the value, so all starts of the value share
    /// them. The static state machine of a Release build is copied for each start instead.
    /// </para>
    /// <para>
    /// A second start therefore resumes where the previous start last suspended rather than at the first statement: the
    /// statements before that point do not run again and the state they created is reused, so a loop that collected
    /// <c>[| 1; 2; 3 |]</c> returns <c>[| 1; 2; 3; 3 |]</c>, then <c>[| 1; 2; 3; 3; 3 |]</c> on a third start. A start
    /// made while an earlier start is still suspended blocks its caller until the pending await of that earlier start
    /// completes. A value built afresh for each start, a body that never suspends and every Release build behave
    /// correctly.
    /// </para>
    /// </remarks>
    [<Literal>]
    let DebugMultiStart =
        "Fails in Debug: SDK 10.0.400+ compiles no resumable state machine without optimizations (dotnet/fsharp#20466), "
        + "and the dynamic implementation in IcedTasks 0.11.9 shares one state machine between all starts of a value, "
        + "so a second start resumes where the previous one last suspended. Passes in Release."

/// <summary>
/// The source of one pending <see cref="ValueTask"/> or <see cref="ValueTask{T}"/> that completes only when the test
/// calls <see cref="SetResult"/>, backed by an <see cref="IValueTaskSource{T}"/> rather than by a task, the way most
/// value tasks .NET hands out are.
/// </summary>
/// <remarks>
/// Its continuations run synchronously, so <see cref="SetResult"/> resumes the awaiting probe on the caller's thread and
/// returns only once the probe has suspended again or completed. It counts how often its result was taken, because a
/// value task may be awaited only once.
/// </remarks>
type private PendingValueTaskSource<'T> () =

    // Synchronous continuations are the default; stated here because the probes depend on them.
    let mutable core = ManualResetValueTaskSourceCore<'T>(RunContinuationsAsynchronously = false)
    let mutable resultsTaken = 0

    /// <summary>Whether <see cref="SetResult"/> has completed the value task.</summary>
    member _.IsCompleted = core.GetStatus core.Version <> ValueTaskSourceStatus.Pending

    /// How many times an awaiter has taken the result of the value task.
    member _.ResultsTaken = resultsTaken

    /// <summary>The value task as a <see cref="ValueTask{T}"/> that completes with the value of <see cref="SetResult"/>.</summary>
    member this.ValueTaskOfResult = ValueTask<'T>(this, core.Version)

    /// <summary>The value task as a <see cref="ValueTask"/> that completes when <see cref="SetResult"/> is called.</summary>
    member this.ValueTask = ValueTask (this, core.Version)

    /// <summary>Completes the value task with <paramref name="value"/> and runs its continuation before returning.</summary>
    member _.SetResult (value : 'T) = core.SetResult value

    interface IValueTaskSource<'T> with
        /// <inheritdoc />
        member _.GetStatus token = core.GetStatus token

        /// <inheritdoc />
        member _.OnCompleted (continuation, state, token, flags) = core.OnCompleted (continuation, state, token, flags)

        /// <inheritdoc />
        member _.GetResult token =
            resultsTaken <- resultsTaken + 1
            core.GetResult token

    interface IValueTaskSource with
        /// <inheritdoc />
        member this.GetStatus token = (this :> IValueTaskSource<'T>).GetStatus token

        /// <inheritdoc />
        member this.OnCompleted (continuation, state, token, flags) =
            (this :> IValueTaskSource<'T>).OnCompleted(continuation, state, token, flags)

        /// <inheritdoc />
        member this.GetResult token = (this :> IValueTaskSource<'T>).GetResult token |> ignore

/// <summary>
/// One pending operation of each awaitable kind a computation expression has to support – <see cref="Task"/>,
/// <see cref="Task{T}"/>, <see cref="ValueTask"/> and <see cref="ValueTask{T}"/> – each completed only when the test
/// releases it, so a probe awaits operations that have not finished yet instead of already completed ones.
/// </summary>
/// <remarks>
/// <para>
/// Every completion runs its continuations synchronously: releasing an operation resumes the probe on the test's own
/// thread and returns only once the probe has suspended on the next operation or completed. That is what keeps the next
/// operation pending when the probe reaches it. With asynchronous continuations the test releases every operation before
/// the probe resumes from the first one, and the probe finds the other three already completed.
/// </para>
/// <para>
/// The value tasks come from <see cref="PendingValueTaskSource{T}"/>, which also counts how often the probe takes their
/// results.
/// </para>
/// </remarks>
type private PendingAwaitables () =

    // No TaskCreationOptions.RunContinuationsAsynchronously: see the remarks.
    let pendingTask = TaskCompletionSource ()
    let pendingTaskOfInt = TaskCompletionSource<int>()
    let pendingValueTask = PendingValueTaskSource<unit>()
    let pendingValueTaskOfInt = PendingValueTaskSource<int>()

    // The operations the probe reached only after the test had released them, so it awaited them already completed.
    let reachedAfterRelease = ResizeArray<string>()

    let reach (operation : string) (isReleased : bool) =
        if isReleased then
            reachedAfterRelease.Add operation

    /// <summary>A <see cref="Task"/> that completes when <see cref="ReleaseInOrder"/> releases it first.</summary>
    member _.Task () : Task =
        reach "Task" pendingTask.Task.IsCompleted
        pendingTask.Task

    /// <summary>A <see cref="Task{T}"/> that completes with 1 when <see cref="ReleaseInOrder"/> releases it second.</summary>
    member _.TaskOfInt () : Task<int> =
        reach "Task<int>" pendingTaskOfInt.Task.IsCompleted
        pendingTaskOfInt.Task

    /// <summary>A <see cref="ValueTask"/> that completes when <see cref="ReleaseInOrder"/> releases it third.</summary>
    member _.ValueTask () : ValueTask =
        reach "ValueTask" pendingValueTask.IsCompleted
        pendingValueTask.ValueTask

    /// <summary>A <see cref="ValueTask{T}"/> that completes with 2 when <see cref="ReleaseInOrder"/> releases it last.</summary>
    member _.ValueTaskOfInt () : ValueTask<int> =
        reach "ValueTask<int>" pendingValueTaskOfInt.IsCompleted
        pendingValueTaskOfInt.ValueTaskOfResult

    /// <summary>
    /// Releases the operations in the order the probes await them, checking before each release that the probe has not
    /// completed (it cannot while it is still waiting for one of them, unless it skipped an await), and afterwards that
    /// the last release ran the probe to its end, that the probe reached every operation before its release, and that it
    /// took the result of each value task exactly once.
    /// </summary>
    /// <param name="builderName">The computation expression under test, for the failure messages.</param>
    /// <param name="isCompleted">Reports whether the probe has completed.</param>
    member _.ReleaseInOrder (builderName : string, isCompleted : unit -> bool) =
        Assert.IsFalse (isCompleted (), $"%s{builderName} should still be waiting for the pending Task.")
        pendingTask.SetResult ()
        Assert.IsFalse (isCompleted (), $"%s{builderName} should still be waiting for the pending Task<int>.")
        pendingTaskOfInt.SetResult 1
        Assert.IsFalse (isCompleted (), $"%s{builderName} should still be waiting for the pending ValueTask.")
        pendingValueTask.SetResult ()
        Assert.IsFalse (isCompleted (), $"%s{builderName} should still be waiting for the pending ValueTask<int>.")
        pendingValueTaskOfInt.SetResult 2

        Assert.IsTrue (
            isCompleted (),
            $"%s{builderName} should run to its end while the last operation is released, because continuations run synchronously."
        )

        Assert.IsEmpty (
            reachedAfterRelease,
            $"%s{builderName} should reach every operation while it is still pending, not after the test released it."
        )

        Assert.AreEqual (
            1,
            pendingValueTask.ResultsTaken,
            $"%s{builderName} should take the result of the ValueTask exactly once."
        )

        Assert.AreEqual (
            1,
            pendingValueTaskOfInt.ResultsTaken,
            $"%s{builderName} should take the result of the ValueTask<int> exactly once."
        )

/// <summary>An <see cref="IDisposable"/> that appends one entry to <paramref name="log"/> on every disposal.</summary>
type private DisposalRecorder (log : ResizeArray<string>, name : string) =

    interface IDisposable with
        /// <inheritdoc />
        member _.Dispose () = log.Add $"%s{name} disposed"

/// <summary>
/// An <see cref="IAsyncDisposable"/> that appends one entry to <paramref name="log"/> on every disposal, after
/// <see cref="Task.Yield"/> has suspended the disposal once.
/// </summary>
type private AsyncDisposalRecorder (log : ResizeArray<string>, name : string) =

    interface IAsyncDisposable with
        /// <inheritdoc />
        member _.DisposeAsync () = valueTaskUnit {
            do! Task.Yield ()
            log.Add $"%s{name} disposed"
        }

/// <summary>
/// Probes the IcedTasks computation expressions library code may use – <c>valueTask { }</c>, <c>valueTaskUnit { }</c>,
/// <c>cancellableTask { }</c> and <c>cancellableValueTask { }</c> – in whichever configuration this assembly was built.
/// </summary>
/// <remarks>
/// <para>
/// Since .NET SDK 10.0.400 the F# compiler no longer lowers resumable computation expressions to static state machines
/// in Debug builds (dotnet/fsharp#20466), so they run through their builder's dynamic implementation instead, where
/// <c>taskSeq { }</c> threw or silently lost items. The builders are inlined into this assembly, so its own
/// configuration decides which implementation runs: these tests have to pass in both Debug and Release before library
/// code uses one of these computation expressions.
/// </para>
/// <para>
/// Each probe asserts the values and the order of the side effects it produced, not only that nothing threw. Its body is
/// written out once per computation expression on purpose: it means something only when compiled by that builder.
/// </para>
/// </remarks>
[<TestClass; IcedTasksProbeTestCategory>]
type IcedTasksProbeTests () =

    // valueTask

    [<TestMethod>]
    member _.``valueTask collects every value awaited in sequence and in loops`` () : Task = task {
        let running = valueTask {
            let collected = ResizeArray<int>()
            let! first = ProbeAwaitables.yielded 1
            collected.Add first
            do! Task.Yield ()
            let! second = ProbeAwaitables.yielded 2
            collected.Add second

            for candidate in 3..5 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            let mutable next = 6

            while next <= 8 do
                do! Task.Yield ()
                let! value = ProbeAwaitables.yielded next
                collected.Add value
                next <- next + 1

            return collected.ToArray ()
        }

        let! collected = running

        CollectionAssert.AreEqual (
            ProbeExpectations.collected,
            collected,
            "valueTask should collect every value awaited in sequence, in a for loop and in a while loop, in order."
        )
    }

    [<TestMethod>]
    member _.``valueTask resumes with the value of each awaitable kind`` () : Task = task {
        let awaitables = PendingAwaitables ()

        let running = valueTask {
            let log = ResizeArray<string>()
            do! awaitables.Task ()
            log.Add "Task"
            let! fromTaskOfInt = awaitables.TaskOfInt ()
            log.Add $"Task<int>: %d{fromTaskOfInt}"
            do! awaitables.ValueTask ()
            log.Add "ValueTask"
            let! fromValueTaskOfInt = awaitables.ValueTaskOfInt ()
            log.Add $"ValueTask<int>: %d{fromValueTaskOfInt}"
            do! Task.CompletedTask
            log.Add "completed Task"
            let! fromCompletedTaskOfInt = Task.FromResult 3
            log.Add $"completed Task<int>: %d{fromCompletedTaskOfInt}"
            do! ValueTask.CompletedTask
            log.Add "completed ValueTask"
            let! fromCompletedValueTaskOfInt = ValueTask<int> 4
            log.Add $"completed ValueTask<int>: %d{fromCompletedValueTaskOfInt}"
            return log.ToArray ()
        }

        awaitables.ReleaseInOrder ("valueTask", fun () -> running.IsCompleted)
        let! log = running

        CollectionAssert.AreEqual (
            ProbeExpectations.awaitableKinds,
            log,
            "valueTask should resume after a pending and an already completed Task, Task<'T>, ValueTask and ValueTask<'T> with each one's value, in order."
        )
    }

    [<TestMethod>]
    member _.``valueTask runs each finally block exactly once and in order`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTask {
            try
                try
                    log.Add "inner try"
                    do! Task.Yield ()
                finally
                    log.Add "inner finally"

                let! value = ProbeAwaitables.yielded 1
                log.Add $"outer try awaited %d{value}"

                for iteration in 1..2 do
                    try
                        let! awaited = ProbeAwaitables.yielded iteration
                        log.Add $"loop try awaited %d{awaited}"
                    finally
                        log.Add $"loop finally %d{iteration}"
            finally
                log.Add "outer finally"

            log.Add "after try/finally"
        }

        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.tryFinally,
            log.ToArray (),
            "valueTask should run each finally block exactly once, right after its try block, and once per iteration inside a loop."
        )
    }

    [<TestMethod>]
    member _.``valueTask disposes every used resource exactly once after the body`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTask {
            use syncResource = new DisposalRecorder (log, "use IDisposable")
            use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
            use! awaitedSyncResource = ProbeAwaitables.yielded (new DisposalRecorder (log, "use! IDisposable"))
            use! awaitedAsyncResource =
                ProbeAwaitables.yielded (new AsyncDisposalRecorder (log, "use! IAsyncDisposable"))
            log.Add "body started"
            let! value = ProbeAwaitables.yielded 1
            log.Add $"body awaited %d{value}"
        }

        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.disposal,
            log.ToArray (),
            "valueTask should dispose each IDisposable and IAsyncDisposable bound with use or use! exactly once, after the body, in reverse order."
        )
    }

    [<TestMethod>]
    member _.``valueTask propagates exceptions through try/with and try/finally`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTask {
            try
                try
                    let! value = ProbeAwaitables.yielded 1
                    log.Add $"no failure: %d{value}"
                with _ ->
                    log.Add "handler ran without a failure"

                try
                    let! _ = ProbeAwaitables.failsAfterYield "awaited failure"
                    log.Add "after the failed await"
                with :? InvalidOperationException as ex ->
                    log.Add $"caught: %s{ex.Message}"

                use syncResource = new DisposalRecorder (log, "use IDisposable")
                use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
                do! Task.Yield ()
                log.Add "raising"
                return raise (ArgumentException ProbeExpectations.RaisedMessage)
            finally
                log.Add "finally"
        }

        let! ex =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task {
                    let! (_ : int) = running
                    return ()
                }),
                "valueTask should propagate an exception raised after an await to its awaiter."
            )

        Assert.AreEqual (
            ProbeExpectations.RaisedMessage,
            ex.Message,
            "valueTask should propagate the exception its body raised, not another one."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.exceptions,
            log.ToArray (),
            "valueTask should run a handler only for a failure, catch the awaited failure, then dispose each resource and run finally exactly once while the raised exception propagates."
        )
    }

    // valueTaskUnit

    [<TestMethod>]
    member _.``valueTaskUnit collects every value awaited in sequence and in loops`` () : Task = task {
        let collected = ResizeArray<int>()

        let running = valueTaskUnit {
            let! first = ProbeAwaitables.yielded 1
            collected.Add first
            do! Task.Yield ()
            let! second = ProbeAwaitables.yielded 2
            collected.Add second

            for candidate in 3..5 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            let mutable next = 6

            while next <= 8 do
                do! Task.Yield ()
                let! value = ProbeAwaitables.yielded next
                collected.Add value
                next <- next + 1
        }

        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.collected,
            collected.ToArray (),
            "valueTaskUnit should collect every value awaited in sequence, in a for loop and in a while loop, in order."
        )
    }

    [<TestMethod>]
    member _.``valueTaskUnit resumes with the value of each awaitable kind`` () : Task = task {
        let awaitables = PendingAwaitables ()
        let log = ResizeArray<string>()

        let running = valueTaskUnit {
            do! awaitables.Task ()
            log.Add "Task"
            let! fromTaskOfInt = awaitables.TaskOfInt ()
            log.Add $"Task<int>: %d{fromTaskOfInt}"
            do! awaitables.ValueTask ()
            log.Add "ValueTask"
            let! fromValueTaskOfInt = awaitables.ValueTaskOfInt ()
            log.Add $"ValueTask<int>: %d{fromValueTaskOfInt}"
            do! Task.CompletedTask
            log.Add "completed Task"
            let! fromCompletedTaskOfInt = Task.FromResult 3
            log.Add $"completed Task<int>: %d{fromCompletedTaskOfInt}"
            do! ValueTask.CompletedTask
            log.Add "completed ValueTask"
            let! fromCompletedValueTaskOfInt = ValueTask<int> 4
            log.Add $"completed ValueTask<int>: %d{fromCompletedValueTaskOfInt}"
        }

        awaitables.ReleaseInOrder ("valueTaskUnit", fun () -> running.IsCompleted)
        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.awaitableKinds,
            log.ToArray (),
            "valueTaskUnit should resume after a pending and an already completed Task, Task<'T>, ValueTask and ValueTask<'T> with each one's value, in order."
        )
    }

    [<TestMethod>]
    member _.``valueTaskUnit runs each finally block exactly once and in order`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTaskUnit {
            try
                try
                    log.Add "inner try"
                    do! Task.Yield ()
                finally
                    log.Add "inner finally"

                let! value = ProbeAwaitables.yielded 1
                log.Add $"outer try awaited %d{value}"

                for iteration in 1..2 do
                    try
                        let! awaited = ProbeAwaitables.yielded iteration
                        log.Add $"loop try awaited %d{awaited}"
                    finally
                        log.Add $"loop finally %d{iteration}"
            finally
                log.Add "outer finally"

            log.Add "after try/finally"
        }

        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.tryFinally,
            log.ToArray (),
            "valueTaskUnit should run each finally block exactly once, right after its try block, and once per iteration inside a loop."
        )
    }

    [<TestMethod>]
    member _.``valueTaskUnit disposes every used resource exactly once after the body`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTaskUnit {
            use syncResource = new DisposalRecorder (log, "use IDisposable")
            use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
            use! awaitedSyncResource = ProbeAwaitables.yielded (new DisposalRecorder (log, "use! IDisposable"))
            use! awaitedAsyncResource =
                ProbeAwaitables.yielded (new AsyncDisposalRecorder (log, "use! IAsyncDisposable"))
            log.Add "body started"
            let! value = ProbeAwaitables.yielded 1
            log.Add $"body awaited %d{value}"
        }

        do! running

        CollectionAssert.AreEqual (
            ProbeExpectations.disposal,
            log.ToArray (),
            "valueTaskUnit should dispose each IDisposable and IAsyncDisposable bound with use or use! exactly once, after the body, in reverse order."
        )
    }

    [<TestMethod>]
    member _.``valueTaskUnit propagates exceptions through try/with and try/finally`` () : Task = task {
        let log = ResizeArray<string>()

        let running = valueTaskUnit {
            try
                try
                    let! value = ProbeAwaitables.yielded 1
                    log.Add $"no failure: %d{value}"
                with _ ->
                    log.Add "handler ran without a failure"

                try
                    let! _ = ProbeAwaitables.failsAfterYield "awaited failure"
                    log.Add "after the failed await"
                with :? InvalidOperationException as ex ->
                    log.Add $"caught: %s{ex.Message}"

                use syncResource = new DisposalRecorder (log, "use IDisposable")
                use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
                do! Task.Yield ()
                log.Add "raising"
                raise (ArgumentException ProbeExpectations.RaisedMessage)
            finally
                log.Add "finally"
        }

        let! ex =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task { do! running }),
                "valueTaskUnit should propagate an exception raised after an await to its awaiter."
            )

        Assert.AreEqual (
            ProbeExpectations.RaisedMessage,
            ex.Message,
            "valueTaskUnit should propagate the exception its body raised, not another one."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.exceptions,
            log.ToArray (),
            "valueTaskUnit should run a handler only for a failure, catch the awaited failure, then dispose each resource and run finally exactly once while the raised exception propagates."
        )
    }

    // cancellableTask

    [<TestMethod>]
    member _.``cancellableTask collects every value awaited in sequence and in loops`` () : Task = task {
        let probe = cancellableTask {
            let collected = ResizeArray<int>()
            let! first = ProbeAwaitables.yielded 1
            collected.Add first
            do! Task.Yield ()
            let! second = ProbeAwaitables.yielded 2
            collected.Add second

            for candidate in 3..5 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            let mutable next = 6

            while next <= 8 do
                do! Task.Yield ()
                let! value = ProbeAwaitables.yielded next
                collected.Add value
                next <- next + 1

            return collected.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let! collected = probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.collected,
            collected,
            "cancellableTask should collect every value awaited in sequence, in a for loop and in a while loop, in order."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask resumes with the value of each awaitable kind`` () : Task = task {
        let awaitables = PendingAwaitables ()

        let probe = cancellableTask {
            let log = ResizeArray<string>()
            do! awaitables.Task ()
            log.Add "Task"
            let! fromTaskOfInt = awaitables.TaskOfInt ()
            log.Add $"Task<int>: %d{fromTaskOfInt}"
            do! awaitables.ValueTask ()
            log.Add "ValueTask"
            let! fromValueTaskOfInt = awaitables.ValueTaskOfInt ()
            log.Add $"ValueTask<int>: %d{fromValueTaskOfInt}"
            do! Task.CompletedTask
            log.Add "completed Task"
            let! fromCompletedTaskOfInt = Task.FromResult 3
            log.Add $"completed Task<int>: %d{fromCompletedTaskOfInt}"
            do! ValueTask.CompletedTask
            log.Add "completed ValueTask"
            let! fromCompletedValueTaskOfInt = ValueTask<int> 4
            log.Add $"completed ValueTask<int>: %d{fromCompletedValueTaskOfInt}"
            return log.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let running = probe cts.Token
        awaitables.ReleaseInOrder ("cancellableTask", fun () -> running.IsCompleted)
        let! log = running

        CollectionAssert.AreEqual (
            ProbeExpectations.awaitableKinds,
            log,
            "cancellableTask should resume after a pending and an already completed Task, Task<'T>, ValueTask and ValueTask<'T> with each one's value, in order."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask runs each finally block exactly once and in order`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableTask {
            try
                try
                    log.Add "inner try"
                    do! Task.Yield ()
                finally
                    log.Add "inner finally"

                let! value = ProbeAwaitables.yielded 1
                log.Add $"outer try awaited %d{value}"

                for iteration in 1..2 do
                    try
                        let! awaited = ProbeAwaitables.yielded iteration
                        log.Add $"loop try awaited %d{awaited}"
                    finally
                        log.Add $"loop finally %d{iteration}"
            finally
                log.Add "outer finally"

            log.Add "after try/finally"
        }

        use cts = new CancellationTokenSource ()
        do! probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.tryFinally,
            log.ToArray (),
            "cancellableTask should run each finally block exactly once, right after its try block, and once per iteration inside a loop."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask disposes every used resource exactly once after the body`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableTask {
            use syncResource = new DisposalRecorder (log, "use IDisposable")
            use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
            use! awaitedSyncResource = ProbeAwaitables.yielded (new DisposalRecorder (log, "use! IDisposable"))
            use! awaitedAsyncResource =
                ProbeAwaitables.yielded (new AsyncDisposalRecorder (log, "use! IAsyncDisposable"))
            log.Add "body started"
            let! value = ProbeAwaitables.yielded 1
            log.Add $"body awaited %d{value}"
        }

        use cts = new CancellationTokenSource ()
        do! probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.disposal,
            log.ToArray (),
            "cancellableTask should dispose each IDisposable and IAsyncDisposable bound with use or use! exactly once, after the body, in reverse order."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask propagates exceptions through try/with and try/finally`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableTask {
            try
                try
                    let! value = ProbeAwaitables.yielded 1
                    log.Add $"no failure: %d{value}"
                with _ ->
                    log.Add "handler ran without a failure"

                try
                    let! _ = ProbeAwaitables.failsAfterYield "awaited failure"
                    log.Add "after the failed await"
                with :? InvalidOperationException as ex ->
                    log.Add $"caught: %s{ex.Message}"

                use syncResource = new DisposalRecorder (log, "use IDisposable")
                use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
                do! Task.Yield ()
                log.Add "raising"
                return raise (ArgumentException ProbeExpectations.RaisedMessage)
            finally
                log.Add "finally"
        }

        use cts = new CancellationTokenSource ()

        let! ex =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task {
                    let! (_ : int) = probe cts.Token
                    return ()
                }),
                "cancellableTask should propagate an exception raised after an await to its awaiter."
            )

        Assert.AreEqual (
            ProbeExpectations.RaisedMessage,
            ex.Message,
            "cancellableTask should propagate the exception its body raised, not another one."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.exceptions,
            log.ToArray (),
            "cancellableTask should run a handler only for a failure, catch the awaited failure, then dispose each resource and run finally exactly once while the raised exception propagates."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask does not start its body with an already cancelled token`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableTask {
            log.Add "started"
            do! Task.Yield ()
            log.Add "finished"
        }

        use cts = new CancellationTokenSource ()
        cts.Cancel ()

        let! ex =
            Assert.ThrowsAsync<OperationCanceledException>(
                Func<Task>(fun () -> task { do! probe cts.Token }),
                "cancellableTask should observe a token that is already cancelled when it starts."
            )

        Assert.AreEqual (cts.Token, ex.CancellationToken, "cancellableTask should report the token it was started with.")
        Assert.IsEmpty (log, "cancellableTask should not run any of its body with an already cancelled token.")
    }

    [<TestMethod>]
    member _.``cancellableTask observes cancellation before its next bind`` () : Task = task {
        let log = ResizeArray<string>()
        use cts = new CancellationTokenSource ()

        let probe = cancellableTask {
            try
                let! first = ProbeAwaitables.yielded 1
                log.Add $"first: %d{first}"
                cts.Cancel ()
                let! second = ProbeAwaitables.yielded 2
                log.Add $"second: %d{second}"
            finally
                log.Add "finally"
        }

        let! ex =
            Assert.ThrowsAsync<OperationCanceledException>(
                Func<Task>(fun () -> task { do! probe cts.Token }),
                "cancellableTask should observe a token cancelled during its body at the next bind."
            )

        Assert.AreEqual (cts.Token, ex.CancellationToken, "cancellableTask should report the token it was started with.")

        CollectionAssert.AreEqual (
            [| "first: 1"; "finally" |],
            log.ToArray (),
            "cancellableTask should stop before the bind that follows the cancellation and still run finally exactly once."
        )
    }

    [<TestMethod>]
    member _.``cancellableTask passes its token to every nested bind`` () : Task = task {
        // Built by a function for every bind: one value started twice would make this probe depend on the multi-start
        // behaviour the multi-start probes check (see KnownProbeFailures.DebugMultiStart).
        let nestedCancellableTask () = cancellableTask {
            do! Task.Yield ()
            return! CancellableTask.getCancellationToken ()
        }

        let nestedCancellableValueTask () = cancellableValueTask {
            do! Task.Yield ()
            return! CancellableValueTask.getCancellationToken ()
        }

        let probe = cancellableTask {
            let! own = CancellableTask.getCancellationToken ()
            let! fromCancellableTask = nestedCancellableTask ()
            let! fromCancellableValueTask = nestedCancellableValueTask ()
            let! fromFunction = fun (token : CancellationToken) -> ProbeAwaitables.yielded token

            let! fromTwoLevelsDown = cancellableTask {
                let! fromInner = nestedCancellableTask ()
                return fromInner
            }

            return [| own; fromCancellableTask; fromCancellableValueTask; fromFunction; fromTwoLevelsDown |]
        }

        use cts = new CancellationTokenSource ()
        let! tokens = probe cts.Token

        CollectionAssert.AreEqual (
            Array.create 5 cts.Token,
            tokens,
            "cancellableTask should pass its token to itself, a nested cancellableTask, a nested cancellableValueTask, a CancellationToken -> Task function and a cancellableTask nested two levels down."
        )
    }

#if DEBUG
    // Skipped in Debug only, so that Release keeps checking it: see KnownProbeFailures.DebugMultiStart.
    [<Ignore(KnownProbeFailures.DebugMultiStart)>]
#endif
    [<TestMethod>]
    member _.``cancellableTask runs again from the start when started a second time`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableTask {
            log.Add "started"
            let collected = ResizeArray<int>()

            for candidate in 1..3 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            log.Add "finished"
            return collected.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let! first = probe cts.Token
        let! second = probe cts.Token

        CollectionAssert.AreEqual ([| 1; 2; 3 |], first, "cancellableTask should collect every value on its first run.")

        CollectionAssert.AreEqual (
            [| 1; 2; 3 |],
            second,
            "cancellableTask should run again from the start, with fresh state, when started a second time."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.twoStarts,
            log.ToArray (),
            "cancellableTask should run its whole body, from the first statement, on every start."
        )
    }

#if DEBUG
    // Skipped in Debug only, so that Release keeps checking it: see KnownProbeFailures.DebugMultiStart.
    [<Ignore(KnownProbeFailures.DebugMultiStart)>]
#endif
    [<TestMethod>]
    member _.``cancellableTask runs overlapping starts of the same value independently`` () : Task = task {
        let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)

        let probe = cancellableTask {
            let collected = ResizeArray<int>()
            collected.Add 1
            do! gate.Task
            let! value = ProbeAwaitables.yielded 2
            collected.Add value
            let! token = CancellableTask.getCancellationToken ()
            return struct (token, collected.ToArray ())
        }

        use firstSource = new CancellationTokenSource ()
        use secondSource = new CancellationTokenSource ()

        // Both starts run on a thread pool thread under a time limit: a builder that shares one state machine between
        // the starts of a value blocks the second start on the first one's pending await, which would hang the run.
        let starting = Task.Run (Func<_>(fun () -> struct (probe firstSource.Token, probe secondSource.Token)))

        try
            let! returned = Task.WhenAny (starting, Task.Delay ProbeExpectations.startTimeLimit)

            Assert.AreSame (
                starting :> Task,
                returned,
                "cancellableTask should return from a second start while the first start is still suspended."
            )

            let! struct (firstRun, secondRun) = starting
            gate.SetResult ()
            let! struct (firstToken, firstCollected) = firstRun
            let! struct (secondToken, secondCollected) = secondRun

            Assert.AreEqual (firstSource.Token, firstToken, "The first start of cancellableTask should observe its own token.")
            Assert.AreEqual (secondSource.Token, secondToken, "The second start of cancellableTask should observe its own token.")
            CollectionAssert.AreEqual (
                [| 1; 2 |],
                firstCollected,
                "The first start of cancellableTask should collect its own values."
            )

            CollectionAssert.AreEqual (
                [| 1; 2 |],
                secondCollected,
                "The second start of cancellableTask should collect its own values."
            )
        finally
            // Lets a start blocked on the gate finish after a failed assertion.
            gate.TrySetResult () |> ignore
    }

    // cancellableValueTask

    [<TestMethod>]
    member _.``cancellableValueTask collects every value awaited in sequence and in loops`` () : Task = task {
        let probe = cancellableValueTask {
            let collected = ResizeArray<int>()
            let! first = ProbeAwaitables.yielded 1
            collected.Add first
            do! Task.Yield ()
            let! second = ProbeAwaitables.yielded 2
            collected.Add second

            for candidate in 3..5 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            let mutable next = 6

            while next <= 8 do
                do! Task.Yield ()
                let! value = ProbeAwaitables.yielded next
                collected.Add value
                next <- next + 1

            return collected.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let! collected = probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.collected,
            collected,
            "cancellableValueTask should collect every value awaited in sequence, in a for loop and in a while loop, in order."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask resumes with the value of each awaitable kind`` () : Task = task {
        let awaitables = PendingAwaitables ()

        let probe = cancellableValueTask {
            let log = ResizeArray<string>()
            do! awaitables.Task ()
            log.Add "Task"
            let! fromTaskOfInt = awaitables.TaskOfInt ()
            log.Add $"Task<int>: %d{fromTaskOfInt}"
            do! awaitables.ValueTask ()
            log.Add "ValueTask"
            let! fromValueTaskOfInt = awaitables.ValueTaskOfInt ()
            log.Add $"ValueTask<int>: %d{fromValueTaskOfInt}"
            do! Task.CompletedTask
            log.Add "completed Task"
            let! fromCompletedTaskOfInt = Task.FromResult 3
            log.Add $"completed Task<int>: %d{fromCompletedTaskOfInt}"
            do! ValueTask.CompletedTask
            log.Add "completed ValueTask"
            let! fromCompletedValueTaskOfInt = ValueTask<int> 4
            log.Add $"completed ValueTask<int>: %d{fromCompletedValueTaskOfInt}"
            return log.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let running = probe cts.Token
        awaitables.ReleaseInOrder ("cancellableValueTask", fun () -> running.IsCompleted)
        let! log = running

        CollectionAssert.AreEqual (
            ProbeExpectations.awaitableKinds,
            log,
            "cancellableValueTask should resume after a pending and an already completed Task, Task<'T>, ValueTask and ValueTask<'T> with each one's value, in order."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask runs each finally block exactly once and in order`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableValueTask {
            try
                try
                    log.Add "inner try"
                    do! Task.Yield ()
                finally
                    log.Add "inner finally"

                let! value = ProbeAwaitables.yielded 1
                log.Add $"outer try awaited %d{value}"

                for iteration in 1..2 do
                    try
                        let! awaited = ProbeAwaitables.yielded iteration
                        log.Add $"loop try awaited %d{awaited}"
                    finally
                        log.Add $"loop finally %d{iteration}"
            finally
                log.Add "outer finally"

            log.Add "after try/finally"
        }

        use cts = new CancellationTokenSource ()
        do! probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.tryFinally,
            log.ToArray (),
            "cancellableValueTask should run each finally block exactly once, right after its try block, and once per iteration inside a loop."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask disposes every used resource exactly once after the body`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableValueTask {
            use syncResource = new DisposalRecorder (log, "use IDisposable")
            use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
            use! awaitedSyncResource = ProbeAwaitables.yielded (new DisposalRecorder (log, "use! IDisposable"))
            use! awaitedAsyncResource =
                ProbeAwaitables.yielded (new AsyncDisposalRecorder (log, "use! IAsyncDisposable"))
            log.Add "body started"
            let! value = ProbeAwaitables.yielded 1
            log.Add $"body awaited %d{value}"
        }

        use cts = new CancellationTokenSource ()
        do! probe cts.Token

        CollectionAssert.AreEqual (
            ProbeExpectations.disposal,
            log.ToArray (),
            "cancellableValueTask should dispose each IDisposable and IAsyncDisposable bound with use or use! exactly once, after the body, in reverse order."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask propagates exceptions through try/with and try/finally`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableValueTask {
            try
                try
                    let! value = ProbeAwaitables.yielded 1
                    log.Add $"no failure: %d{value}"
                with _ ->
                    log.Add "handler ran without a failure"

                try
                    let! _ = ProbeAwaitables.failsAfterYield "awaited failure"
                    log.Add "after the failed await"
                with :? InvalidOperationException as ex ->
                    log.Add $"caught: %s{ex.Message}"

                use syncResource = new DisposalRecorder (log, "use IDisposable")
                use asyncResource = new AsyncDisposalRecorder (log, "use IAsyncDisposable")
                do! Task.Yield ()
                log.Add "raising"
                return raise (ArgumentException ProbeExpectations.RaisedMessage)
            finally
                log.Add "finally"
        }

        use cts = new CancellationTokenSource ()

        let! ex =
            Assert.ThrowsExactlyAsync<ArgumentException>(
                Func<Task>(fun () -> task {
                    let! (_ : int) = probe cts.Token
                    return ()
                }),
                "cancellableValueTask should propagate an exception raised after an await to its awaiter."
            )

        Assert.AreEqual (
            ProbeExpectations.RaisedMessage,
            ex.Message,
            "cancellableValueTask should propagate the exception its body raised, not another one."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.exceptions,
            log.ToArray (),
            "cancellableValueTask should run a handler only for a failure, catch the awaited failure, then dispose each resource and run finally exactly once while the raised exception propagates."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask does not start its body with an already cancelled token`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableValueTask {
            log.Add "started"
            do! Task.Yield ()
            log.Add "finished"
        }

        use cts = new CancellationTokenSource ()
        cts.Cancel ()

        let! ex =
            Assert.ThrowsAsync<OperationCanceledException>(
                Func<Task>(fun () -> task { do! probe cts.Token }),
                "cancellableValueTask should observe a token that is already cancelled when it starts."
            )

        Assert.AreEqual (cts.Token, ex.CancellationToken, "cancellableValueTask should report the token it was started with.")
        Assert.IsEmpty (log, "cancellableValueTask should not run any of its body with an already cancelled token.")
    }

    [<TestMethod>]
    member _.``cancellableValueTask observes cancellation before its next bind`` () : Task = task {
        let log = ResizeArray<string>()
        use cts = new CancellationTokenSource ()

        let probe = cancellableValueTask {
            try
                let! first = ProbeAwaitables.yielded 1
                log.Add $"first: %d{first}"
                cts.Cancel ()
                let! second = ProbeAwaitables.yielded 2
                log.Add $"second: %d{second}"
            finally
                log.Add "finally"
        }

        let! ex =
            Assert.ThrowsAsync<OperationCanceledException>(
                Func<Task>(fun () -> task { do! probe cts.Token }),
                "cancellableValueTask should observe a token cancelled during its body at the next bind."
            )

        Assert.AreEqual (cts.Token, ex.CancellationToken, "cancellableValueTask should report the token it was started with.")

        CollectionAssert.AreEqual (
            [| "first: 1"; "finally" |],
            log.ToArray (),
            "cancellableValueTask should stop before the bind that follows the cancellation and still run finally exactly once."
        )
    }

    [<TestMethod>]
    member _.``cancellableValueTask passes its token to every nested bind`` () : Task = task {
        // Built by a function for every bind: one value started twice would make this probe depend on the multi-start
        // behaviour the multi-start probes check (see KnownProbeFailures.DebugMultiStart).
        let nestedCancellableTask () = cancellableTask {
            do! Task.Yield ()
            return! CancellableTask.getCancellationToken ()
        }

        let nestedCancellableValueTask () = cancellableValueTask {
            do! Task.Yield ()
            return! CancellableValueTask.getCancellationToken ()
        }

        let probe = cancellableValueTask {
            let! own = CancellableValueTask.getCancellationToken ()
            let! fromCancellableTask = nestedCancellableTask ()
            let! fromCancellableValueTask = nestedCancellableValueTask ()
            let! fromFunction = fun (token : CancellationToken) -> ProbeAwaitables.yielded token

            let! fromTwoLevelsDown = cancellableValueTask {
                let! fromInner = nestedCancellableValueTask ()
                return fromInner
            }

            return [| own; fromCancellableTask; fromCancellableValueTask; fromFunction; fromTwoLevelsDown |]
        }

        use cts = new CancellationTokenSource ()
        let! tokens = probe cts.Token

        CollectionAssert.AreEqual (
            Array.create 5 cts.Token,
            tokens,
            "cancellableValueTask should pass its token to itself, a nested cancellableTask, a nested cancellableValueTask, a CancellationToken -> Task function and a cancellableValueTask nested two levels down."
        )
    }

#if DEBUG
    // Skipped in Debug only, so that Release keeps checking it: see KnownProbeFailures.DebugMultiStart.
    [<Ignore(KnownProbeFailures.DebugMultiStart)>]
#endif
    [<TestMethod>]
    member _.``cancellableValueTask runs again from the start when started a second time`` () : Task = task {
        let log = ResizeArray<string>()

        let probe = cancellableValueTask {
            log.Add "started"
            let collected = ResizeArray<int>()

            for candidate in 1..3 do
                let! value = ProbeAwaitables.yielded candidate
                collected.Add value

            log.Add "finished"
            return collected.ToArray ()
        }

        use cts = new CancellationTokenSource ()
        let! first = probe cts.Token
        let! second = probe cts.Token

        CollectionAssert.AreEqual ([| 1; 2; 3 |], first, "cancellableValueTask should collect every value on its first run.")

        CollectionAssert.AreEqual (
            [| 1; 2; 3 |],
            second,
            "cancellableValueTask should run again from the start, with fresh state, when started a second time."
        )

        CollectionAssert.AreEqual (
            ProbeExpectations.twoStarts,
            log.ToArray (),
            "cancellableValueTask should run its whole body, from the first statement, on every start."
        )
    }

#if DEBUG
    // Skipped in Debug only, so that Release keeps checking it: see KnownProbeFailures.DebugMultiStart.
    [<Ignore(KnownProbeFailures.DebugMultiStart)>]
#endif
    [<TestMethod>]
    member _.``cancellableValueTask runs overlapping starts of the same value independently`` () : Task = task {
        let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)

        let probe = cancellableValueTask {
            let collected = ResizeArray<int>()
            collected.Add 1
            do! gate.Task
            let! value = ProbeAwaitables.yielded 2
            collected.Add value
            let! token = CancellableValueTask.getCancellationToken ()
            return struct (token, collected.ToArray ())
        }

        use firstSource = new CancellationTokenSource ()
        use secondSource = new CancellationTokenSource ()

        // Both starts run on a thread pool thread under a time limit: a builder that shares one state machine between
        // the starts of a value blocks the second start on the first one's pending await, which would hang the run.
        let starting = Task.Run (Func<_>(fun () -> struct (probe firstSource.Token, probe secondSource.Token)))

        try
            let! returned = Task.WhenAny (starting, Task.Delay ProbeExpectations.startTimeLimit)

            Assert.AreSame (
                starting :> Task,
                returned,
                "cancellableValueTask should return from a second start while the first start is still suspended."
            )

            let! struct (firstRun, secondRun) = starting
            gate.SetResult ()
            let! struct (firstToken, firstCollected) = firstRun
            let! struct (secondToken, secondCollected) = secondRun

            Assert.AreEqual (
                firstSource.Token,
                firstToken,
                "The first start of cancellableValueTask should observe its own token."
            )
            Assert.AreEqual (
                secondSource.Token,
                secondToken,
                "The second start of cancellableValueTask should observe its own token."
            )

            CollectionAssert.AreEqual (
                [| 1; 2 |],
                firstCollected,
                "The first start of cancellableValueTask should collect its own values."
            )

            CollectionAssert.AreEqual (
                [| 1; 2 |],
                secondCollected,
                "The second start of cancellableValueTask should collect its own values."
            )
        finally
            // Lets a start blocked on the gate finish after a failed assertion.
            gate.TrySetResult () |> ignore
    }
