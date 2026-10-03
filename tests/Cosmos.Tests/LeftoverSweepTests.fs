namespace FSharp.Azure.Cosmos.Tests

open System
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting
open Newtonsoft.Json

open FSharp.Azure.Cosmos.Tests.Integration

/// <summary>
/// Emulator-free coverage of how <see cref="Emulator.deleteLeftoverDatabasesAsync"/> tells a leftover test database
/// from the live database of another test process: <see cref="Emulator.isLeftover"/> decides by the time that passed
/// since the database was modified last, which <see cref="Emulator.lastModifiedOf"/> reads.
/// </summary>
[<TestClass; TestInfrastructureUnitTestCategory>]
type LeftoverSweepTests () =

    static let now = DateTimeOffset (2026, 10, 3, 12, 0, 0, TimeSpan.Zero)
    static let oneHour = TimeSpan.FromHours 1.0

    // Through the SDK's own Newtonsoft.Json converter for _ts, so that the kind of the DateTime it produces is covered too
    static let deserializeDatabase (json : string) =
        JsonConvert.DeserializeObject<DatabaseProperties> json
        |> nonNull

    [<TestMethod>]
    [<DataRow(0, DisplayName = "modified at the moment of the sweep")>]
    [<DataRow(1800, DisplayName = "modified half an hour before")>]
    [<DataRow(3599, DisplayName = "modified a second short of the minimum age before")>]
    member _.``isLeftover keeps a database modified more recently than the minimum age`` (secondsBefore : int) =
        Assert.IsFalse (
            Emulator.isLeftover oneHour now (now.AddSeconds (float -secondsBefore)),
            "A database younger than the minimum age may belong to a test process that is still running and must be kept."
        )

    [<TestMethod>]
    [<DataRow(3600, DisplayName = "modified exactly the minimum age before")>]
    [<DataRow(3601, DisplayName = "modified a second longer before")>]
    [<DataRow(86400, DisplayName = "modified a day before")>]
    member _.``isLeftover sweeps a database modified the minimum age before or earlier`` (secondsBefore : int) =
        Assert.IsTrue (
            Emulator.isLeftover oneHour now (now.AddSeconds (float -secondsBefore)),
            "A database that stayed unmodified for the minimum age should be swept as a leftover."
        )

    [<TestMethod>]
    member _.``isLeftover with a zero minimum age sweeps a database modified at the moment of the sweep`` () =
        Assert.IsTrue (
            Emulator.isLeftover TimeSpan.Zero now now,
            "A zero minimum age should sweep every test database that exists when the sweep begins."
        )

    [<TestMethod>]
    member _.``isLeftover keeps a database modified after the moment of the sweep even with a zero minimum age`` () =
        // A database created while the sweep lists the databases, or stamped by an emulator clock that runs ahead
        Assert.IsFalse (
            Emulator.isLeftover TimeSpan.Zero now (now.AddSeconds 1.0),
            "A database modified after the sweep began should never be swept."
        )

    [<TestMethod>]
    member _.``lastModifiedOf reads the _ts system property as UTC seconds since the Unix epoch`` () =
        // Reading the SDK's DateTime as local time would shift the instant by this machine's offset
        let database = deserializeDatabase """{ "id": "fsac-test-database", "_ts": 1790000000 }"""

        Assert.AreEqual (
            ValueSome (DateTimeOffset.FromUnixTimeSeconds 1790000000L),
            Emulator.lastModifiedOf database,
            "The last modification should be the instant the _ts seconds denote."
        )

    [<TestMethod>]
    member _.``lastModifiedOf returns ValueNone when the database reports no _ts`` () =
        let database = deserializeDatabase """{ "id": "fsac-test-database" }"""

        Assert.AreEqual (
            ValueNone,
            Emulator.lastModifiedOf database,
            "A database without _ts should have no last modification, so that the sweep keeps it."
        )
