namespace FSharp.Azure.Cosmos.Tests

open System
open System.Collections.Generic

open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests.Integration

/// <summary>
/// Emulator-free coverage of <see cref="ContainerSet"/>, in which a fixture declares its containers.
/// </summary>
[<TestClass; UnitTestCategory; TestInfrastructureTestCategory>]
type ContainerSetTests () =

    static let container (id : string) = ContainerProperties (id, "/partitionKey")

    // The identifiers in ordinal order, because the set has no defined order. The array has its final size from the
    // start and is sorted in place. Thus no intermediate collection exists.
    static let ids (set : ContainerSet) =
        let ids = Array.zeroCreate<string> set.Count
        let mutable index = 0

        for container in set do
            ids[index] <- container.Id
            index <- index + 1

        Array.Sort (ids, StringComparer.Ordinal)
        ids

    [<TestMethod>]
    member _.``Empty has no container`` () = Assert.IsEmpty (ContainerSet.Empty, "The empty set should have no container.")

    [<TestMethod>]
    member _.``Create without a container gives the empty set`` () =
        let set = ContainerSet.Create ()

        Assert.IsEmpty (set, "A set created without a container should have no container.")

    [<TestMethod>]
    member _.``Create keeps the containers when their identifiers differ`` () =
        let set = ContainerSet.Create (container "orders", container "customers")

        Assert.HasCount (2, set, "The set should have both containers.")
        CollectionAssert.AreEqual ([| "customers"; "orders" |], ids set, "The set should enumerate both containers.")

    [<TestMethod>]
    member _.``Create throws and names the container when two definitions have the same identifier`` () =
        let error =
            Assert.ThrowsExactly<ArgumentException>(
                (fun () ->
                    ContainerSet.Create (container "orders", container "customers", container "orders")
                    |> ignore
                ),
                "Create should throw when a container is declared twice."
            )

        Assert.Contains ("'orders'", error.Message, StringComparison.Ordinal, "The message should name the container.")

    [<TestMethod>]
    member _.``Add throws when the settings of the second definition differ`` () =
        let first = container "orders"
        let second = ContainerProperties ("orders", "/partitionKey", DefaultTimeToLive = Nullable 60)
        let set = ContainerSet.Create first

        Assert.ThrowsExactly<ArgumentException>(
            (fun () -> set.Add second |> ignore),
            "Add should throw although the second definition differs, because CreateContainerIfNotExistsAsync would ignore its settings."
        )
        |> ignore

    [<TestMethod>]
    member _.``Add leaves the set unchanged and returns a set with the container`` () =
        let set = ContainerSet.Create (container "orders")

        let extended = set.Add (container "customers")

        Assert.ContainsSingle (set, "Add should not change the set it is called on.")
        |> ignore
        CollectionAssert.AreEqual (
            [| "customers"; "orders" |],
            ids extended,
            "Add should return the containers of the set and the new one."
        )

    [<TestMethod>]
    member _.``Identifiers are compared case-sensitively`` () =
        let set = ContainerSet.Create (container "orders", container "Orders")

        Assert.HasCount (2, set, "Identifiers that differ in case should name two containers.")

    // Count is a member of the set, and ContainerCount of a fixture uses it, while HasCount counts the enumerated
    // containers. Thus this test compares the two.
    [<TestMethod>]
    member _.``Count is the number of the containers that the set enumerates`` () =
        let set = ContainerSet.Create (container "orders", container "customers")

        let collection = set :> IReadOnlyCollection<ContainerProperties>

        Assert.HasCount (2, collection, "The collection view should enumerate both containers.")
        Assert.AreEqual (Seq.length collection, set.Count, "Count of the set should equal the number of its containers.")
        Assert.AreEqual (
            Seq.length collection,
            collection.Count,
            "Count of the collection view should equal the number of its containers."
        )
