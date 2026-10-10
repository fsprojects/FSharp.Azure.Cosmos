namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Collections.Immutable
open System.Collections.ObjectModel
open System.Globalization
open System.IO
open System.Net
open System.Text
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Azure.Cosmos.Tests

/// The answer of the emulator to one query.
type private QueryOutcome =
    /// The query ran and returned these items, in the order of the emulator's pages.
    | Returned of items : JsonArray
    /// The emulator refused to run the query; the substatus is the value of the x-ms-substatus header.
    | Rejected of statusCode : HttpStatusCode * subStatusCode : string * message : string

/// <summary>
/// Assertions on a <see cref="QueryOutcome"/>. JSON is compared after both sides are written compactly, so an expected
/// answer may be laid out for reading.
/// </summary>
[<RequireQualifiedAccess>]
module private QueryAssert =

    let private compact (json : string) = (nonNull (JsonNode.Parse json)).ToJsonString()

    let private itemTexts (items : JsonArray) =
        items
        |> Seq.map (fun item ->
            match item with
            | null -> "null"
            | item -> item.ToJsonString ()
        )
        |> Seq.sort
        |> Seq.toArray

    /// <summary>
    /// Returns the items of a query that ran; fails the test with <paramref name="message"/> when the emulator rejected
    /// the query.
    /// </summary>
    let WantItems (message : string) (outcome : QueryOutcome) : JsonArray =
        match outcome with
        | Returned items -> items
        | Rejected (statusCode, _, error) ->
            Assert.Fail $"{message} The emulator rejected the query with {statusCode}: {error}"
            JsonArray ()

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless the query returned exactly <paramref name="expected"/>, a
    /// JSON array, in the same order.
    /// </summary>
    let Returns (expected : string) (message : string) (outcome : QueryOutcome) =
        Assert.AreEqual (compact expected, (WantItems message outcome).ToJsonString(), message)

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless the query returned the items of <paramref name="expected"/>,
    /// a JSON array, in any order.
    /// </summary>
    let ReturnsInAnyOrder (expected : string) (message : string) (outcome : QueryOutcome) =
        let expectedItems = (nonNull (JsonNode.Parse expected)).AsArray()
        CollectionAssert.AreEqual (itemTexts expectedItems, itemTexts (WantItems message outcome), message)

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless the emulator rejected the query with
    /// <paramref name="expectedStatusCode"/> and an error that contains <paramref name="text"/>.
    /// </summary>
    let RejectsWith (expectedStatusCode : HttpStatusCode) (text : string) (message : string) (outcome : QueryOutcome) =
        match outcome with
        | Returned items -> Assert.Fail $"{message} The query ran and returned {items.ToJsonString ()}."
        | Rejected (statusCode, _, error) ->
            Assert.AreEqual (expectedStatusCode, statusCode, message)
            Assert.Contains (text, error, StringComparison.Ordinal, message)

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless the emulator rejected the query as a bad request whose
    /// error contains <paramref name="text"/>.
    /// </summary>
    let RejectsSaying (text : string) (message : string) (outcome : QueryOutcome) =
        RejectsWith HttpStatusCode.BadRequest text message outcome

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless the emulator rejected the query as a bad request whose
    /// error carries the syntax or semantic error code <paramref name="errorCode"/>, such as <c>SC2203</c>.
    /// </summary>
    /// <remarks>
    /// Only the quoted code is looked for, because the emulators lay out the JSON of the error differently: the Windows
    /// emulator writes it compactly, the vNext emulator indented.
    /// </remarks>
    let Rejects (errorCode : string) (message : string) (outcome : QueryOutcome) =
        RejectsSaying $"\"{errorCode}\"" message outcome

