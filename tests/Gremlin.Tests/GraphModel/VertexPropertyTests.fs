namespace FSharp.Gremlin.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Gremlin

/// The meta-properties of a vertex property in the tests.
type EmployeeAction = { By : string }

/// <summary>
/// Checks <see cref="T:FSharp.Gremlin.VertexProperty`2"/> and the functions that create it.
/// </summary>
[<TestClass; UnitTestCategory; GraphModelTestCategory>]
type VertexPropertyTests () =

    [<TestMethod>]
    member _.``create keeps the value and the meta-properties`` () =
        let created = VertexProperty.create 45 { By = "u1" }

        Assert.AreEqual (45, created.Value, "create should keep the value.")
        Assert.AreEqual ({ By = "u1" }, created.Meta, "create should keep the meta-properties.")

    [<TestMethod>]
    member _.``some gives the vertex property of create in ValueSome`` () =
        Assert.AreEqual (
            ValueSome (VertexProperty.create 45 { By = "u1" }),
            VertexProperty.some 45 { By = "u1" },
            "some should give the vertex property of create in ValueSome."
        )

    [<TestMethod>]
    member _.``a vertex property is a value type with structural equality`` () =
        Assert.IsTrue (
            typeof<VertexProperty<int, EmployeeAction>>.IsValueType,
            "A vertex property should be a struct, so that a record of many vertex properties does not allocate them."
        )

        Assert.AreEqual (
            VertexProperty.create "x" { By = "u1" },
            VertexProperty.create "x" { By = "u1" },
            "Two vertex properties with equal values and equal meta-properties should be equal."
        )

        Assert.AreNotEqual (
            VertexProperty.create "x" { By = "u1" },
            VertexProperty.create "x" { By = "u2" },
            "Two vertex properties with different meta-properties should not be equal."
        )
