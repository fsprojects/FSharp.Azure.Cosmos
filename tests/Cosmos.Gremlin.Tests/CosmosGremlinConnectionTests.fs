namespace FSharp.Azure.Cosmos.Gremlin.Tests

open System

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Gremlin
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// Checks the settings that <see cref="M:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnectionModule.azure"/> and
/// <see cref="M:FSharp.Azure.Cosmos.Gremlin.CosmosGremlinConnectionModule.emulator"/> create, the names and the ids that
/// they refuse, and that the text of the settings does not contain the key.
/// </summary>
[<TestClass; UnitTestCategory; ConnectionTestCategory>]
type CosmosGremlinConnectionTests () =

    static let throwsArgumentException (create : unit -> CosmosGremlinConnection) (message : string) =
        Assert.ThrowsExactly<ArgumentException>(Action (fun () -> create () |> ignore), message)
        |> ignore

    [<TestMethod>]
    member _.``azure connects to the Gremlin host of the account with TLS on port 443`` () =
        let connection = CosmosGremlinConnection.azure "my-account-1" "db1" "graph1" "secret"

        Assert.AreEqual (
            "my-account-1.gremlin.cosmos.azure.com",
            connection.Host,
            "The host should be the Gremlin host of the account."
        )
        Assert.AreEqual (443, connection.Port, "An account in Azure should use port 443.")
        Assert.IsTrue (connection.UseSsl, "An account in Azure should use TLS.")
        Assert.AreEqual ("db1", connection.Database, "The settings should keep the database.")
        Assert.AreEqual ("graph1", connection.Graph, "The settings should keep the graph.")
        Assert.AreEqual ("secret", connection.Key, "The settings should keep the key.")

    [<TestMethod>]
    member _.``emulator connects to port 8901 of localhost without TLS with the well-known key`` () =
        let connection = CosmosGremlinConnection.emulator "db1" "graph1"

        Assert.AreEqual ("localhost", connection.Host, "The emulator should be on localhost.")
        Assert.AreEqual (8901, connection.Port, "The Gremlin endpoint of the emulator should be on port 8901.")
        Assert.IsFalse (connection.UseSsl, "The Gremlin endpoint of the emulator does not accept TLS.")
        Assert.AreEqual (CosmosGremlinConnection.EmulatorKey, connection.Key, "The emulator should use its well-known key.")

    [<TestMethod>]
    member _.``the user name is the path of the graph`` () =
        Assert.AreEqual (
            "/dbs/db1/colls/graph1",
            (CosmosGremlinConnection.emulator "db1" "graph1").UserName,
            "The user name should be /dbs/<database>/colls/<graph>."
        )

    [<TestMethod>]
    member _.``the text of the settings shows the endpoint and the graph but not the key`` () =
        let connection = CosmosGremlinConnection.azure "my-account" "db1" "graph1" "the-secret-key"

        for text in [| connection.ToString (); $"%A{connection}"; $"{connection}" |] do
            Assert.DoesNotContain (
                "the-secret-key",
                text,
                StringComparison.Ordinal,
                "The text of the settings should not contain the key."
            )

            Assert.Contains (
                "wss://my-account.gremlin.cosmos.azure.com:443 /dbs/db1/colls/graph1",
                text,
                StringComparison.Ordinal,
                "The text of the settings should show the endpoint and the user name."
            )

    [<TestMethod>]
    [<DataRow("abc", DisplayName = "3 characters")>]
    [<DataRow("my-account-1", DisplayName = "letters, numbers and hyphens")>]
    [<DataRow("1account", DisplayName = "a number first")>]
    member _.``azure accepts the name of a Cosmos DB account`` (account : string) =
        Assert.AreEqual (
            $"{account}.gremlin.cosmos.azure.com",
            (CosmosGremlinConnection.azure account "db" "graph" "key").Host,
            "azure should accept a name that obeys the rule of Azure for account names."
        )

    [<TestMethod>]
    member _.``azure accepts an account name of 44 characters and refuses one of 45`` () =
        let longest = String ('a', 44)

        Assert.AreEqual (
            $"{longest}.gremlin.cosmos.azure.com",
            (CosmosGremlinConnection.azure longest "db" "graph" "key").Host,
            "azure should accept an account name of 44 characters."
        )

        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure (String ('a', 45)) "db" "graph" "key")
            "azure should refuse an account name of 45 characters."

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow("ab", DisplayName = "too short")>]
    [<DataRow("MyAccount", DisplayName = "capital letters")>]
    [<DataRow("my.account", DisplayName = "a dot")>]
    [<DataRow("-account", DisplayName = "a hyphen first")>]
    [<DataRow("my_account", DisplayName = "an underscore")>]
    [<DataRow("myaccount.gremlin.cosmos.azure.com", DisplayName = "the whole host")>]
    [<DataRow("account\n", DisplayName = "a line feed at the end")>]
    member _.``azure refuses a name that is not the name of a Cosmos DB account`` (account : string) =
        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure account "db" "graph" "key")
            "azure should refuse a name that does not obey the rule of Azure for account names."

    [<TestMethod>]
    member _.``azure refuses a null account name that a caller in C# can give`` () =
        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure Unchecked.defaultof<string> "db" "graph" "key")
            "azure should refuse a null account name with an ArgumentException, not with an exception of the regular expression."

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow(" ", DisplayName = "only whitespace")>]
    [<DataRow("db/1", DisplayName = "a slash")>]
    [<DataRow("db\\1", DisplayName = "a backslash")>]
    [<DataRow("db?1", DisplayName = "a question mark")>]
    [<DataRow("db#1", DisplayName = "a number sign")>]
    [<DataRow("db ", DisplayName = "a space at the end")>]
    member _.``azure and emulator refuse an id that Cosmos DB does not allow`` (id : string) =
        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure "account" id "graph" "key")
            "azure should refuse the id of the database."

        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure "account" "db" id "key")
            "azure should refuse the id of the graph."

        throwsArgumentException
            (fun () -> CosmosGremlinConnection.emulator id "graph")
            "emulator should refuse the id of the database."
        throwsArgumentException (fun () -> CosmosGremlinConnection.emulator "db" id) "emulator should refuse the id of the graph."

    [<TestMethod>]
    member _.``azure and emulator accept an id of 255 characters and refuse one of 256`` () =
        let longest = String ('d', 255)

        Assert.AreEqual (
            longest,
            (CosmosGremlinConnection.emulator longest "graph").Database,
            "emulator should accept an id of 255 characters."
        )
        Assert.AreEqual (
            longest,
            (CosmosGremlinConnection.azure "account" "db" longest "key").Graph,
            "azure should accept an id of 255 characters."
        )

        throwsArgumentException
            (fun () -> CosmosGremlinConnection.emulator (String ('d', 256)) "graph")
            "emulator should refuse an id of 256 characters."

    [<TestMethod>]
    [<DataRow("", DisplayName = "empty")>]
    [<DataRow(" ", DisplayName = "only whitespace")>]
    member _.``azure refuses an empty key`` (key : string) =
        throwsArgumentException
            (fun () -> CosmosGremlinConnection.azure "account" "db" "graph" key)
            "azure should refuse an empty key."
