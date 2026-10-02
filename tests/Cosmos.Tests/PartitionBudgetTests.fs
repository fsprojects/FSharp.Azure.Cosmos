namespace FSharp.Azure.Cosmos.Tests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests.Integration

/// <summary>
/// Emulator-free coverage of <see cref="PartitionBudget"/>, which keeps the fixtures of the integration tests,
/// <see cref="DatabaseTestApplicationFactory"/> instances, within the emulator's partition count.
/// </summary>
[<TestClass; TestInfrastructureUnitTestCategory>]
type PartitionBudgetTests () =

    // Long enough never to fire on a healthy machine; it only turns a deadlock into a failure instead of a hang
    static let deadlockTimeout = TimeSpan.FromSeconds 30.0

    [<TestMethod>]
    member _.``AcquireAsync takes the requested permits and Release gives them back`` () : Task = task {
        let budget = PartitionBudget 3

        let! acquired = budget.AcquireAsync (2, CancellationToken.None)

        Assert.AreEqual (2, acquired, "AcquireAsync should report the number of permits it took.")
        Assert.AreEqual (1, budget.Available, "Two of three permits should be held.")

        budget.Release acquired

        Assert.AreEqual (3, budget.Available, "Release should give every permit back.")
    }

    [<TestMethod>]
    member _.``AcquireAsync waits until enough permits are released`` () : Task = task {
        let budget = PartitionBudget 2
        let! held = budget.AcquireAsync (2, CancellationToken.None)

        let waiting = budget.AcquireAsync (1, CancellationToken.None)

        Assert.IsFalse (waiting.IsCompleted, "AcquireAsync should wait while no permit is free.")

        budget.Release held
        let! acquired = waiting.WaitAsync deadlockTimeout

        Assert.AreEqual (1, acquired, "AcquireAsync should take its permit once one is released.")
    }

    [<TestMethod>]
    member _.``AcquireAsync gives back the permits it took when it is cancelled`` () : Task = task {
        let budget = PartitionBudget 2
        let! held = budget.AcquireAsync (1, CancellationToken.None)
        use cancellation = new CancellationTokenSource ()

        // Takes the one free permit, then waits for the second
        let waiting = budget.AcquireAsync (2, cancellation.Token)
        cancellation.Cancel ()

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>(
                Func<Task>(fun () -> waiting :> Task),
                "A cancelled AcquireAsync should throw OperationCanceledException."
            )

        Assert.AreEqual (1, budget.Available, "A cancelled AcquireAsync should give back the permit it already took.")

        budget.Release held
        let! acquired = (budget.AcquireAsync (2, CancellationToken.None)).WaitAsync deadlockTimeout

        Assert.AreEqual (2, acquired, "Every permit should be available again after the cancelled acquisition.")
    }

    [<TestMethod>]
    member _.``AcquireAsync rejects a count larger than the budget`` () : Task = task {
        let budget = PartitionBudget 2

        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                Func<Task>(fun () -> budget.AcquireAsync (3, CancellationToken.None) :> Task),
                "AcquireAsync should reject a count it could never satisfy instead of waiting forever."
            )

        ()
    }

    [<TestMethod>]
    member _.``AcquireAsync never deadlocks or exceeds the budget when fixtures need several permits each`` () : Task = task {
        // Three permits and fixtures that need two each: taking the permits one at a time would let two fixtures hold
        // one each and wait for each other forever
        let budget = PartitionBudget 3
        let sync = obj ()
        let held = ref 0
        let maximumHeld = ref 0

        let fixture () : Task = task {
            let! acquired = budget.AcquireAsync (2, CancellationToken.None)

            lock
                sync
                (fun () ->
                    held.Value <- held.Value + acquired
                    maximumHeld.Value <- max maximumHeld.Value held.Value
                )

            do! Task.Yield ()
            lock sync (fun () -> held.Value <- held.Value - acquired)
            budget.Release acquired
        }

        let fixtures = [| for _ in 1..200 -> Task.Run (Func<Task> fixture) |]
        do! (Task.WhenAll fixtures).WaitAsync deadlockTimeout

        Assert.IsLessThanOrEqualTo (3, maximumHeld.Value, "The fixtures should never hold more permits than the budget has.")
        Assert.AreEqual (3, budget.Available, "Every permit should be given back once all fixtures are done.")
    }