/// <summary>
/// Records how the emulator evaluates the query constructs that the planned translation of F# quotations into Cosmos DB
/// SQL relies on: comparisons with missing and <see langword="null"/> values, the coalesce operator, integer division,
/// string functions with a case flag, parameters in unusual positions, object equality, subqueries, <c>ORDER BY</c> and
/// spatial functions.
/// </summary>
/// <remarks>
/// <para>
/// Each test seeds the documents it needs as raw JSON, so that a document can lack a property, hold an explicit
/// <see langword="null"/> or mix JSON types, and reads the answer through
/// <see cref="M:Microsoft.Azure.Cosmos.Container.GetItemQueryStreamIterator(Microsoft.Azure.Cosmos.QueryDefinition,System.String,Microsoft.Azure.Cosmos.QueryRequestOptions)"/>,
/// so that no serializer stands between the answer and the assertion: a member the answer lacks is undefined. Every
/// query targets one logical partition, so the SDK hands it to the query engine without a cross-partition pipeline of
/// its own.
/// </para>
/// <para>
/// The Windows emulator and the Linux vNext emulator, which CI runs, give the same answers, except where a test states
/// both and tells the emulators apart through <see cref="Emulator.readKindAsync"/>. ADR 0001 records what the
/// translator makes of them.
/// </para>
/// </remarks>
[<TestClass; QuerySemanticsTestCategory>]
type QuerySemanticsTests () =
    inherit IntegrationTestBase ()

    // Every document of these tests lives in this logical partition
    static let partitionKeyValue = "p"

    // A property v of every JSON type, an explicit null, and a document that lacks the property
    static let valuesOfEveryType =
        seq {
            """{"id": "missing"}"""
            """{"id": "null", "v": null}"""
            """{"id": "false", "v": false}"""
            """{"id": "true", "v": true}"""
            """{"id": "zero", "v": 0}"""
            """{"id": "one", "v": 1}"""
            """{"id": "empty", "v": ""}"""
            """{"id": "a", "v": "a"}"""
            """{"id": "array", "v": [3, 1, 2]}"""
            """{"id": "object", "v": {"x": 1, "y": 2}}"""
        }
        |> ImmutableArray.CreateRange

    // Documents with a number, a group and, in the first, an array, for the subquery tests
    static let subqueryDocuments =
        seq {
            """{"id": "a", "n": 1, "g": "x", "arr": [3, 1, 2]}"""
            """{"id": "b", "n": 2, "g": "x"}"""
            """{"id": "c", "n": 3, "g": "y"}"""
        }
        |> ImmutableArray.CreateRange

    // Documents that lack a, b or both, or hold null in a, for the ORDER BY tests over two properties
    static let documentsForTwoSortKeys =
        seq {
            """{"id": "none"}"""
            """{"id": "b1", "b": 1}"""
            """{"id": "aNull", "a": null}"""
            """{"id": "a1", "a": 1}"""
            """{"id": "a1b1", "a": 1, "b": 1}"""
            """{"id": "a2b0", "a": 2, "b": 0}"""
        }
        |> ImmutableArray.CreateRange

    // GeoJSON for the spatial tests: points at the origin, one degree north of it and off the globe, and a square of one
    // degree around the origin
    static let geometries =
        seq {
            struct ("@origin", """{"type": "Point", "coordinates": [0.0, 0.0]}""")
            struct ("@north", """{"type": "Point", "coordinates": [0.0, 1.0]}""")
            struct ("@offTheGlobe", """{"type": "Point", "coordinates": [0.0, 100.0]}""")
            struct ("@square",
                    """{"type": "Polygon", "coordinates": [[[-0.5, -0.5], [0.5, -0.5], [0.5, 0.5], [-0.5, 0.5], [-0.5, -0.5]]]}""")
        }
        |> ImmutableArray.CreateRange

    static let invariant (value : int) = value.ToString CultureInfo.InvariantCulture

    // Passes every geometry as a raw JSON parameter, the form planned for captured geometry values
    static let withGeometries (query : QueryDefinition) =
        for struct (name, json) in geometries do
            query.WithParameterStream (name, new MemoryStream (Encoding.UTF8.GetBytes json))
            |> ignore

        query

    /// <summary>
    /// Creates the container of the running test and seeds <paramref name="documents"/>, JSON objects to which the
    /// partition key property is added.
    /// </summary>
    member private this.SeedAsync (documents : string seq) : Task<Container> =
        this.SeedAsync (ContainerProperties ("query-semantics", "/pk"), documents)

    /// <summary>
    /// Creates the container that <paramref name="containerProperties"/> describe, which must be partitioned by
    /// <c>/pk</c>, and seeds <paramref name="documents"/>, JSON objects to which the partition key property is added.
    /// </summary>
    member private this.SeedAsync (containerProperties : ContainerProperties, documents : string seq) : Task<Container> = task {
        let! container = this.Application.GetOrCreateContainerAsync (containerProperties, this.CancellationToken)

        for document in documents do
            let item = (nonNull (JsonNode.Parse document)).AsObject()
            item["pk"] <- JsonValue.Create partitionKeyValue
            use content = new MemoryStream (Encoding.UTF8.GetBytes (item.ToJsonString ()))

            use! response =
                container.CreateItemStreamAsync (
                    content,
                    PartitionKey partitionKeyValue,
                    cancellationToken = this.CancellationToken
                )

            Assert.AreEqual (HttpStatusCode.Created, response.StatusCode, $"Seeding should create the document {document}.")

        return container
    }

    /// <summary>
    /// Creates a container with the composite index <c>(/a ASC, /b ASC)</c> and seeds the documents of the
    /// <c>ORDER BY</c> tests over two properties.
    /// </summary>
    /// <remarks>
    /// The vNext emulator accepts the composite index but does not build it, because it has no composite indexes yet.
    /// </remarks>
    member private this.SeedWithCompositeIndexAsync () : Task<Container> =
        let indexingPolicy = IndexingPolicy ()

        // CompositeIndexes has no public setter, so the index joins the collection that the policy creates
        indexingPolicy.CompositeIndexes.Add (
            Collection<CompositePath>(
                ResizeArray [|
                    CompositePath (Path = "/a", Order = CompositePathSortOrder.Ascending)
                    CompositePath (Path = "/b", Order = CompositePathSortOrder.Ascending)
                |]
            )
        )

        this.SeedAsync (
            ContainerProperties ("query-semantics-composite", "/pk", IndexingPolicy = indexingPolicy),
            documentsForTwoSortKeys
        )

    /// <summary>
    /// Runs <paramref name="query"/> in the logical partition of the seeded documents and collects the items of every
    /// page, or the answer of the emulator when it refuses the query.
    /// </summary>
    member private this.QueryAsync (container : Container, query : QueryDefinition) : Task<QueryOutcome> = task {
        use iterator =
            container.GetItemQueryStreamIterator (
                query,
                requestOptions = QueryRequestOptions (PartitionKey = PartitionKey partitionKeyValue)
            )

        let items = JsonArray ()
        let mutable rejection = ValueNone

        while rejection.IsNone && iterator.HasMoreResults do
            use! response = iterator.ReadNextAsync this.CancellationToken

            if response.IsSuccessStatusCode then
                let page = nonNull (JsonNode.Parse response.Content)

                for item in (nonNull page["Documents"]).AsArray() do
                    // An item belongs to its page; a copy can join the items of every page
                    items.Add (
                        match item with
                        | null -> null
                        | item -> item.DeepClone ()
                    )
            else
                let error =
                    match response.ErrorMessage with
                    | null -> ""
                    | error -> error

                let subStatusCode =
                    match response.Headers["x-ms-substatus"] with
                    | null -> ""
                    | subStatusCode -> subStatusCode

                rejection <- ValueSome (Rejected (response.StatusCode, subStatusCode, error))

        return rejection |> ValueOption.defaultValue (Returned items)
    }

    [<TestMethod>]
    member this.``Equality between different JSON types is false and only a missing value makes it undefined`` () : Task = task {
        let! container = this.SeedAsync valuesOfEveryType

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition
                    """SELECT VALUE {"id": c.id, "equalsOne": c.v = 1, "differsFromOne": c.v != 1, "equalsNull": c.v = null, "inOneOrA": c.v IN (1, "a")} FROM c ORDER BY c.id"""
            )

        outcome
        |> QueryAssert.Returns
            """[
                {"id": "a",       "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": true},
                {"id": "array",   "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false},
                {"id": "empty",   "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false},
                {"id": "false",   "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false},
                {"id": "missing"},
                {"id": "null",    "equalsOne": false, "differsFromOne": true,  "equalsNull": true,  "inOneOrA": false},
                {"id": "object",  "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false},
                {"id": "one",     "equalsOne": true,  "differsFromOne": false, "equalsNull": false, "inOneOrA": true},
                {"id": "true",    "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false},
                {"id": "zero",    "equalsOne": false, "differsFromOne": true,  "equalsNull": false, "inOneOrA": false}
            ]"""
            "=, != and IN should give false or true between different JSON types, null included, and undefined only for a missing value."
    }

    [<TestMethod>]
    member this.``Ordering is undefined between different JSON types and between arrays or objects`` () : Task = task {
        let! container = this.SeedAsync valuesOfEveryType

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition
                    """SELECT VALUE {"id": c.id, "lessThanOne": c.v < 1, "atLeastOne": c.v >= 1, "lessThanItself": c.v < c.v} FROM c ORDER BY c.id"""
            )

        outcome
        |> QueryAssert.Returns
            """[
                {"id": "a",                                                 "lessThanItself": false},
                {"id": "array"},
                {"id": "empty",                                             "lessThanItself": false},
                {"id": "false",                                             "lessThanItself": false},
                {"id": "missing"},
                {"id": "null",                                              "lessThanItself": false},
                {"id": "object"},
                {"id": "one",     "lessThanOne": false, "atLeastOne": true,  "lessThanItself": false},
                {"id": "true",                                              "lessThanItself": false},
                {"id": "zero",    "lessThanOne": true,  "atLeastOne": false, "lessThanItself": false}
            ]"""
            "< and >= should be defined only between values of the same scalar type, null included, and undefined for arrays, objects and missing values."
    }

    [<TestMethod>]
    member this.``A comparison with a parameter matches neither a missing nor a null value while its negation matches null``
        ()
        : Task
        = task {
        let! container = this.SeedAsync valuesOfEveryType

        let whereWithOne (predicate : string) =
            QueryDefinition($"SELECT VALUE c.id FROM c WHERE {predicate} ORDER BY c.id").WithParameter("@value", 1)

        let! equal = this.QueryAsync (container, whereWithOne "c.v = @value")
        equal
        |> QueryAssert.Returns """["one"]""" "c.v = @value should match only the document whose value equals the parameter."

        let! notEqual = this.QueryAsync (container, whereWithOne "NOT (c.v = @value)")

        notEqual
        |> QueryAssert.Returns
            """["a", "array", "empty", "false", "null", "object", "true", "zero"]"""
            "NOT (c.v = @value) should match every defined value but the parameter's, null included, and no missing value."

        let! different = this.QueryAsync (container, whereWithOne "c.v != @value")

        different
        |> QueryAssert.Returns
            """["a", "array", "empty", "false", "null", "object", "true", "zero"]"""
            "c.v != @value should match exactly what NOT (c.v = @value) matches."
    }

    [<TestMethod>]
    member this.``The coalesce operator replaces a missing value but keeps null`` () : Task = task {
        let! container = this.SeedAsync valuesOfEveryType

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition
                    """SELECT VALUE {"id": c.id, "coalesced": c.v ?? "default"} FROM c WHERE c.id IN ("missing", "null", "zero") ORDER BY c.id"""
            )

        outcome
        |> QueryAssert.Returns
            """[
                {"id": "missing", "coalesced": "default"},
                {"id": "null",    "coalesced": null},
                {"id": "zero",    "coalesced": 0}
            ]"""
            "?? should replace only a missing value; an explicit null and any other value should stay."
    }

    [<TestMethod>]
    member this.``INTDIV and INTMOD truncate toward zero like F# and are undefined for a zero divisor or a fractional operand``
        ()
        : Task
        = task {
        let! container = this.SeedAsync [| """{"id": "item"}""" |]

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition
                    """SELECT VALUE {"7 INTDIV 2": INTDIV(7, 2), "-7 INTDIV 2": INTDIV(-7, 2), "7 INTDIV -2": INTDIV(7, -2), "-7 INTMOD 2": INTMOD(-7, 2), "7 INTMOD -2": INTMOD(7, -2), "7 INTDIV 0": INTDIV(7, 0), "7 INTMOD 0": INTMOD(7, 0), "7.5 INTDIV 2": INTDIV(7.5, 2), "7 INTDIV 2.5": INTDIV(7, 2.5), "7.5 INTMOD 2": INTMOD(7.5, 2), "7 INTMOD 2.5": INTMOD(7, 2.5)} FROM c"""
            )

        // F# computes the expected quotients and remainders. Where F# throws DivideByZeroException, and for a fractional
        // operand on either side of either function, the engine leaves the member undefined.
        let expected =
            $$"""[{"7 INTDIV 2": {{invariant (7 / 2)}}, "-7 INTDIV 2": {{invariant (-7 / 2)}}, "7 INTDIV -2": {{invariant (7 / -2)}}, "-7 INTMOD 2": {{invariant (-7 % 2)}}, "7 INTMOD -2": {{invariant (7 % -2)}}}]"""

        outcome
        |> QueryAssert.Returns
            expected
            "INTDIV and INTMOD should agree with F# / and % on integers and be undefined for a zero divisor or a fractional operand."
    }

    [<TestMethod>]
    member this.``A division by zero with an operator compares as infinity but fails the query when the result is returned``
        ()
        : Task
        = task {
        let! kind = Emulator.readKindAsync this.CancellationToken
        let! container = this.SeedAsync [| """{"id": "item"}""" |]

        let! filtered =
            this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c WHERE 7 / 0 > 1000000")
        filtered
        |> QueryAssert.Returns """["item"]""" "7 / 0 should compare as positive infinity in a filter."

        let! projected =
            this.QueryAsync (container, QueryDefinition """SELECT VALUE {"quotient": 7 / 0} FROM c""")

        // Both emulators fail with the same status and message, but only the Windows emulator sets a substatus
        let expectedSubStatusCode =
            match kind with
            | Emulator.Kind.Windows -> "4001"
            | Emulator.Kind.VNext -> "0"

        match projected with
        | Returned items -> Assert.Fail $"A query that returns 7 / 0 should fail, but it returned {items.ToJsonString ()}."
        | Rejected (statusCode, subStatusCode, error) ->
            Assert.AreEqual (HttpStatusCode.BadRequest, statusCode, "A query that returns 7 / 0 should fail as a bad request.")

            Assert.AreEqual (
                expectedSubStatusCode,
                subStatusCode,
                $"A query that returns 7 / 0 should fail with substatus {expectedSubStatusCode} on the {kind} emulator."
            )

            Assert.Contains (
                "cannot be represented in JSON",
                error,
                StringComparison.Ordinal,
                "A query that returns 7 / 0 should fail because JSON has no infinity."
            )
    }

    [<TestMethod>]
    [<DataRow("STRINGEQUALS", "abc", DisplayName = "STRINGEQUALS")>]
    [<DataRow("CONTAINS", "b", DisplayName = "CONTAINS")>]
    [<DataRow("STARTSWITH", "ab", DisplayName = "STARTSWITH")>]
    [<DataRow("ENDSWITH", "bc", DisplayName = "ENDSWITH")>]
    member this.``A parameter can be the ignore-case flag of a string function`` (functionName : string, text : string) : Task =
        task {
            let! container =
                this.SeedAsync [|
                    """{"id": "lower", "s": "abc"}"""
                    """{"id": "upper", "s": "ABC"}"""
                    """{"id": "other", "s": "xyz"}"""
                |]

            let withIgnoreCase (ignoreCase : bool) =
                QueryDefinition($"SELECT VALUE c.id FROM c WHERE {functionName}(c.s, @text, @ignoreCase) ORDER BY c.id")
                    .WithParameter("@text", text)
                    .WithParameter("@ignoreCase", ignoreCase)

            let! ignoringCase = this.QueryAsync (container, withIgnoreCase true)

            ignoringCase
            |> QueryAssert.Returns """["lower", "upper"]""" $"{functionName} should ignore case when the flag parameter is true."

            let! matchingCase = this.QueryAsync (container, withIgnoreCase false)
            matchingCase
            |> QueryAssert.Returns """["lower"]""" $"{functionName} should match case when the flag parameter is false."
        }

    [<TestMethod>]
    member this.``A boolean parameter can be the whole filter`` () : Task = task {
        let! container = this.SeedAsync [| """{"id": "first"}"""; """{"id": "second"}""" |]

        let whereMatchAll (matchAll : bool) =
            QueryDefinition("SELECT VALUE c.id FROM c WHERE @matchAll ORDER BY c.id").WithParameter("@matchAll", matchAll)

        let! everything = this.QueryAsync (container, whereMatchAll true)
        everything
        |> QueryAssert.Returns """["first", "second"]""" "WHERE @matchAll should match every document when true."

        let! nothing = this.QueryAsync (container, whereMatchAll false)
        nothing
        |> QueryAssert.Returns "[]" "WHERE @matchAll should match no document when false."
    }

    [<TestMethod>]
    member this.``STRINGEQUALS is undefined unless both operands are strings`` () : Task = task {
        let! container =
            this.SeedAsync [|
                """{"id": "missing"}"""
                """{"id": "null", "s": null}"""
                """{"id": "number", "s": 5}"""
                """{"id": "string", "s": "abc"}"""
            |]

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition
                    """SELECT VALUE {"id": c.id, "equals": STRINGEQUALS(c.s, "abc"), "equalsItself": STRINGEQUALS(c.s, c.s)} FROM c ORDER BY c.id"""
            )

        outcome
        |> QueryAssert.Returns
            """[
                {"id": "missing"},
                {"id": "null"},
                {"id": "number"},
                {"id": "string", "equals": true, "equalsItself": true}
            ]"""
            "STRINGEQUALS should be undefined for a missing value, null and a number, even compared with itself."
    }

    [<TestMethod>]
    member this.``The ignore-case flag of STRINGEQUALS folds some letters unlike dotnet OrdinalIgnoreCase`` () : Task = task {
        // Name, the two strings, and whether the Windows emulator, the vNext emulator and .NET OrdinalIgnoreCase call
        // them equal ignoring case. The Windows emulator differs from .NET in the Greek final sigma alone; the vNext
        // emulator also equates the dotted capital I, the Kelvin sign and the Angstrom sign with the letters they
        // lowercase to.
        let pairs = [|
            struct ("ASCII", "abc", "ABC", true, true, true)
            struct ("Cyrillic", "ё", "Ё", true, true, true)
            struct ("Greek sigma", "σ", "Σ", true, true, true)
            struct ("Greek final sigma", "ς", "Σ", false, false, true)
            struct ("German sharp s", "straße", "STRASSE", false, false, false)
            struct ("Turkish dotless i", "ı", "I", false, false, false)
            struct ("Turkish dotted capital I", "i", "İ", false, true, false)
            struct ("Kelvin sign", Char.ConvertFromUtf32 0x212A, "k", false, true, false)
            struct ("Angstrom sign", Char.ConvertFromUtf32 0x212B, "å", false, true, false)
            struct ("Long s", "ſ", "s", false, false, false)
        |]

        for struct (name, left, right, _, _, dotnetEquals) in pairs do
            Assert.AreEqual (
                dotnetEquals,
                String.Equals (left, right, StringComparison.OrdinalIgnoreCase),
                $"The table should state what .NET OrdinalIgnoreCase answers for {name}."
            )

        let! kind = Emulator.readKindAsync this.CancellationToken
        let! container = this.SeedAsync [| """{"id": "item"}""" |]

        let selections =
            pairs
            |> Seq.mapi (fun index struct (name, _, _, _, _, _) -> $"\"{name}\": STRINGEQUALS(@left{index}, @right{index}, true)")
            |> String.concat ", "

        let query = QueryDefinition $"SELECT VALUE {{{selections}}} FROM c"

        pairs
        |> Array.iteri (fun index struct (_, left, right, _, _, _) ->
            query.WithParameter($"@left{index}", left).WithParameter($"@right{index}", right)
            |> ignore
        )

        let! outcome = this.QueryAsync (container, query)

        let answer =
            outcome
            |> QueryAssert.WantItems "STRINGEQUALS with the ignore-case flag should run."
            |> Seq.exactlyOne
            |> nonNull

        // Every pair the emulator folds otherwise than the table states, so that a failure names all of them at once
        let differences = ResizeArray<string>()

        for struct (name, _, _, windowsEquals, vNextEquals, _) in pairs do
            let expected =
                match kind with
                | Emulator.Kind.Windows -> windowsEquals
                | Emulator.Kind.VNext -> vNextEquals

            match answer[name] with
            | null -> differences.Add $"{name}: undefined"
            | equals when equals.GetValue<bool>() <> expected -> differences.Add $"{name}: {equals.GetValue<bool>()}"
            | _ -> ()

        Assert.IsEmpty (
            differences,
            $"STRINGEQUALS with the ignore-case flag should fold case on the {kind} emulator as the table states."
        )
    }

    [<TestMethod>]
    member this.``A parameter can index an object and an array`` () : Task = task {
        let! container = this.SeedAsync [| """{"id": "item", "m": {"x": 1, "y": 2}, "arr": [10, 20, 30]}""" |]

        let! projected =
            this.QueryAsync (
                container,
                QueryDefinition("""SELECT VALUE {"byKey": c.m[@key], "byIndex": c.arr[@index]} FROM c""")
                    .WithParameter("@key", "y")
                    .WithParameter("@index", 1)
            )

        projected
        |> QueryAssert.Returns
            """[{"byKey": 2, "byIndex": 20}]"""
            "A parameter should select a property by name and an element by index."

        let! filtered =
            this.QueryAsync (
                container,
                QueryDefinition("SELECT VALUE c.id FROM c WHERE c.m[@key] = 2").WithParameter("@key", "y")
            )

        filtered
        |> QueryAssert.Returns """["item"]""" "A parameter used as an indexer should work in a filter too."
    }

    [<TestMethod>]
    member this.``Object equality ignores the order of properties but array equality does not`` () : Task = task {
        let! container =
            this.SeedAsync [| """{"id": "object", "v": {"x": 1, "y": 2}}"""; """{"id": "array", "v": [3, 1, 2]}""" |]

        // The parameter goes to the engine as raw JSON, so its property order is the one written here
        let whereEqualTo (json : string) =
            QueryDefinition("SELECT VALUE c.id FROM c WHERE c.v = @value")
                .WithParameterStream("@value", new MemoryStream (Encoding.UTF8.GetBytes json))

        let! reordered = this.QueryAsync (container, whereEqualTo """{"y": 2, "x": 1}""")
        reordered
        |> QueryAssert.Returns """["object"]""" "An object should equal one with the same properties in another order."

        let! subset = this.QueryAsync (container, whereEqualTo """{"x": 1}""")
        subset
        |> QueryAssert.Returns "[]" "An object should not equal one with only some of its properties."

        let! sameOrder = this.QueryAsync (container, whereEqualTo "[3, 1, 2]")
        sameOrder
        |> QueryAssert.Returns """["array"]""" "An array should equal one with the same elements in the same order."

        let! otherOrder = this.QueryAsync (container, whereEqualTo "[1, 2, 3]")
        otherOrder
        |> QueryAssert.Returns "[]" "An array should not equal one with the same elements in another order."

        let! literal =
            this.QueryAsync (container, QueryDefinition """SELECT VALUE c.id FROM c WHERE c.v = {"y": 2, "x": 1}""")
        literal
        |> QueryAssert.Returns """["object"]""" "An object literal should compare like an object parameter."
    }

    [<TestMethod>]
    member this.``ObjectToArray can be searched through ARRAY_CONTAINS and through IN over a subquery`` () : Task = task {
        let! container =
            this.SeedAsync [| """{"id": "item", "m": {"x": 1, "y": 2}}"""; """{"id": "other", "m": {"x": 3}}""" |]

        let! pairs =
            this.QueryAsync (container, QueryDefinition """SELECT VALUE ObjectToArray(c.m) FROM c WHERE c.id = "item" """)

        pairs
        |> QueryAssert.Returns
            """[[{"k": "x", "v": 1}, {"k": "y", "v": 2}]]"""
            "ObjectToArray should turn each property into an object with its name as k and its value as v."

        let! containing =
            this.QueryAsync (
                container,
                QueryDefinition """SELECT VALUE c.id FROM c WHERE ARRAY_CONTAINS(ObjectToArray(c.m), {"v": 2}, true)"""
            )

        containing
        |> QueryAssert.Returns """["item"]""" "ARRAY_CONTAINS with a partial match should find a property value in ObjectToArray."

        let! existing =
            this.QueryAsync (
                container,
                QueryDefinition
                    "SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE kv FROM kv IN (SELECT VALUE ObjectToArray(c.m)) WHERE kv.v = 2)"
            )

        existing
        |> QueryAssert.Returns """["item"]""" "IN over a subquery that returns ObjectToArray should iterate its elements."
    }

    [<TestMethod>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT TOP 2 VALUE c FROM c) AS x", "SC2203", DisplayName = "TOP in a FROM subquery")>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT VALUE c FROM c ORDER BY c.id) AS x",
              "SC2202",
              DisplayName = "ORDER BY in a FROM subquery")>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT VALUE c FROM c OFFSET 1 LIMIT 2) AS x",
              "SC2204",
              DisplayName = "OFFSET LIMIT in a FROM subquery")>]
    [<DataRow("SELECT VALUE c.id FROM c WHERE EXISTS(SELECT TOP 1 VALUE t FROM t IN c.arr)",
              "SC2203",
              DisplayName = "TOP in an EXISTS subquery")>]
    [<DataRow("SELECT VALUE ARRAY(SELECT VALUE t FROM t IN c.arr ORDER BY t) FROM c",
              "SC2202",
              DisplayName = "ORDER BY in an ARRAY subquery")>]
    [<DataRow("SELECT VALUE ARRAY(SELECT VALUE t FROM t IN c.arr OFFSET 1 LIMIT 1) FROM c",
              "SC2204",
              DisplayName = "OFFSET LIMIT in an ARRAY subquery")>]
    [<DataRow("SELECT VALUE (SELECT VALUE t FROM t IN c.arr ORDER BY t) FROM c",
              "SC2202",
              DisplayName = "ORDER BY in a scalar subquery")>]
    member this.``A subquery rejects TOP, ORDER BY and OFFSET LIMIT`` (query : string, errorCode : string) : Task = task {
        let! container = this.SeedAsync [| """{"id": "item", "arr": [3, 1, 2]}""" |]
        let! outcome = this.QueryAsync (container, QueryDefinition query)
        outcome
        |> QueryAssert.Rejects errorCode "The engine should reject the clause inside the subquery."
    }

    [<TestMethod>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT VALUE c FROM c) AS x WHERE x.n > 1",
              """["b", "c"]""",
              DisplayName = "a filter outside a FROM subquery")>]
    [<DataRow("SELECT VALUE x FROM (SELECT VALUE c.n FROM c) AS x WHERE x > 1",
              "[2, 3]",
              DisplayName = "a projection inside a FROM subquery")>]
    [<DataRow("SELECT VALUE x FROM (SELECT DISTINCT VALUE c.g FROM c) AS x",
              """["x", "y"]""",
              DisplayName = "DISTINCT in a FROM subquery")>]
    [<DataRow("SELECT VALUE COUNT(1) FROM (SELECT DISTINCT VALUE c.g FROM c) AS x",
              "[2]",
              DisplayName = "an aggregate over a DISTINCT subquery")>]
    [<DataRow("SELECT VALUE x FROM (SELECT VALUE COUNT(1) FROM c GROUP BY c.g) AS x",
              "[2, 1]",
              DisplayName = "GROUP BY in a FROM subquery")>]
    [<DataRow("SELECT VALUE y FROM (SELECT VALUE x.n FROM (SELECT VALUE c FROM c) AS x) AS y WHERE y > 1",
              "[2, 3]",
              DisplayName = "nested FROM subqueries")>]
    [<DataRow("SELECT TOP 2 VALUE x.id FROM (SELECT VALUE c FROM c) AS x ORDER BY x.n DESC",
              """["c", "b"]""",
              DisplayName = "TOP and ORDER BY outside a FROM subquery")>]
    [<DataRow("SELECT VALUE (SELECT VALUE MAX(t) FROM t IN c.arr) FROM c WHERE IS_DEFINED(c.arr)",
              "[3]",
              DisplayName = "a correlated scalar subquery")>]
    [<DataRow("SELECT VALUE t FROM c JOIN (SELECT VALUE t FROM t IN c.arr WHERE t > 1) AS t",
              "[3, 2]",
              DisplayName = "a subquery as the source of a JOIN")>]
    [<DataRow("SELECT VALUE x.g FROM (SELECT c.g AS g, COUNT(1) AS n FROM c GROUP BY c.g) AS x WHERE x.n > 1",
              """["x"]""",
              DisplayName = "a filter outside a GROUP BY subquery")>]
    [<DataRow("SELECT VALUE COUNT(1) FROM (SELECT c.g AS g FROM c GROUP BY c.g) AS x",
              "[2]",
              DisplayName = "counting the groups of a GROUP BY subquery")>]
    [<DataRow("SELECT VALUE SUM(x.n) FROM (SELECT VALUE c FROM c WHERE c.n > 1) AS x",
              "[5]",
              DisplayName = "an aggregate over a filtered FROM subquery")>]
    [<DataRow("SELECT DISTINCT VALUE x.g FROM (SELECT VALUE c FROM c WHERE c.n > 1) AS x",
              """["x", "y"]""",
              DisplayName = "DISTINCT outside a FROM subquery")>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT VALUE c FROM c) AS x ORDER BY x.n OFFSET 1 LIMIT 1",
              """["b"]""",
              DisplayName = "ORDER BY and OFFSET LIMIT outside a FROM subquery")>]
    member this.``Subqueries without TOP, ORDER BY or OFFSET LIMIT run`` (query : string, expected : string) : Task = task {
        let! container = this.SeedAsync subqueryDocuments
        let! outcome = this.QueryAsync (container, QueryDefinition query)

        outcome
        |> QueryAssert.ReturnsInAnyOrder expected "The subquery should run and return these items."
    }

    [<TestMethod>]
    [<DataRow("SELECT VALUE x.g FROM (SELECT c.g AS g, COUNT(1) AS n FROM c GROUP BY c.g) AS x ORDER BY x.n",
              DisplayName = "an aggregate of a GROUP BY subquery")>]
    [<DataRow("SELECT VALUE x.id FROM (SELECT c.id AS id, c.n * 2 AS m FROM c) AS x ORDER BY x.m",
              DisplayName = "an expression computed in a FROM subquery")>]
    member this.``ORDER BY outside a FROM subquery sorts only by paths of the documents`` (query : string) : Task = task {
        let! container = this.SeedAsync subqueryDocuments
        let! outcome = this.QueryAsync (container, QueryDefinition query)

        outcome
        |> QueryAssert.RejectsSaying
            "ORDER BY item expression could not be mapped to a document path"
            "ORDER BY should reject a value the subquery computes instead of reading it from the document."
    }

    [<TestMethod>]
    [<DataRow("SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE t FROM t IN [1, 2])",
              DisplayName = "an array literal in a subquery")>]
    [<DataRow("SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE kv FROM kv IN ObjectToArray(c.m))",
              DisplayName = "a function call in a subquery")>]
    [<DataRow("SELECT VALUE c.id FROM c JOIN kv IN ObjectToArray(c.m)", DisplayName = "a function call in a JOIN")>]
    member this.``The source of IN must be a path or a subquery`` (query : string) : Task = task {
        let! container = this.SeedAsync [| """{"id": "item", "m": {"x": 1}}""" |]
        let! outcome = this.QueryAsync (container, QueryDefinition query)
        outcome
        |> QueryAssert.Rejects "SC1001" "The engine should reject the source of IN as a syntax error."
    }

    [<TestMethod>]
    member this.``ORDER BY keeps documents without the value and orders JSON types from undefined to objects`` () : Task = task {
        let! container = this.SeedAsync valuesOfEveryType

        let! ascending = this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c ORDER BY c.v")

        ascending
        |> QueryAssert.Returns
            """["missing", "null", "false", "true", "zero", "one", "empty", "a", "array", "object"]"""
            "ORDER BY ASC should return the document without the value first, then null, booleans, numbers, strings, arrays and objects."

        let! descending =
            this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c ORDER BY c.v DESC")

        descending
        |> QueryAssert.Returns
            """["object", "array", "a", "empty", "one", "zero", "true", "false", "null", "missing"]"""
            "ORDER BY DESC should return the exact reverse of ORDER BY ASC."
    }

    [<TestMethod>]
    member this.``ORDER BY over two properties keeps documents without a value and sorts them first`` () : Task = task {
        let! container = this.SeedWithCompositeIndexAsync ()

        let! ascending =
            this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c ORDER BY c.a, c.b")

        ascending
        |> QueryAssert.Returns
            """["none", "b1", "aNull", "a1", "a1b1", "a2b0"]"""
            "ORDER BY c.a, c.b should keep the documents without a or b and sort a missing value before null and numbers in each key."

        let! descending =
            this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c ORDER BY c.a DESC, c.b DESC")

        descending
        |> QueryAssert.Returns
            """["a2b0", "a1b1", "a1", "aNull", "b1", "none"]"""
            "ORDER BY c.a DESC, c.b DESC, the inverse of the composite index, should return the exact reverse of ORDER BY c.a, c.b."
    }

    [<TestMethod>]
    member this.``ORDER BY over two properties in directions that no composite index matches fails on the Windows emulator``
        ()
        : Task
        = task {
        let! kind = Emulator.readKindAsync this.CancellationToken
        let! container = this.SeedWithCompositeIndexAsync ()

        let! outcome =
            this.QueryAsync (container, QueryDefinition "SELECT VALUE c.id FROM c ORDER BY c.a, c.b DESC")

        match kind with
        | Emulator.Kind.Windows ->
            outcome
            |> QueryAssert.RejectsSaying
                "does not have a corresponding composite index"
                "The Windows emulator should refuse ORDER BY c.a, c.b DESC, which the composite index (/a ASC, /b ASC) cannot serve."
        | Emulator.Kind.VNext ->
            // The vNext emulator has no composite indexes, so it sorts without one
            outcome
            |> QueryAssert.Returns
                """["b1", "none", "aNull", "a1b1", "a1", "a2b0"]"""
                "The vNext emulator should sort by c.a ascending, then by c.b descending, without a composite index."
    }

    [<TestMethod>]
    [<DataRow("ROUND(ST_DISTANCE(@origin, @north))", "110574", DisplayName = "ST_DISTANCE in metres")>]
    [<DataRow("ROUND(ST_AREA(@square) / 1000000)", "12309", DisplayName = "ST_AREA in square kilometres")>]
    [<DataRow("[ST_INTERSECTS(@square, @origin), ST_INTERSECTS(@square, @north)]", "[true, false]", DisplayName = "ST_INTERSECTS")>]
    [<DataRow("[ST_WITHIN(@origin, @square), ST_WITHIN(@north, @square)]", "[true, false]", DisplayName = "ST_WITHIN")>]
    [<DataRow("[ST_ISVALID(@north), ST_ISVALID(@offTheGlobe)]", "[true, false]", DisplayName = "ST_ISVALID")>]
    [<DataRow("ST_ISVALIDDETAILED(@offTheGlobe)",
              """{"valid": false, "reason": "Latitude values must be between -90 and 90 degrees."}""",
              DisplayName = "ST_ISVALIDDETAILED")>]
    member this.``Spatial functions measure GeoJSON on the WGS-84 ellipsoid but fail on the vNext emulator``
        (expression : string, expected : string)
        : Task
        = task {
        let! kind = Emulator.readKindAsync this.CancellationToken
        let! container = this.SeedAsync [| """{"id": "item"}""" |]

        let! outcome =
            this.QueryAsync (
                container,
                QueryDefinition $"SELECT VALUE {expression} FROM c"
                |> withGeometries
            )

        match kind with
        | Emulator.Kind.Windows ->
            outcome
            |> QueryAssert.Returns
                $"[{expected}]"
                $"{expression} should measure in metres on the WGS-84 ellipsoid and test the points against the square."
        | Emulator.Kind.VNext ->
            outcome
            |> QueryAssert.RejectsWith
                HttpStatusCode.InternalServerError
                "This query type isn't supported yet"
                $"The vNext emulator should refuse {expression}, because it has no spatial functions yet."
    }

    [<TestMethod>]
    member this.``Spatial functions filter stored GeoJSON points but the vNext emulator answers without documents`` () : Task =
        task {
            let! kind = Emulator.readKindAsync this.CancellationToken

            let! container =
                this.SeedAsync [|
                    """{"id": "origin", "location": {"type": "Point", "coordinates": [0.0, 0.0]}}"""
                    """{"id": "north", "location": {"type": "Point", "coordinates": [0.0, 1.0]}}"""
                    """{"id": "far", "location": {"type": "Point", "coordinates": [10.0, 10.0]}}"""
                |]

            let near =
                QueryDefinition "SELECT VALUE c.id FROM c WHERE ST_DISTANCE(c.location, @origin) < 200000 ORDER BY c.id"
                |> withGeometries

            let within =
                QueryDefinition "SELECT VALUE c.id FROM c WHERE ST_WITHIN(c.location, @square) ORDER BY c.id"
                |> withGeometries

            match kind with
            | Emulator.Kind.Windows ->
                let! nearOutcome = this.QueryAsync (container, near)

                nearOutcome
                |> QueryAssert.Returns
                    """["north", "origin"]"""
                    "ST_DISTANCE should keep the stored points within 200 km of the origin."

                let! withinOutcome = this.QueryAsync (container, within)

                withinOutcome
                |> QueryAssert.Returns """["origin"]""" "ST_WITHIN should keep only the stored point inside the square."
            | Emulator.Kind.VNext ->
                // Unlike a spatial function in the projection, which fails the request, a spatial filter gets an answer
                // without documents, which the SDK cannot read
                for query in [| near; within |] do
                    let! error =
                        Assert.ThrowsExactlyAsync<InvalidOperationException>(
                            Func<Task>(fun () -> this.QueryAsync (container, query) :> Task),
                            "The SDK should fail to read the answer of the vNext emulator to a spatial filter."
                        )

                    Assert.Contains (
                        "QueryResponse did not have property: Documents",
                        error.Message,
                        StringComparison.Ordinal,
                        "The SDK should fail because the answer has no documents."
                    )
        }
