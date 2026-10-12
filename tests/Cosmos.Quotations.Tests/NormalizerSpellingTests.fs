namespace FSharp.Azure.Cosmos.Quotations.Tests

open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations

/// <summary>
/// One way to write a function, next to the plain way to write the same function: with operators and members of the
/// document only. The normalizer has to make one quotation of both.
/// </summary>
type Spelling = {
    /// What is special about the way the function is written; the name of a test result starts with it.
    Name : string
    /// The quotation of the function as it is written.
    Written : Expr
    /// The quotation of the plain way to write it.
    Plain : Expr
    /// Fails unless the function that a quotation holds computes what the compiled code of the written function does.
    ComputesTheSame : Expr -> unit
} with

    /// <summary>
    /// The spelling <paramref name="written"/> of the function that <paramref name="plain"/> writes plainly, to be
    /// evaluated on <paramref name="inputs"/>.
    /// </summary>
    static member Over<'Input, 'Result when 'Result : equality>
        (
            name : string,
            inputs : 'Input seq,
            [<ReflectedDefinition(true)>] written : Expr<'Input -> 'Result>,
            plain : Expr<'Input -> 'Result>
        )
        : Spelling
        =
        let written = Quoted.Split written

        {
            Name = name
            Written = written.Quotation
            Plain = plain
            ComputesTheSame = NormalizerAssert.ComputesTheSame written.Compiled inputs
        }

    /// <summary>
    /// The spelling <paramref name="written"/> of the function of a person that <paramref name="plain"/> writes plainly.
    /// </summary>
    static member Of<'Result when 'Result : equality>
        (name : string, [<ReflectedDefinition(true)>] written : Expr<Person -> 'Result>, plain : Expr<Person -> 'Result>)
        : Spelling
        =
        let written = Quoted.Split written

        {
            Name = name
            Written = written.Quotation
            Plain = plain
            ComputesTheSame = NormalizerAssert.ComputesTheSame written.Compiled Persons.all
        }

    /// <summary>
    /// The spelling <paramref name="written"/> of the function of a person that <paramref name="plain"/> writes plainly,
    /// for a way of writing that only a quotation literal has, such as a splice: the compiler cannot give the code of
    /// such a quotation, so <paramref name="compiled"/> is the same function as code.
    /// </summary>
    static member OfQuotation<'Result when 'Result : equality>
        (name : string, written : Expr<Person -> 'Result>, compiled : Person -> 'Result, plain : Expr<Person -> 'Result>)
        : Spelling
        = {
        Name = name
        Written = written
        Plain = plain
        ComputesTheSame = NormalizerAssert.ComputesTheSame compiled Persons.all
    }

/// <summary>
/// The normal form of a function does not depend on how the function is written: every
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.Spelling"/> has the normal form of its plain way, the normal form
/// stays what it is when it is normalized again, and it computes what the compiled code computes.
/// </summary>
[<TestClass; NormalizerUnitTestCategory>]
type NormalizerSpellingTests () =

    static let spellings =
        // Captured by the quotations below: a local value arrives in a quotation as a named value, which is how a
        // parameter of a query is written.
        let limit = 30
        let invoices = [| Invoice ("a", 50); Invoice ("", 150) |]
        let nicks = [| None; Some "x"; Some "y" |]

        // Spliced into the quotations below. Every splice of one quotation is the same nodes, variables included.
        let isAdult = <@ fun (person : Person) -> person.Age >= 18 @>

        let hasTagThat =
            <@ fun (predicate : string -> bool) (person : Person) -> person.Tags |> List.exists (fun tag -> predicate tag) @>

        seq {
            // Pipes
            Spelling.Of (
                "a forward pipe into a function written in place",
                (fun p -> p.Age |> fun age -> age > 18),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "a forward pipe into a function",
                (fun p -> p |> Conditions.isTeenager),
                <@ fun p -> Conditions.isTeenager p @>
            )
            Spelling.Of (
                "a forward pipe into a partly applied function",
                (fun p -> p.Tags |> List.contains "vip"),
                <@ fun p -> List.contains "vip" p.Tags @>
            )
            Spelling.Of (
                "a chain of forward pipes",
                (fun p ->
                    p.Tags
                    |> List.filter (fun tag -> tag <> "")
                    |> List.isEmpty
                    |> not
                ),
                <@ fun p -> not (List.isEmpty (List.filter (fun tag -> tag <> "") p.Tags)) @>
            )
            Spelling.Of ("a backward pipe", (fun p -> not <| (p.Age > 18)), <@ fun p -> not (p.Age > 18) @>)
            Spelling.Of ("a forward pipe of two values", (fun p -> (p.Age, 18) ||> (>)), <@ fun p -> p.Age > 18 @>)
            Spelling.Of (
                "a forward pipe of three values",
                (fun p ->
                    (p.Age, 18, 65)
                    |||> (fun age low high -> age >= low && age <= high)
                ),
                <@ fun p -> p.Age >= 18 && p.Age <= 65 @>
            )
            Spelling.Of ("a backward pipe of two values", (fun p -> (>) <|| (p.Age, 18)), <@ fun p -> p.Age > 18 @>)
            Spelling.Of (
                "a backward pipe of three values",
                (fun p ->
                    (fun age low high -> age >= low && age <= high)
                    <||| (p.Age, 18, 65)
                ),
                <@ fun p -> p.Age >= 18 && p.Age <= 65 @>
            )
            Spelling.Of (
                "a pipe of two values that are one tuple of the document",
                (fun p -> p.Scores ||> (>)),
                <@
                    fun p ->
                        let (best, worst) = p.Scores
                        best > worst
                @>
            )

            // Spliced quotations
            Spelling.OfQuotation (
                "a quotation that is spliced in and applied",
                <@ fun p -> (%isAdult) p && p.Active @>,
                (fun p -> p.Age >= 18 && p.Active),
                <@ fun p -> p.Age >= 18 && p.Active @>
            )
            Spelling.OfQuotation (
                "an untyped quotation that is spliced in and applied",
                <@ fun p -> (%%isAdult.Raw : Person -> bool) p @>,
                (fun p -> p.Age >= 18),
                <@ fun p -> p.Age >= 18 @>
            )
            Spelling.OfQuotation (
                "a quotation that is spliced in twice",
                <@ fun p -> (%isAdult) p || not ((%isAdult) p) @>,
                (fun p -> p.Age >= 18 || not (p.Age >= 18)),
                <@ fun p -> p.Age >= 18 || not (p.Age >= 18) @>
            )
            Spelling.OfQuotation (
                "a quotation that is spliced into its own argument",
                <@ fun p -> (%hasTagThat) (fun outer -> (%hasTagThat) (fun inner -> inner <> outer) p) p @>,
                (fun p ->
                    p.Tags
                    |> List.exists (fun outer -> p.Tags |> List.exists (fun inner -> inner <> outer))
                ),
                <@ fun p -> List.exists (fun outer -> List.exists (fun inner -> inner <> outer) p.Tags) p.Tags @>
            )

            // Functions that are not written as a function
            Spelling.Of ("a function that is the whole function", Conditions.isAdult, <@ fun p -> p.Age >= 18 @>)
            Spelling.Of (
                "a partly applied function that is the whole function",
                Conditions.isOlderThan 18,
                <@ fun p -> p.Age > 18 @>
            )

            // Composition
            Spelling.Of (
                "a forward composition that is applied",
                (fun p -> (Conditions.isAdult >> not) p),
                <@ fun p -> not (p.Age >= 18) @>
            )
            Spelling.Of (
                "a backward composition that is applied",
                (fun p -> (not << Conditions.isAdult) p),
                <@ fun p -> not (p.Age >= 18) @>
            )
            Spelling.Of ("a composition that is the whole function", Conditions.isAdult >> not, <@ fun p -> not (p.Age >= 18) @>)
            Spelling.Of (
                "a composition of three functions",
                (fun (p : Person) -> p.Age) >> Conditions.twice >> ((<) 50),
                <@ fun p -> 50 < p.Age + p.Age @>
            )

            // Bindings
            Spelling.Of (
                "a value that is bound once and used twice",
                (fun p ->
                    let age = p.Age
                    age > 18 && age < 65
                ),
                <@ fun p -> p.Age > 18 && p.Age < 65 @>
            )
            Spelling.Of (
                "a tuple pattern",
                (fun p ->
                    let (low, high) = (18, 65)
                    p.Age >= low && p.Age <= high
                ),
                <@ fun p -> p.Age >= 18 && p.Age <= 65 @>
            )
            Spelling.Of (
                "a struct tuple pattern",
                (fun p ->
                    let struct (low, high) = struct (18, 65)
                    p.Age >= low && p.Age <= high
                ),
                <@ fun p -> p.Age >= 18 && p.Age <= 65 @>
            )
            Spelling.Of (
                "a variable that hides another of the same name",
                (fun p ->
                    let age = p.Age
                    let age = age + 1
                    age > 18
                ),
                <@ fun p -> p.Age + 1 > 18 @>
            )
            Spelling.Of (
                "an operator that is applied to its first argument only",
                (fun p -> p.Age |> (<) 18),
                <@ fun p -> 18 < p.Age @>
            )
            Spelling.Of (
                "a match on an option that is built in place",
                (fun p ->
                    match Some p.Age with
                    | Some age -> age > 18
                    | None -> false
                ),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "a match on a value option that is built in place",
                (fun p ->
                    match ValueSome p.Age with
                    | ValueSome age -> age > 18
                    | ValueNone -> false
                ),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Over (
                "a match that binds the value of an option",
                nicks,
                (fun nick ->
                    match nick with
                    | Some value -> value = "x"
                    | None -> false
                ),
                <@
                    fun nick ->
                        match nick with
                        | Some _ -> nick.Value = "x"
                        | None -> false
                @>
            )

            // Functions and members with a reflected definition
            Spelling.Of ("a function", (fun p -> Conditions.isAdult p), <@ fun p -> p.Age >= 18 @>)
            Spelling.Of (
                "a curried function with a captured argument",
                (fun p -> Conditions.isOlderThan limit p),
                <@ fun p -> p.Age > limit @>
            )
            Spelling.Of (
                "a function of a tuple and a person",
                (fun p -> Conditions.isBetween (18, 65) p),
                <@ fun p -> p.Age >= 18 && p.Age <= 65 @>
            )
            Spelling.Of ("a function of one tuple", (fun p -> Conditions.isNamed ("Bob", p)), <@ fun p -> p.Name = "Bob" @>)
            Spelling.Of ("a function of the unit value", (fun p -> p.Age >= Conditions.adultAge ()), <@ fun p -> p.Age >= 18 @>)
            Spelling.Of ("a generic function", (fun p -> Conditions.hasValue p.Nick), <@ fun p -> p.Nick.IsSome @>)
            Spelling.Of (
                "an inline function with an operator",
                (fun p -> Conditions.twice p.Age > 50),
                <@ fun p -> p.Age + p.Age > 50 @>
            )
            Spelling.Of ("an inline method with an operator", (fun p -> p.IsOlderThanTwice 9), <@ fun p -> p.Age > 9 + 9 @>)
            Spelling.Of (
                "an inline static method with an operator",
                (fun p -> Person.Doubled p.Age > 50),
                <@ fun p -> p.Age + p.Age > 50 @>
            )
            Spelling.Of (
                "a function that calls other functions",
                (fun p -> Conditions.isWorkingAge p),
                <@ fun p -> p.Age >= 18 && not (p.Age > 64) @>
            )
            Spelling.Of (
                "a function that takes a function",
                (fun p -> p |> Conditions.hasTagThat (fun tag -> tag = "vip")),
                <@ fun p -> List.exists (fun tag -> tag = "vip") p.Tags @>
            )
            Spelling.Of (
                "a function that takes a function, inside its own argument",
                (fun p ->
                    p
                    |> Conditions.hasTagThat (fun outer -> p |> Conditions.hasTagThat (fun inner -> inner <> outer))
                ),
                <@ fun p -> List.exists (fun outer -> List.exists (fun inner -> inner <> outer) p.Tags) p.Tags @>
            )
            Spelling.Of (
                "a function that takes a struct anonymous record",
                (fun p ->
                    p
                    |> Conditions.isOlderThanMinimum (struct {| MinAge = 18 |})
                ),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "a function that matches on a union, called with the first case",
                (fun p -> p |> Conditions.satisfies (ByName "Bob")),
                <@ fun p -> p.Name = "Bob" @>
            )
            Spelling.Of (
                "a function that matches on a union, called with the second case",
                (fun p -> p |> Conditions.satisfies (OlderThan 18)),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of ("a method", (fun p -> p.IsOlderThan 18), <@ fun p -> p.Age > 18 @>)
            Spelling.Of ("a method of two arguments", (fun p -> p.IsBetween (18, 65)), <@ fun p -> p.Age >= 18 && p.Age <= 65 @>)
            Spelling.Of ("a curried method", (fun p -> p.IsWithin 18 65), <@ fun p -> p.Age >= 18 && p.Age <= 65 @>)
            Spelling.Of ("a property", (fun p -> p.IsAdult), <@ fun p -> p.Age >= 18 @>)
            Spelling.Of ("a static method", (fun p -> Person.IsNamed ("Bob", p)), <@ fun p -> p.Name = "Bob" @>)
            Spelling.Of ("an extension method", (fun p -> p.IsYoungerThan 18), <@ fun p -> p.Age < 18 @>)
            Spelling.Of (
                "an extension method of two arguments",
                (fun p -> p.IsOutside (18, 65)),
                <@ fun p -> p.Age < 18 || p.Age > 65 @>
            )
            Spelling.Of ("an extension property", (fun p -> p.HasNick), <@ fun p -> p.Nick.IsSome @>)
            Spelling.Of ("a static extension property", (fun p -> p.Age >= Person.AdultAge), <@ fun p -> p.Age >= 18 @>)
            Spelling.Over (
                "a static property",
                invoices,
                (fun invoice -> invoice.Total > Invoice.LargeTotal),
                <@ fun invoice -> invoice.Total > 100 @>
            )
            Spelling.Over (
                "a method of a base class",
                invoices,
                (fun invoice -> invoice.HasId "a"),
                <@ fun invoice -> (invoice :> DocumentBase).Id = "a" @>
            )
            Spelling.Over (
                "a property of a base class",
                invoices,
                (fun invoice -> invoice.IsIdentified),
                <@ fun invoice -> (invoice :> DocumentBase).Id <> "" @>
            )

            // Parts of values that are built in place
            Spelling.Of (
                "the first item of a pair that is built in place",
                (fun p -> fst (p.Age, p.Name) > 18),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "the second item of a pair that is built in place",
                (fun p -> snd (p.Name, p.Age) > 18),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "the first item of a pair of the document",
                (fun p -> fst p.Scores > 5),
                <@
                    fun p ->
                        let (best, _) = p.Scores
                        best > 5
                @>
            )
            Spelling.Of (
                "the second item of a pair of the document",
                (fun p -> snd p.Scores > 5),
                <@
                    fun p ->
                        let (_, worst) = p.Scores
                        worst > 5
                @>
            )
            #nowarn "3220" // the item property of a tuple is what the case is about
            Spelling.Of ("the item property of a pair", (fun p -> (p.Age, p.Name).Item1 > 18), <@ fun p -> p.Age > 18 @>)
            #warnon "3220"
            Spelling.Of (
                "a field of a record that is built in place",
                (fun p -> p.Age > { Low = 18; High = 65 }.Low),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "a field of an anonymous record that is built in place",
                (fun p -> {| Low = 18; High = p.Age |}.High > 18),
                <@ fun p -> p.Age > 18 @>
            )
            Spelling.Of (
                "a field of a struct anonymous record that is built in place",
                (fun p -> (struct {| Low = 18; High = p.Age |}).High > 18),
                <@ fun p -> p.Age > 18 @>
            )

            // Booleans
            Spelling.Of ("a boolean that is compared with true", (fun p -> p.Active = true), <@ fun p -> p.Active @>)
            Spelling.Of ("true that is compared with a boolean", (fun p -> true = p.Active), <@ fun p -> p.Active @>)
            Spelling.Of ("a boolean that is compared with false", (fun p -> p.Active = false), <@ fun p -> not p.Active @>)
            Spelling.Of ("a boolean that is not true", (fun p -> p.Active <> true), <@ fun p -> not p.Active @>)
            Spelling.Of ("a boolean that is not false", (fun p -> p.Active <> false), <@ fun p -> p.Active @>)
            Spelling.Of ("a double negation", (fun p -> not (not p.Active)), <@ fun p -> p.Active @>)
            Spelling.Of ("a choice between true and false", (fun p -> if p.Active then true else false), <@ fun p -> p.Active @>)
            Spelling.Of (
                "a choice between false and true",
                (fun p -> if p.Active then false else true),
                <@ fun p -> not p.Active @>
            )
            Spelling.Of ("a conjunction with true", (fun p -> p.Active && true), <@ fun p -> p.Active @>)
            Spelling.Of ("a conjunction of true", (fun p -> true && p.Active), <@ fun p -> p.Active @>)
            Spelling.Of ("a disjunction with false", (fun p -> p.Active || false), <@ fun p -> p.Active @>)
            Spelling.Of ("a disjunction of false", (fun p -> false || p.Active), <@ fun p -> p.Active @>)
            Spelling.Of (
                "a condition that is a constant",
                (fun p -> if true then p.Active else p.Age > 5),
                <@ fun p -> p.Active @>
            )
            Spelling.Of ("the negation of a constant", (fun p -> p.Active && not false), <@ fun p -> p.Active @>)
            // A boolean that is made with its constructor is the default value of the type in a quotation: false
            Spelling.Of (
                "a boolean that is compared with the default value of its type",
                (fun p -> p.Active = bool ()),
                <@ fun p -> not p.Active @>
            )
            Spelling.Of (
                "a condition that is the default value of a boolean",
                (fun p -> if bool () then p.Age > 5 else p.Active),
                <@ fun p -> p.Active @>
            )

            // Code that never runs: it has no normal form here, and needs none
            Spelling.Of (
                "a branch without a normal form that a constant condition does not choose",
                (fun p -> if true then p.Active else Conditions.factorial p.Age > 0),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a part without a normal form after a conjunction with the default value of a boolean",
                (fun p -> p.Active || (bool () && Conditions.factorial p.Age > 0)),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a part without a normal form after a conjunction with false",
                (fun p -> p.Active || (false && Conditions.factorial p.Age > 0)),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a branch without a normal form that a condition does not choose once it is normalized",
                (fun p ->
                    if not (false && p.Active) then
                        p.Active
                    else
                        Conditions.factorial p.Age > 0
                ),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a branch without a normal form that a constant bound before the condition does not choose",
                (fun p ->
                    let onlyActive = true
                    if onlyActive then
                        p.Active
                    else
                        Conditions.factorial p.Age > 0
                ),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a branch without a normal form that a constant passed to a function in place does not choose",
                (fun p ->
                    (fun onlyActive ->
                        if onlyActive then
                            p.Active
                        else
                            Conditions.factorial p.Age > 0
                    )
                        true
                ),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a branch without a normal form that a piped constant does not choose",
                (fun p ->
                    true
                    |> fun onlyActive ->
                        if onlyActive then
                            p.Active
                        else
                            Conditions.factorial p.Age > 0
                ),
                <@ fun p -> p.Active @>
            )
            Spelling.Of (
                "a branch without a normal form that a constant passed to a function with a definition does not choose",
                (fun p -> Conditions.isActiveOrCounted true p),
                <@ fun p -> p.Active @>
            )
        }
        |> ImmutableArray.CreateRange

    /// <summary>
    /// Every spelling as a row of a data-driven test. The name of a row replaces the name of the test method in the
    /// results, so it says what is special about the spelling and what <paramref name="law"/> the method asserts.
    /// </summary>
    static member private Rows (law : string) : IEnumerable<TestDataRow<Spelling>> =
        spellings
        |> Seq.map (fun spelling -> TestDataRow<Spelling>(spelling, DisplayName = $"%s{spelling.Name} %s{law}"))

    /// The rows of the test that compares the normal forms of the two ways to write a function.
    static member SameFormRows =
        NormalizerSpellingTests.Rows "must have the normal form of the plain way to write the function"

    /// The rows of the test that normalizes a normal form again.
    static member StableFormRows =
        NormalizerSpellingTests.Rows "must have a normal form that normalization returns as it is"

    /// The rows of the test that compares the normal form with the compiled code.
    static member SameMeaningRows =
        NormalizerSpellingTests.Rows "must have a normal form that computes what the compiled code computes"

    [<TestMethod; DynamicData(nameof NormalizerSpellingTests.SameFormRows)>]
    member _.``A spelling must have the normal form of the plain way to write the function`` (spelling : Spelling) =
        NormalizerAssert.AreEquivalent
            (NormalizerAssert.WantNormal spelling.Plain)
            (NormalizerAssert.WantNormal spelling.Written)
            "Both ways to write the function should have one normal form."

    [<TestMethod; DynamicData(nameof NormalizerSpellingTests.StableFormRows)>]
    member _.``A spelling must have a normal form that normalization returns as it is`` (spelling : Spelling) =
        let normal = NormalizerAssert.WantNormal spelling.Written

        Assert.AreSame<Expr>(
            normal,
            NormalizerAssert.WantNormal normal,
            "A quotation in normal form should be returned as the same node."
        )

    [<TestMethod; DynamicData(nameof NormalizerSpellingTests.SameMeaningRows)>]
    member _.``A spelling must have a normal form that computes what the compiled code computes`` (spelling : Spelling) =
        spelling.ComputesTheSame (NormalizerAssert.WantNormal spelling.Written)
