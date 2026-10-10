namespace FSharp.Azure.Cosmos.Tests

open System
open System.IO
open System.Text.Json.Nodes
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Emulator-free coverage of the golden baselines: how <see cref="Baseline.compare"/> matches, reports and rewrites a
/// baseline, how <see cref="Baseline.normalizeSql"/> ignores layout, and how <see cref="Baseline.directoryOf"/> finds
/// the baselines of a test project.
/// </summary>
[<TestClass; TestInfrastructureUnitTestCategory>]
type BaselineTests () =

    // Gives a test a directory of its own, deleted afterwards
    static let inTemporaryDirectory (test : string -> unit) =
        let directory = Path.Combine (Path.GetTempPath (), "fsac-baseline-tests", Guid.NewGuid().ToString "N")
        Directory.CreateDirectory directory |> ignore

        try
            test directory
        finally
            Directory.Delete (directory, true)

    static let write (path : string) (content : string) =
        Directory.CreateDirectory (nonNull (Path.GetDirectoryName path))
        |> ignore
        File.WriteAllText (path, content)

    static let parameters (json : string) = (nonNull (JsonNode.Parse json)).AsObject()

    [<TestMethod>]
    member _.``normalizeSql collapses every run of whitespace into one space and trims both ends`` () =
        Assert.AreEqual (
            "SELECT VALUE c FROM c WHERE c.n > @n",
            Baseline.normalizeSql "\n  SELECT VALUE c\r\n\tFROM c\n    WHERE  c.n > @n  \n",
            "normalizeSql should turn line breaks, tabs and repeated spaces into single spaces and drop them at both ends."
        )

    [<TestMethod>]
    member _.``compare matches a baseline that differs only in layout`` () =
        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.sql")) "SELECT VALUE c\nFROM c\nWHERE c.n > @n\n"
            write (Path.Combine (directory, "Suite", "case.params.json")) "{\n  \"@n\": 1\n}\n"

            let comparison =
                Baseline.compare
                    false
                    directory
                    "Suite"
                    "case"
                    "SELECT VALUE c FROM c WHERE c.n > @n"
                    (parameters """{"@n": 1}""")

            Assert.AreEqual (
                Baseline.Comparison.Matches,
                comparison,
                "compare should match SQL and parameters that differ from the baseline only in layout."
            )
        )

    [<TestMethod>]
    member _.``compare reports different SQL in its normalised form`` () =
        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.sql")) "SELECT VALUE c\nFROM c\nWHERE c.n > @n\n"
            write (Path.Combine (directory, "Suite", "case.params.json")) """{"@n": 1}"""

            let comparison =
                Baseline.compare
                    false
                    directory
                    "Suite"
                    "case"
                    "SELECT VALUE c FROM c WHERE c.n >= @n"
                    (parameters """{"@n": 1}""")

            Assert.AreEqual (
                Baseline.Comparison.Differs (
                    "SELECT VALUE c FROM c WHERE c.n > @n\n{\"@n\":1}",
                    "SELECT VALUE c FROM c WHERE c.n >= @n\n{\"@n\":1}"
                ),
                comparison,
                "compare should report SQL that differs from the baseline, both sides normalised."
            )
        )

    [<TestMethod>]
    member _.``compare reports different parameters`` () =
        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.sql")) "SELECT VALUE c FROM c WHERE c.n > @n"
            write (Path.Combine (directory, "Suite", "case.params.json")) """{"@n": 1}"""

            let comparison =
                Baseline.compare
                    false
                    directory
                    "Suite"
                    "case"
                    "SELECT VALUE c FROM c WHERE c.n > @n"
                    (parameters """{"@n": 2}""")

            Assert.AreEqual (
                Baseline.Comparison.Differs (
                    "SELECT VALUE c FROM c WHERE c.n > @n\n{\"@n\":1}",
                    "SELECT VALUE c FROM c WHERE c.n > @n\n{\"@n\":2}"
                ),
                comparison,
                "compare should report parameters that differ from the baseline."
            )
        )

    [<TestMethod>]
    member _.``compare matches a query without parameters against a baseline without a parameter file`` () =
        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.sql")) "SELECT VALUE 1"

            Assert.AreEqual (
                Baseline.Comparison.Matches,
                Baseline.compare false directory "Suite" "case" "SELECT VALUE 1" (JsonObject ()),
                "compare should match a query without parameters when the baseline has no parameter file."
            )
        )

    [<TestMethod>]
    member _.``compare reports a missing baseline with the path of its SQL file`` () =
        inTemporaryDirectory (fun directory ->
            Assert.AreEqual (
                Baseline.Comparison.Missing (Path.Combine (directory, "Suite", "case.sql")),
                Baseline.compare false directory "Suite" "case" "SELECT VALUE 1" (JsonObject ()),
                "compare should report a baseline that does not exist yet."
            )
        )

    [<TestMethod>]
    member _.``compare rewrites a baseline that the next comparison matches`` () =
        inTemporaryDirectory (fun directory ->
            let sqlPath = Path.Combine (directory, "Suite", "case.sql")
            let parametersPath = Path.Combine (directory, "Suite", "case.params.json")
            let sql = "SELECT VALUE c\nFROM c\nWHERE c.n > @n"

            Assert.AreEqual (
                Baseline.Comparison.Rewritten sqlPath,
                Baseline.compare true directory "Suite" "case" sql (parameters """{"@n": 1}"""),
                "compare should report that it rewrote the baseline."
            )

            Assert.AreEqual ($"{sql}\n", File.ReadAllText sqlPath, "The rewritten SQL file should keep the layout of the SQL.")

            Assert.AreEqual (
                "{\n  \"@n\": 1\n}\n",
                File.ReadAllText parametersPath,
                "The rewritten parameter file should hold the parameters as indented JSON with LF line breaks."
            )

            Assert.AreEqual (
                Baseline.Comparison.Matches,
                Baseline.compare false directory "Suite" "case" sql (parameters """{"@n": 1}"""),
                "compare should match the baseline it has just rewritten."
            )
        )

    [<TestMethod>]
    member _.``compare removes the parameter file of a baseline it rewrites for a query without parameters`` () =
        inTemporaryDirectory (fun directory ->
            let parametersPath = Path.Combine (directory, "Suite", "case.params.json")
            write parametersPath """{"@old": 1}"""

            Baseline.compare true directory "Suite" "case" "SELECT VALUE 1" (JsonObject ())
            |> ignore

            Assert.IsFalse (
                File.Exists parametersPath,
                "Rewriting the baseline of a query without parameters should remove its old parameter file."
            )
        )

    [<TestMethod>]
    [<DataRow("a/b", DisplayName = "a slash")>]
    [<DataRow("a:b", DisplayName = "a colon")>]
    [<DataRow(" ", DisplayName = "only whitespace")>]
    member _.``compare rejects a case name that cannot be part of a file name`` (caseName : string) =
        Assert.ThrowsExactly<ArgumentException>(
            Action (fun () ->
                Baseline.compare false (Path.GetTempPath ()) "Suite" caseName "SELECT VALUE 1" (JsonObject ())
                |> ignore
            ),
            "compare should reject a case name that cannot be part of a file name on every platform."
        )
        |> ignore

    [<TestMethod>]
    member _.``directoryOf finds the Baselines directory next to the project file of the test assembly`` () =
        let directory = Baseline.directoryOf typeof<BaselineTests>.Assembly

        Assert.AreEqual ("Baselines", Path.GetFileName directory, "directoryOf should return a directory named Baselines.")

        Assert.IsTrue (
            File.Exists (Path.Combine (nonNull (Path.GetDirectoryName directory), "FSharp.Azure.Cosmos.Tests.fsproj")),
            "directoryOf should return the Baselines directory next to the project file of the test assembly."
        )
