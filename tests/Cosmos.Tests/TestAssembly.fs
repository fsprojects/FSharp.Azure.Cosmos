namespace FSharp.Azure.Cosmos.Tests

open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests.Integration

/// <summary>
/// The assembly-level hooks of this test project. MSTest allows one <see cref="AssemblyInitializeAttribute"/> method
/// per assembly, so everything that has to run once before the first test belongs to <see cref="Initialize"/>.
/// <para>
/// <see cref="Initialize"/> and <see cref="Cleanup"/> both run <see cref="Emulator.deleteLeftoverDatabasesAsync"/>,
/// which deletes only the test databases that stayed unmodified for longer than the leftover age
/// (<see cref="Emulator.readLeftoverAge"/>), an hour unless the environment variable
/// <c>COSMOS_TEST_LEFTOVER_AGE_MINUTES</c>, <see cref="Emulator.LeftoverAgeVariable"/>, says otherwise, so that another
/// test process that uses the same emulator at the same time keeps its live databases. Every test deletes its own
/// database in <see cref="DatabaseTestApplicationFactory.CleanupAsync"/>, whatever its age.
/// </para>
/// <para>
/// The age does not keep apart two test processes that run the same test at the same time: the identifier of a test's
/// database is stable, so both use one database and either may delete it under the other.
/// </para>
/// </summary>
[<TestClass>]
type TestAssembly () =

    /// <summary>
    /// Deletes the test databases older than the leftover age that earlier runs left behind, aborted ones or ones whose
    /// cleanup failed, whose containers would otherwise keep occupying emulator partitions while this run creates its
    /// own. A younger one is kept, because it may belong to a test process that is still running; a test of this run
    /// whose database is among them recreates it empty.
    /// </summary>
    [<AssemblyInitialize>]
    static member Initialize (testContext : TestContext) : Task = Emulator.deleteLeftoverDatabasesAsync testContext

    /// <summary>
    /// Deletes the test databases older than the leftover age that are left once every test has run. This run's own
    /// databases are younger: each test has deleted its database in its cleanup already, and one whose best-effort
    /// cleanup failed is left to a later sweep once it is old enough.
    /// </summary>
    [<AssemblyCleanup>]
    static member Cleanup (testContext : TestContext) : Task = Emulator.deleteLeftoverDatabasesAsync testContext
