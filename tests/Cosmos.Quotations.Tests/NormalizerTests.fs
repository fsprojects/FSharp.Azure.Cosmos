namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.DerivedPatterns
open FSharp.Quotations.Patterns

open Swensen.Unquote

open FSharp.Azure.Cosmos.Tests

/// <summary>
/// What the normalizer leaves in a quotation and what it must not touch: the facts that a pair of spellings in
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.NormalizerSpellingTests"/> cannot state.
/// </summary>
[<TestClass; UnitTestCategory; NormalizerTestCategory>]
type NormalizerTests () =

    /// Whether a quotation reads the property of a value that has this name.
    static let reads (name : string) (expr : Expr) : bool =
        match expr with
        | PropertyGet (Some _, property, []) -> String.Equals (property.Name, name, StringComparison.Ordinal)
        | _ -> false

    /// Whether a quotation is a sum.
    static let isSum (expr : Expr) : bool =
        match expr with
        | SpecificCall <@@ (+) @@> _ -> true
        | _ -> false

    [<TestMethod>]
    member _.``A quotation in normal form must be returned as the same node`` () =
        let quotation = <@ fun (p : Person) -> p.Active && p.Name = "Bob" @>

        Assert.AreSame<Expr>(quotation, NormalizerAssert.WantNormal quotation, "Nothing in the quotation is to be rewritten.")

    [<TestMethod>]
    member _.``A captured boolean must stay in a comparison because it is a parameter and not a constant`` () =
        let enabled = true
        let quotation = <@ fun (p : Person) -> p.Active = enabled @>

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "The comparison with a captured value should stay: the text of a query must not depend on the value."
        )

    [<TestMethod>]
    member _.``A module-level value with a reflected definition must stay a reference to the value`` () =
        let quotation = <@ fun (p : Person) -> p.Age >= Conditions.retirementAge @>

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "A module-level value is computed once, so its definition should not be written into the quotation."
        )

    [<TestMethod>]
    member _.``A module-level value that is a function of the unit value must stay a reference to the value`` () =
        let quotation = <@ fun (p : Person) -> p.Age >= Conditions.earlyRetirementAge () @>

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "The definition of the value has the type of a getter that takes the unit value, and is no getter."
        )

    [<TestMethod>]
    member _.``A call of a function without a reflected definition must stay a call`` () =
        let quotation = <@ fun (p : Person) -> Conditions.isTeenager p && p.IsRetired () @>

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "A function whose definition cannot be read should stay a call."
        )

    [<TestMethod>]
    member _.``A call of a member that a derived class can override must stay a call`` () =
        let quotation = <@ fun (document : DocumentBase) -> document.Describe () = "invoice a" @>

        let describe =
            match quotation with
            | Lambda (_, Call (_, _, [ Call (_, method, _); _ ])) -> method
            | _ -> invalidOp $"The quotation should compare the result of a call: %A{quotation}"

        Assert.IsTrue (
            (Expr.TryGetReflectedDefinition describe).IsSome,
            "The member should have a reflected definition, or the test checks nothing."
        )

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "The definition in the base class says nothing about a derived class that overrides the member."
        )

    [<TestMethod>]
    member _.``A call that passes witnesses must become a plain call of the same operator`` () =
        let quotation = <@ fun (p : Person) -> p.Age + 1 > 18 @>

        let passesWitnesses (expr : Expr) =
            match expr with
            | CallWithWitnesses _ -> true
            | _ -> false

        Assert.IsTrue (
            Quotation.exists passesWitnesses quotation,
            "The compiler should pass a witness to the addition, or the test checks nothing."
        )

        let normal = NormalizerAssert.WantNormal quotation

        Assert.IsFalse (Quotation.exists passesWitnesses normal, "No call should pass witnesses after normalization.")
        normal
        |> NormalizerAssert.ComputesTheSame (fun (p : Person) -> p.Age + 1 > 18) Persons.all

    [<TestMethod>]
    member _.``A struct anonymous record parameter must be read without its address`` () =
        let condition =
            Quoted.Of (fun (limits : struct {| MinAge : int |}) (p : Person) -> p.Age > limits.MinAge)

        let takesAddress (expr : Expr) =
            match expr with
            | AddressOf _ -> true
            | _ -> false

        Assert.IsTrue (
            Quotation.exists takesAddress condition.Quotation,
            "The compiler should read the struct record through its address, or the test checks nothing."
        )

        let normal = NormalizerAssert.WantNormal condition.Quotation
        let limits = struct {| MinAge = 18 |}

        Assert.IsFalse (Quotation.exists takesAddress normal, "No address should be left after normalization.")

        Expr.Application (normal, Expr.Value limits)
        |> NormalizerAssert.ComputesTheSame (condition.Compiled limits) Persons.all

    [<TestMethod>]
    member _.``The copy of a struct that the compiler binds must be gone`` () =
        let condition = Quoted.Of (fun (date : DateTime) -> date.AddDays(1.0).Year > 2000)

        let binds (expr : Expr) =
            match expr with
            | Let _ -> true
            | _ -> false

        Assert.IsTrue (
            Quotation.exists binds condition.Quotation,
            "The compiler should bind a copy of the date, or the test checks nothing."
        )

        let normal = NormalizerAssert.WantNormal condition.Quotation

        Assert.IsFalse (Quotation.exists binds normal, "No binding should be left after normalization.")

        normal
        |> NormalizerAssert.ComputesTheSame condition.Compiled [| DateTime (1999, 12, 31); DateTime (2000, 12, 31) |]

    [<TestMethod>]
    member _.``The mutable copy of a struct that the compiler binds must be gone when nothing assigns to it`` () =
        let condition =
            Quoted.Of (fun (invoice : Invoice) -> invoice.Note.IsSome && invoice.Note.Value = "paid")

        let bindsMutable (expr : Expr) =
            match expr with
            | Let (variable, _, _) -> variable.IsMutable
            | _ -> false

        Assert.IsTrue (
            Quotation.exists bindsMutable condition.Quotation,
            "The compiler should bind the note to a mutable variable, or the test checks nothing."
        )

        let normal = NormalizerAssert.WantNormal condition.Quotation

        Assert.IsFalse (Quotation.exists bindsMutable normal, "No binding should be left after normalization.")

        normal
        |> NormalizerAssert.ComputesTheSame condition.Compiled [|
            Invoice ("a", 50)
            Invoice ("b", 50, Note = ValueSome "paid")
            Invoice ("c", 50, Note = ValueSome "open")
        |]

    [<TestMethod>]
    member _.``A bound value that is used twice must be the same node at both places`` () =
        let quotation =
            <@
                fun (p : Person) ->
                    let year = DateTime.UtcNow.Year
                    p.Age < year && p.Age + 1900 < year
            @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode
            2
            (reads "Year")
            "Both places should hold the node that was bound, so that a later stage can tell they are one value."

    [<TestMethod>]
    member _.``A value that a function binds must be one node in a call and another node in another call`` () =
        let quotation =
            <@
                fun (p : Person) ->
                    Conditions.isWithinProcessors p
                    || not (Conditions.isWithinProcessors p)
            @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.AreNodes
            2
            4
            (fun expr ->
                match expr with
                | PropertyGet (None, property, []) -> String.Equals (property.Name, "ProcessorCount", StringComparison.Ordinal)
                | _ -> false
            )
            "A call computes the number once for its two places, and the other call computes it for itself."

    [<TestMethod>]
    member _.``An argument that a function uses twice must be the same node at both places`` () =
        let quotation =
            <@ fun (p : Person) -> (fun year -> p.Age < year && p.Age + 1900 < year) DateTime.UtcNow.Year @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode 2 (reads "Year") "The code computes an argument once, however often the function uses it."

    [<TestMethod>]
    member _.``Values bound one after another must each be the same node at all their places`` () =
        let quotation =
            <@
                fun (p : Person) ->
                    let year = DateTime.UtcNow.Year
                    let month = DateTime.UtcNow.Month
                    (p.Age < year && p.Age > month)
                    || (fst p.Scores < year && fst p.Scores > month)
            @>

        let normal = NormalizerAssert.WantNormal quotation

        normal
        |> NormalizerAssert.IsOneNode 2 (reads "Year") "Writing the month into the quotation should leave the year one node."

        normal
        |> NormalizerAssert.IsOneNode 2 (reads "Month") "Writing the year into the quotation should leave the month one node."

    [<TestMethod>]
    member _.``A bound value that reads another bound value must stay one node`` () =
        let quotation =
            <@
                fun (p : Person) ->
                    let year = DateTime.UtcNow.Year
                    let next = year + 1
                    p.Age < next && fst p.Scores < next
            @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode
            2
            isSum
            "The sum is bound once, so writing the year into it should give one sum for both places and not one for each."

    [<TestMethod>]
    member _.``A bound value must stay one node when a later rewrite changes it`` () =
        // Writing the pair into the sum leaves the reading of an item of a pair that is built in place, which is
        // rewritten to that item afterwards, at both places of the sum.
        let quotation =
            <@
                fun (p : Person) ->
                    let pair = (p.Age, fst p.Scores)
                    let sum = fst pair + snd pair
                    sum > 1 && sum < 100
            @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode 2 isSum "The sum is bound once, so both places should get the same rewritten sum."

    [<TestMethod>]
    member _.``A bound function that is passed twice must stay one node`` () =
        let quotation =
            <@
                fun (p : Person) ->
                    let name = Environment.MachineName
                    let isIt = fun (tag : string) -> tag = name
                    List.exists isIt p.Tags || List.forall isIt p.Tags
            @>

        let isFunctionOfTag (expr : Expr) =
            match expr with
            | Lambda (parameter, _) -> Type.(=) (parameter.Type, typeof<string>)
            | _ -> false

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode
            2
            isFunctionOfTag
            "Writing the name into the function should give one function for both places, with one reading of the name."

    [<TestMethod>]
    member _.``A quotation that is spliced into two places must stay one node when it is rewritten`` () =
        // The spliced quotation is not in normal form: it binds a value.
        let year =
            <@
                let now = DateTime.UtcNow
                now.Year
            @>

        let quotation = <@ fun (p : Person) -> p.Age < %year && p.Age + 1900 < %year @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode
            2
            (reads "Year")
            "A splice puts the node it is given at both places, and both should get the one normal form of that node."

    [<TestMethod>]
    member _.``A quotation that is spliced into two places must stay one node when a value is written around it`` () =
        let year = <@ DateTime.UtcNow.Year @>

        let quotation =
            <@
                fun (p : Person) ->
                    let century = 1900
                    p.Age < %year && p.Age + century < %year
            @>

        NormalizerAssert.WantNormal quotation
        |> NormalizerAssert.IsOneNode
            2
            (reads "Year")
            "Writing the bound number into the quotation should leave the spliced node one node."

    [<TestMethod>]
    member _.``A variable that an inner function binds again must not be replaced inside that function`` () =
        // One variable that two functions bind, one inside the other: a quotation that is spliced into itself arrives
        // this way, because every splice of it is the same nodes.
        //     (fun number -> List.exists (fun number -> number > 6) [ 1 ] || number > 9) 7
        let number = Var ("number", typeof<int>)
        let isLarge = Expr.Lambda (number, <@@ %%(Expr.Var number) > 6 @@>)
        let body =
            <@@
                List.exists (%%isLarge : int -> bool) [ 1 ]
                || %%(Expr.Var number) > 9
            @@>
        let normal = NormalizerAssert.WantNormal (Expr.Application (Expr.Lambda (number, body), <@@ 7 @@>))

        Assert.IsFalse (
            evalRaw<bool> normal,
            "The inner function tests its own argument, 1, and not the 7 that the outer function was applied to."
        )

    [<TestMethod>]
    member _.``A function parameter must be renamed when a value with the same variable is moved under it`` () =
        // The argument is the variable that the inner function binds too, so writing it into the inner function as it
        // is would make it the inner parameter.
        //     fun number -> (fun first -> fun number -> first - number) number
        let number = Var ("number", typeof<int>)
        let first = Var ("first", typeof<int>)
        let subtract =
            Expr.Lambda (first, Expr.Lambda (number, <@@ %%(Expr.Var first) - %%(Expr.Var number) @@>))
        let normal =
            NormalizerAssert.WantNormal (Expr.Lambda (number, Expr.Application (subtract, Expr.Var number)))

        Assert.AreEqual<int>(
            7,
            evalRaw<int -> int -> int> normal 10 3,
            "The first argument should stay the outer parameter: 10 - 3, not 3 - 3."
        )

    [<TestMethod>]
    member _.``A delegate parameter must be renamed when a value with the same variable is moved under it`` () =
        // The same with a delegate as the inner function. A quotation keeps the parameters of a delegate in a function
        // under the node of the delegate, so they are bindings like any other.
        //     fun number -> (fun first -> Func<int, int> (fun number -> first - number)) number
        let number = Var ("number", typeof<int>)
        let first = Var ("first", typeof<int>)
        let subtract =
            Expr.Lambda (
                first,
                Expr.NewDelegate (typeof<Func<int, int>>, [ number ], <@@ %%(Expr.Var first) - %%(Expr.Var number) @@>)
            )

        match NormalizerAssert.WantNormal (Expr.Lambda (number, Expr.Application (subtract, Expr.Var number))) with
        | Lambda (outer, NewDelegate (_, [ inner ], Call (None, _, [ Var minuend; Var subtrahend ]))) ->
            Assert.AreNotSame<Var>(outer, inner, "The parameter of the delegate should be a variable of its own.")
            Assert.AreSame<Var>(outer, minuend, "The first argument should stay the outer parameter.")
            Assert.AreSame<Var>(inner, subtrahend, "The second argument should stay the parameter of the delegate.")
        | normal -> Assert.Fail $"The normal form should be a function that gives a delegate, but is %A{normal}."

    [<TestMethod>]
    member _.``A call of an inline method without its witnesses must stay a call`` () =
        // The compiler passes an inline method its operators as witnesses, and the definition of the method takes them
        // first. A call that is built through the API passes none, so the definition does not take its arguments.
        let method =
            match <@ fun (p : Person) -> p.IsOlderThanTwice 9 @> with
            | Lambda (_, Call (Some _, method, _)) -> method
            | other -> invalidOp $"The quotation should be a call of the method: %A{other}"

        let person = Var ("p", typeof<Person>)
        let quotation = Expr.Lambda (person, Expr.Call (Expr.Var person, method, [ Expr.Value 9 ]))

        Assert.AreSame<Expr>(
            quotation,
            NormalizerAssert.WantNormal quotation,
            "A definition is applied only to the arguments it takes, so this call should be left as it is and nothing raised."
        )

    [<TestMethod>]
    member _.``A quotation that carries its compiled value must be normalized as the quotation`` () =
        let condition = Quoted.Of (fun (p : Person) -> p |> Conditions.isAdult)
        let carried = Expr.WithValue (condition.Compiled, typeof<Person -> bool>, condition.Quotation)

        NormalizerAssert.AreEquivalent
            <@ fun (p : Person) -> p.Age >= 18 @>
            (NormalizerAssert.WantNormal carried)
            "The compiled value should be dropped and the quotation normalized."

    [<TestMethod>]
    member _.``A composition must keep the parameter name of its first function`` () =
        let quotation =
            <@
                (fun (person : Person) -> person.Age)
                >> (fun age -> age > 18)
            @>

        match NormalizerAssert.WantNormal quotation with
        | Lambda (parameter, _) ->
            Assert.AreEqual<string>(
                "person",
                parameter.Name,
                "The name the user chose should survive, because it becomes the alias in the query."
            )
        | normal -> Assert.Fail $"The normal form of a composition should be a function, but is %A{normal}."
