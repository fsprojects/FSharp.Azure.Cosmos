namespace FSharp.Gremlin.Tests

open System.Reflection

open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.Azure.Cosmos.Tests
open FSharp.Gremlin

/// A vertex record as an application declares it. The tests read its attributes through reflection.
[<Label "Department">]
type DepartmentVertex = {
    Id : string
    [<PartitionKey>]
    CompanyId : string
    Name : string
}

/// <summary>
/// Checks that the attributes of the graph model land where the model reads them: <see cref="LabelAttribute"/> on the
/// record type, and <see cref="PartitionKeyAttribute"/> on the property that F# generates for a record field.
/// </summary>
[<TestClass; UnitTestCategory; GraphModelTestCategory>]
type AttributeTests () =

    [<TestMethod>]
    member _.``Label on a record type is on the type with its label`` () =
        match typeof<DepartmentVertex>.GetCustomAttribute<LabelAttribute>() with
        | null -> Assert.Fail "The record type should have its LabelAttribute."
        | attribute -> Assert.AreEqual ("Department", attribute.Label, "The attribute should keep the label it got.")

    [<TestMethod>]
    member _.``PartitionKey on a record field is on the property of the field`` () =
        match typeof<DepartmentVertex>.GetProperty(nameof Unchecked.defaultof<DepartmentVertex>.CompanyId) with
        | null -> Assert.Fail "The record type should have a property for the field CompanyId."
        | property ->
            Assert.IsNotNull (
                property.GetCustomAttribute<PartitionKeyAttribute>(),
                "F# should put the attribute of a record field on the property that it generates for the field."
            )

    [<TestMethod>]
    member _.``PartitionKey is only on the marked field`` () =
        let marked =
            typeof<DepartmentVertex>.GetProperties()
            |> Seq.filter (fun property -> not (isNull (property.GetCustomAttribute<PartitionKeyAttribute>())))
            |> Seq.map _.Name
            |> Seq.toArray

        CollectionAssert.AreEqual (
            [| nameof Unchecked.defaultof<DepartmentVertex>.CompanyId |],
            marked,
            "Only the property of the marked field should have the attribute."
        )
