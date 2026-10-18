namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos
open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// The values that the functions of the tests capture. A test writes its functions over two sets of them.
type CapturedValues = {
    Limit : int
    Name : string
    Filter : string option
    Flag : bool
    Nick : string option
    Tags : string list
    Range : AgeRange
    Comparison : StringComparison
}

/// <summary>
/// A function of a person as a quotation, with the check that another quotation computes what its compiled code does.
/// </summary>
type PersonFunction = {
    /// What the function is made of; the name of a test result starts with it.
    Name : string
    /// The quotation of the function as it is written.
    Quotation : Expr
    /// Fails unless the function that a quotation holds computes what the compiled code of this function does.
    ComputesTheSame : Expr -> unit
} with

    /// <summary>
    /// The function <paramref name="code"/>. The compiler passes the code together with its quotation.
    /// </summary>
    static member Of<'Result when 'Result : equality>
        (name : string, [<ReflectedDefinition(true)>] code : Expr<Person -> 'Result>)
        : PersonFunction
        =
        let code = Quoted.Split code

        {
            Name = name
            Quotation = code.Quotation
            ComputesTheSame = NormalizerAssert.ComputesTheSame code.Compiled Persons.all
        }

/// The functions that the laws of the parameterizer are checked on.
[<RequireQualifiedAccess>]
module PersonFunctions =

    /// <summary>
    /// Functions of a person written over <paramref name="captured"/>, every one of a shape of its own: no two of
    /// them may give one query text. Two calls give the same functions in the same order, with other values.
    /// </summary>
    let over (captured : CapturedValues) : ImmutableArray<PersonFunction> =
        // Locals, because a local arrives in a quotation as a value with the name of its variable.
        let limit = captured.Limit
        let name = captured.Name
        let filter = captured.Filter
        let flag = captured.Flag
        let nick = captured.Nick
        let tags = captured.Tags
        let range = captured.Range
        let comparison = captured.Comparison

        seq {
            // Literals
            PersonFunction.Of ("a literal", (fun p -> p.Age > 18))
            PersonFunction.Of ("another literal", (fun p -> p.Age > 21))
            PersonFunction.Of ("a literal of another type", (fun p -> int64 p.Age > 18L))
            PersonFunction.Of ("a zero", (fun p -> float p.Age > 0.0))
            PersonFunction.Of ("a negative zero", (fun p -> float p.Age > -0.0))
            PersonFunction.Of ("a string", (fun p -> p.Name = "Bob"))
            PersonFunction.Of ("an empty string", (fun p -> p.Name = ""))
            PersonFunction.Of ("null", (fun p -> box p.Name = null))
            PersonFunction.Of ("a character", (fun p -> p.Name.Contains 'o'))
            PersonFunction.Of ("a value of an enumeration", (fun p -> p.Name.StartsWith ("b", StringComparison.Ordinal)))
            PersonFunction.Of (
                "another value of the enumeration",
                (fun p -> p.Name.StartsWith ("b", StringComparison.OrdinalIgnoreCase))
            )

            // Captured values
            PersonFunction.Of ("a captured value", (fun p -> p.Age > limit))
            PersonFunction.Of ("a captured value on the other side", (fun p -> limit < p.Age))
            PersonFunction.Of ("a captured value that is used twice", (fun p -> p.Age > limit || fst p.Scores > limit))
            PersonFunction.Of ("two captured values", (fun p -> p.Name = name || fst p.Scores > limit))
            PersonFunction.Of ("a captured value of an enumeration", (fun p -> p.Name.StartsWith (name, comparison)))
            PersonFunction.Of ("a captured option", (fun p -> p.Nick = nick))
            PersonFunction.Of ("a captured list", (fun p -> tags |> List.contains p.Name))
            PersonFunction.Of ("a captured record", (fun p -> { Low = p.Age; High = fst p.Scores } = range))

            // Parts that are computed
            PersonFunction.Of ("a property of a captured value", (fun p -> p.Age >= range.Low))
            PersonFunction.Of ("a call on a captured value", (fun p -> p.Name = name.ToUpperInvariant ()))
            PersonFunction.Of ("arithmetic on a captured value", (fun p -> p.Age <= limit + 1))
            PersonFunction.Of ("a module-level value", (fun p -> p.Age < Conditions.retirementAge))
            PersonFunction.Of ("a static field", (fun p -> p.Name <> String.Empty))
            PersonFunction.Of (
                "an object that a constructor makes",
                (fun p -> DateTime (2000 + p.Age, 1, 1) > DateTime (2000 + limit, 1, 1))
            )

            // Values that the quotation builds
            PersonFunction.Of ("an option with a captured value", (fun p -> p.Nick = Some name))
            PersonFunction.Of ("an option with a literal", (fun p -> p.Nick = Some "bobby"))
            PersonFunction.Of ("an option without a value", (fun p -> p.Nick = None))
            PersonFunction.Of ("a list that is written item by item", (fun p -> [ name; "Dee" ] |> List.contains p.Name))
            PersonFunction.Of ("an array", (fun p -> [| name; "Dee" |] |> Array.contains p.Name))
            PersonFunction.Of ("a longer array", (fun p -> [| name; "Dee"; "Cy" |] |> Array.contains p.Name))
            PersonFunction.Of ("a tuple", (fun p -> p.Scores = (limit, 1)))
            PersonFunction.Of ("a struct tuple", (fun p -> struct (fst p.Scores, snd p.Scores) = struct (limit, 1)))
            PersonFunction.Of ("a record", (fun p -> { Low = p.Age; High = limit } = { Low = 30; High = limit }))
            PersonFunction.Of ("an anonymous record", (fun p -> {| Low = p.Age |} = {| Low = limit |}))
            PersonFunction.Of ("a default value", (fun p -> DateTime (2000 + p.Age, 1, 1) > DateTime ()))

            // Choices
            PersonFunction.Of ("a choice by a captured value", (fun p -> flag || p.Active))
            PersonFunction.Of ("a choice that guards a computed part", (fun p -> filter.IsNone || p.Name = filter.Value.Trim ()))
            PersonFunction.Of (
                "a choice with a computed part in its other branch",
                (fun p -> if flag then p.Age > limit else p.Name = filter.Value)
            )
            PersonFunction.Of ("a choice by the document", (fun p -> if p.Active then p.Age > limit + 1 else p.Age < limit + 2))

            // Other operations on the document
            PersonFunction.Of ("an item of a tuple", (fun p -> fst p.Scores > limit))
            PersonFunction.Of ("another item of a tuple", (fun p -> snd p.Scores > limit))
            PersonFunction.Of (
                "a test for a union case",
                (fun p ->
                    match p.Nick with
                    | Some _ -> p.Active
                    | None -> p.Age > limit
                )
            )
            PersonFunction.Of ("a test for a type", (fun p -> box p.Age :? int))
            PersonFunction.Of ("a value seen as a value of another type", (fun p -> (p.Name :> obj) = (name :> obj)))
            PersonFunction.Of ("an inner function", (fun p -> p.Tags |> List.exists (fun tag -> tag = name)))
            PersonFunction.Of (
                "an inner function that reads the document",
                (fun p -> p.Tags |> List.exists (fun tag -> tag = p.Name))
            )
        }
        |> ImmutableArray.CreateRange

    /// The functions over values with which every choice takes its first branch.
    let first =
        over {
            Limit = 18
            Name = "bob"
            Filter = None
            Flag = true
            Nick = None
            Tags = []
            Range = { Low = 10; High = 9 }
            Comparison = StringComparison.Ordinal
        }

    /// The same functions over other values, with which every choice takes its other branch.
    let second =
        over {
            Limit = 30
            Name = "Bob"
            Filter = Some " Bob "
            Flag = false
            Nick = Some "bobby"
            Tags = [ "Bob"; "Dee" ]
            Range = { Low = 30; High = 10 }
            Comparison = StringComparison.OrdinalIgnoreCase
        }

/// <summary>
/// The laws of <see cref="T:FSharp.Azure.Cosmos.Quotations.Parameterizer"/>: the key of a function does not depend on
/// the values the function captured, two functions that are not alike have different keys, the key is the same whether
/// or not the template is asked for, and the template with its values computes what the function computes.
/// </summary>
[<TestClass; UnitTestCategory; ParameterizerTestCategory>]
type ParameterizerShapeTests () =

    /// <summary>
    /// Every function with both sets of values, as a row of a data-driven test. The name of a row replaces the name of
    /// the test method in the results, so it says what the function is made of and what <paramref name="law"/> the
    /// method asserts.
    /// </summary>
    static member private Rows (law : string) : IEnumerable<TestDataRow<PersonFunction>> = seq {
        for written in PersonFunctions.first do
            TestDataRow<PersonFunction>(written, DisplayName = $"%s{written.Name} %s{law}, with the first values")

        for written in PersonFunctions.second do
            TestDataRow<PersonFunction>(written, DisplayName = $"%s{written.Name} %s{law}, with the second values")
    }

    /// The rows of the test that compares the keys of one function with two sets of values.
    static member SameKeyRows : IEnumerable<TestDataRow<struct (PersonFunction * PersonFunction)>> =
        Seq.zip PersonFunctions.first PersonFunctions.second
        |> Seq.map (fun (struct (first, second)) ->
            TestDataRow<struct (PersonFunction * PersonFunction)>(
                struct (first, second),
                DisplayName = $"%s{first.Name} must have one key whatever values are captured"
            )
        )

    /// The rows of the test that compares what is known without the template with what the template holds.
    static member SameShapeRows = ParameterizerShapeTests.Rows "must have the key and the values of its template"

    /// The rows of the test that looks at what the template holds at the places of the values.
    static member VariableRows =
        ParameterizerShapeTests.Rows "must have a template with a variable for every slot and no captured value"

    /// The rows of the test that compares the template with the compiled code.
    static member SameMeaningRows =
        ParameterizerShapeTests.Rows "must have a template that computes what the compiled code computes"

    [<TestMethod; DynamicData(nameof ParameterizerShapeTests.SameKeyRows)>]
    member _.``A function must have one key whatever values are captured`` (first : PersonFunction, second : PersonFunction) =
        let firstKey = (ParameterizerAssert.WantShape first.Quotation).Key
        let secondKey = (ParameterizerAssert.WantShape second.Quotation).Key

        Assert.AreEqual<ShapeKey>(firstKey, secondKey, "The values a function captured should not be in its key.")

        Assert.AreEqual<int>(
            firstKey.GetHashCode (),
            secondKey.GetHashCode (),
            "Equal keys should have one hash code, or a dictionary does not find the second one."
        )

    [<TestMethod>]
    member _.``Two functions that are not alike must have different keys`` () =
        let keys =
            PersonFunctions.first
            |> Seq.map (fun written -> struct (written.Name, (ParameterizerAssert.WantShape written.Quotation).Key))
            |> Seq.toArray

        let alike =
            seq {
                for index in 0 .. keys.Length - 1 do
                    for other in index + 1 .. keys.Length - 1 do
                        let struct (name, key) = keys[index]
                        let struct (otherName, otherKey) = keys[other]

                        if key = otherKey then
                            $"%s{name} / %s{otherName}"
            }
            |> Seq.toArray

        Assert.IsEmpty (alike, "Functions with one key get one query text, so every pair here would share a wrong one.")

    [<TestMethod; DynamicData(nameof ParameterizerShapeTests.SameShapeRows)>]
    member _.``A function must have the key and the values of its template`` (written : PersonFunction) =
        let shape = ParameterizerAssert.WantShape written.Quotation
        let template = ParameterizerAssert.WantTemplate written.Quotation

        Assert.AreEqual<ShapeKey>(
            template.Key,
            shape.Key,
            "The key that finds a translation should be the key the translation was made under."
        )

        CollectionAssert.AreEqual (
            template.Values |> Seq.toArray,
            shape.Values |> Seq.toArray,
            "The values should be the same, slot by slot."
        )

        Assert.HasCount (template.Slots.Length, shape.Values, "Every slot should have its place among the values.")

    [<TestMethod; DynamicData(nameof ParameterizerShapeTests.VariableRows)>]
    member _.``A function must have a template with a variable for every slot and no captured value`` (written : PersonFunction) =
        let template = ParameterizerAssert.WantTemplate written.Quotation

        let captured =
            template.Body
            |> Quotation.filter (fun part ->
                match part with
                | ValueWithName _ -> true
                | _ -> false
            )

        Assert.IsEmpty (captured, "A captured value is a parameter, so the template should hold a variable at its place.")

        CollectionAssert.AreEquivalent (
            template.Slots |> Seq.map _.Variable |> Seq.toArray,
            template.Body.GetFreeVars () |> Seq.toArray,
            "The variables that the template leaves unbound should be the variables of its slots, every one of them."
        )

    [<TestMethod; DynamicData(nameof ParameterizerShapeTests.SameMeaningRows)>]
    member _.``A function must have a template that computes what the compiled code computes`` (written : PersonFunction) =
        written.Quotation
        |> ParameterizerAssert.WantTemplate
        |> Templates.withValues
        |> written.ComputesTheSame
