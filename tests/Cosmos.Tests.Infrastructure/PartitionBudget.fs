namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Threading
open System.Threading.Tasks

/// <summary>
/// A counting semaphore over the partitions of the Cosmos DB Emulator, from which every
/// <see cref="DatabaseTestApplicationFactory"/> takes one permit per container it creates.
/// </summary>
/// <remarks>
/// <para>
/// The permits of one fixture are acquired atomically: taking them one at a time while other fixtures do the same can
/// deadlock once every fixture holds part of what it needs and waits for the rest. Here only one caller at a time is
/// between its first and its last permit; a caller that holds permits never waits for more, so the one that is
/// acquiring always gets the permits it waits for as soon as other fixtures release theirs.
/// </para>
/// <para>
/// A cancelled acquisition gives back the permits it already took, so a caller holds either all the permits it asked
/// for or none, and must release exactly the number <see cref="AcquireAsync"/> returned.
/// </para>
/// </remarks>
[<Sealed>]
type PartitionBudget (limit : int) =

    do
        if limit < 1 then
            raise (ArgumentOutOfRangeException (nameof limit, limit, "The partition budget needs at least one partition."))

    let permits = new SemaphoreSlim (limit, limit)
    let acquisition = new SemaphoreSlim (1, 1)

    let validateCount (count : int) =
        if count < 0 || count > limit then
            raise (
                ArgumentOutOfRangeException (
                    nameof count,
                    count,
                    $"A fixture can take between 0 and {limit} partitions of this budget; "
                    + "raise COSMOS_EMULATOR_PARTITION_COUNT together with the emulator's partition count to create more containers."
                )
            )

    /// <summary>
    /// The number of permits nobody holds at the moment.
    /// </summary>
    member _.Available = permits.CurrentCount

    /// <summary>
    /// Waits until <paramref name="count"/> permits are free and takes all of them at once.
    /// </summary>
    /// <returns>The number of permits taken, which the caller passes to <see cref="Release"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is negative or larger than the budget, so the wait would never end.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled; no permits are held then.
    /// </exception>
    member _.AcquireAsync (count : int, cancellationToken : CancellationToken) : Task<int> = task {
        validateCount count

        if count = 0 then
            return 0
        else
            // Outside the try: a cancelled wait for the turn must not release a turn it never got
            do! acquisition.WaitAsync cancellationToken
            let mutable acquired = 0

            try
                while acquired < count do
                    do! permits.WaitAsync cancellationToken
                    acquired <- acquired + 1
            finally
                // Only a cancelled wait leaves the loop early: give back what was taken, the caller gets nothing
                if acquired < count && acquired > 0 then
                    permits.Release acquired |> ignore

                acquisition.Release () |> ignore

            return count
    }

    /// <summary>
    /// Gives back <paramref name="count"/> permits that <see cref="AcquireAsync"/> returned.
    /// </summary>
    member _.Release (count : int) : unit =
        validateCount count

        if count > 0 then
            permits.Release count |> ignore
