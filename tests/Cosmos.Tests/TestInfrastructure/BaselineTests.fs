namespace FSharp.Azure.Cosmos.Tests

open System
open System.IO
open System.Text.Json.Nodes
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Emulator-free coverage of the golden baselines: how <see cref="Baseline.compare"/> and
/// <see cref="Baseline.compareText"/> match, report and rewrite a baseline, how <see cref="Baseline.normalizeSql"/>
/// ignores layout and <see cref="Baseline.normalizeText"/> keeps it, and how <see cref="Baseline.directoryOf"/> finds
/// the baselines of a test project.
/// </summary>
[<TestClass; UnitTestCategory; TestInfrastructureTestCategory>]
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

    // When the tests rewrite their baselines, an assertion writes the baseline instead of comparing it. Thus a test of
    // the comparison of an assertion is inconclusive then.
    static let comparingOnly () =
        if Baseline.isRewriting () then
            Assert.Inconclusive
                $"{Baseline.RewriteVariable} is set, so the assertions write their baselines instead of comparing them."

    [<TestMethod>]
    member _.``normalizeSql collapses every run of whitespace into one space and trims both ends`` () =
        Assert.AreEqual (
            "SELECT VALUE c FROM c WHERE c.n > @n",
            Baseline.normalizeSql "\n  SELECT VALUE c\r\n\tFROM c\n    WHERE  c.n > @n  \n",
            "normalizeSql should turn line breaks, tabs and repeated spaces into single spaces and drop them at both ends."
        )

    [<TestMethod>]
    member _.``normalizeText changes the line breaks into line feeds and only trims both ends`` () =
        Assert.AreEqual (
            "g.V() // all\n  .hasLabel('P')\n\n\t.limit(1)",
            Baseline.normalizeText "\r\n  g.V() // all\r  .hasLabel('P')\r\n\r\n\t.limit(1)  \n",
            "normalizeText should turn CR LF and CR into LF, keep every other whitespace, and drop the whitespace at both ends."
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
    [<DataRow("Suite.", DisplayName = "a dot at the end")>]
    [<DataRow("Suite ", DisplayName = "a space at the end")>]
    member _.``compare rejects a suite name that Windows changes`` (suite : string) =
        Assert.ThrowsExactly<ArgumentException>(
            Action (fun () ->
                Baseline.compare false (Path.GetTempPath ()) suite "case" "SELECT VALUE 1" (JsonObject ())
                |> ignore
            ),
            "compare should reject a suite name that ends with a dot or a space, because Windows removes it from the name of the directory."
        )
        |> ignore

    [<TestMethod>]
    member _.``compareText writes and reads the text file with the extension that it gets`` () =
        inTemporaryDirectory (fun directory ->
            let scriptPath = Path.Combine (directory, "Suite", "case.groovy")
            let script = "g.V().hasLabel('Person')\n  .has('age', gt(_a))"

            Assert.AreEqual (
                Baseline.Comparison.Rewritten scriptPath,
                Baseline.compareText "groovy" true directory "Suite" "case" script (parameters """{"_a": 30}"""),
                "compareText should write the text into a file with the extension that it gets."
            )

            Assert.AreEqual (
                Baseline.Comparison.Matches,
                Baseline.compareText "groovy" false directory "Suite" "case" script (parameters """{"_a": 30}"""),
                "compareText should match the baseline that it has just written."
            )

            Assert.AreEqual (
                Baseline.Comparison.Missing (Path.Combine (directory, "Suite", "case.sql")),
                Baseline.compare false directory "Suite" "case" script (parameters """{"_a": 30}"""),
                "compare should look for a SQL file, not for the text file of another extension."
            )
        )

    [<TestMethod>]
    member _.``compareText keeps the line breaks of a text`` () =
        inTemporaryDirectory (fun directory ->
            // A Groovy line comment ends at the line break. Without it, the comment hides the step after it.
            write (Path.Combine (directory, "Suite", "case.groovy")) "g.V() // all\n.hasLabel('P')\n"

            Assert.AreEqual (
                Baseline.Comparison.Differs ("g.V() // all\n.hasLabel('P')\n{}", "g.V() // all .hasLabel('P')\n{}"),
                Baseline.compareText "groovy" false directory "Suite" "case" "g.V() // all .hasLabel('P')" (JsonObject ()),
                "compareText should report a text that differs from the baseline only in a line break."
            )
        )

    [<TestMethod>]
    member _.``compareText ignores the kind of line break and the whitespace at both ends`` () =
        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.groovy")) "g.V()\r\n  .hasLabel('P')\r\n"

            Assert.AreEqual (
                Baseline.Comparison.Matches,
                Baseline.compareText "groovy" false directory "Suite" "case" "\n g.V()\n  .hasLabel('P')  " (JsonObject ()),
                "compareText should match a text that differs from the baseline only in its line breaks and at both ends."
            )
        )

    [<TestMethod>]
    member _.``assertMatches compares a query as compare compares it`` () =
        comparingOnly ()

        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.sql")) "SELECT VALUE c\nFROM c\n"

            // The test fails if assertMatches does not collapse the whitespace of the two queries
            Baseline.assertMatches directory "Suite" "case" "SELECT VALUE c FROM c" (JsonObject ())
        )

    [<TestMethod>]
    member _.``assertTextMatches fails the test for a text that differs from its baseline in a line break`` () =
        comparingOnly ()

        inTemporaryDirectory (fun directory ->
            write (Path.Combine (directory, "Suite", "case.groovy")) "g.V() // all\n.hasLabel('P')\n"

            Assert.ThrowsExactly<AssertFailedException>(
                Action (fun () ->
                    Baseline.assertTextMatches "groovy" directory "Suite" "case" "g.V() // all .hasLabel('P')" (JsonObject ())
                ),
                "assertTextMatches should fail the test when the text differs from its baseline in a line break."
            )
            |> ignore
        )

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow(".groovy", DisplayName = "a leading dot")>]
    [<DataRow("groovy.", DisplayName = "a dot at the end")>]
    [<DataRow("a/b", DisplayName = "a slash")>]
    [<DataRow("params.json", DisplayName = "the extension of the parameter file")>]
    [<DataRow("PARAMS.JSON", DisplayName = "the extension of the parameter file in capital letters")>]
    [<DataRow("params.json.", DisplayName = "the extension of the parameter file with a dot at the end")>]
    [<DataRow("params.json ", DisplayName = "the extension of the parameter file with a space at the end")>]
    member _.``compareText rejects an extension that cannot be the extension of the text file`` (extension : string) =
        Assert.ThrowsExactly<ArgumentException>(
            Action (fun () ->
                Baseline.compareText extension false (Path.GetTempPath ()) "Suite" "case" "g.V()" (JsonObject ())
                |> ignore
            ),
            "compareText should reject an extension that does not give a text file of its own."
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
