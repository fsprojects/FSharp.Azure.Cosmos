namespace FSharp.Azure.Cosmos.Tests

open System
open System.Threading.Tasks
open FSharp.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass; ValidationTestCategory>]
type CosmosNameTests () =

    [<TestMethod>]
    member _.``Validate field throws for null`` () : Task = task {
        let! _ =
            Assert.ThrowsExactlyAsync<ArgumentNullException>(
                Func<Task>(fun () ->
                    CosmosName.validateField Unchecked.defaultof<string>
                    Task.CompletedTask
                ),
                "ValidateField should throw ArgumentNullException for null."
            )
        return ()
    }

    [<TestMethod>]
    member _.``Validate field throws for invalid values`` () : Task = task {
        let invalidFieldNames = [ ""; " "; "1deleted"; "deleted-field" ]

        for fieldName in invalidFieldNames do
            let! _ =
                Assert.ThrowsExactlyAsync<ArgumentException>(
                    Func<Task>(fun () ->
                        CosmosName.validateField fieldName
                        Task.CompletedTask
                    ),
                    $"ValidateField should throw ArgumentException for '{fieldName}'."
                )
            ()

        return ()
    }

    [<TestMethod>]
    member _.``Validate field accepts valid values`` () =
        CosmosName.validateField "_deleted"
        CosmosName.validateField "deletedAt1"
