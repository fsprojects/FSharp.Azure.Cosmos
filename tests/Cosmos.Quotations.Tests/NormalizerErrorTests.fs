namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// The quotations that have no normal form a query can express, and what the
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationError"/> says about each.
/// </summary>
[<TestClass; NormalizerUnitTestCategory>]
type NormalizerErrorTests () =

    static let unsupportedConstructs =
        let mutableVariable = Var ("count", typeof<int>, true)
        let variable = Var ("value", typeof<int>)

        seq {
            struct ("a mutable variable that is assigned to",
                    <@@
                        fun (p : Person) ->
                            let mutable age = p.Age
                            age <- age + 1
                            age > 18
                    @@>,
                    "a mutable variable")
            struct ("an assignment to a property", <@@ fun (counter : Counter) -> counter.Count <- 1 @@>, "an assignment")
            struct ("an assignment to a variable", Expr.VarSet (mutableVariable, <@@ 1 @@>), "an assignment")
            struct ("a sequence of statements",
                    <@@
                        fun (p : Person) ->
                            ignore p.Age
                            p.Active
                    @@>,
                    "a sequence of statements")
            struct ("a for loop",
                    <@@
                        fun (p : Person) ->
                            for index in 1 .. p.Age do
                                ignore index
                    @@>,
                    "a loop")
            struct ("a while loop",
                    <@@
                        fun (p : Person) ->
                            while p.Active do
                                ()
                    @@>,
                    "a loop")
            struct ("a handler of exceptions",
                    <@@
                        fun (p : Person) ->
                            try
                                p.Active
                            with _ ->
                                false
                    @@>,
                    "exception handling")
            struct ("a finally block",
                    <@@
                        fun (p : Person) ->
                            try
                                p.Active
                            finally
                                ()
                    @@>,
                    "exception handling")
            struct ("a recursive function written in place",
                    <@@
                        fun (p : Person) ->
                            let rec countTo (number : int) : int = if number >= p.Age then number else countTo (number + 1)
                            countTo 0 > 5
                    @@>,
                    "a recursive function")
            struct ("the address of a value", Expr.Lambda (variable, Expr.AddressOf (Expr.Var variable)), "the address of a value")
        }
        |> ImmutableArray.CreateRange

    /// <summary>
    /// Every construct that a query cannot express, with the words the message has to name it by. The name of a row
    /// replaces the name of the test method in the results, so it says what is asserted.
    /// </summary>
    static member UnsupportedConstructs : IEnumerable<TestDataRow<struct (Expr * string)>> =
        unsupportedConstructs
        |> Seq.map (fun (struct (name, quotation, words)) ->
            TestDataRow<struct (Expr * string)>(
                struct (quotation, words),
                DisplayName = $"%s{name} must be reported as unsupported and named"
            )
        )

    [<TestMethod; DynamicData(nameof NormalizerErrorTests.UnsupportedConstructs)>]
    member _.``A construct that a query cannot express must be reported as unsupported and named``
        (quotation : Expr, words : string)
        =
        let error = NormalizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.Unsupported,
            error.Code,
            "The construct has no counterpart in a query."
        )
        Assert.Contains (words, error.Message, StringComparison.Ordinal, "The message should name the construct.")
        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")

    [<TestMethod>]
    member _.``A construct that a query cannot express must be reported when the quotation inside it has no normal form`` () =
        // Two errors, one inside the other: a loop, and in its condition a function that calls itself.
        let loop =
            NormalizerAssert.WantError
                <@
                    fun (p : Person) ->
                        while Conditions.factorial p.Age > 100 do
                            ()
                @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.Unsupported,
            loop.Code,
            "The loop is the outer of the two errors, and nothing inside it can make a query of it."
        )
        Assert.Contains ("a loop", loop.Message, StringComparison.Ordinal, "The message should name the loop.")

        // Three errors: the statements of the body are the outermost one, then the loop, then the function.
        let statements =
            NormalizerAssert.WantError
                <@
                    fun (p : Person) ->
                        while Conditions.factorial p.Age > 100 do
                            ()

                        p.Active
                @>

        Assert.Contains (
            "a sequence of statements",
            statements.Message,
            StringComparison.Ordinal,
            "The message should name the outermost construct that a query cannot express."
        )

        // The call of such a function stays in the normal form, where the outermost construct is the error. A
        // condition whose simplification has no end stops the normalization where it is met, inside the loop, and the
        // loop is the error all the same.
        let endless : Expr<bool> =
            <@
                let self =
                    fun (loop : Loop) ->
                        match loop with
                        | Loop apply -> apply loop

                self (Loop self)
            @>

        let endlessLoop =
            NormalizerAssert.WantError
                <@
                    fun (p : Person) ->
                        while %endless do
                            ()
                @>

        Assert.Contains (
            "a loop",
            endlessLoop.Message,
            StringComparison.Ordinal,
            "The message should name the loop, and not what stopped the normalization inside it."
        )

        let endlessStatements =
            NormalizerAssert.WantError
                <@
                    fun (p : Person) ->
                        while %endless do
                            ()

                        p.Active
                @>

        Assert.Contains (
            "a sequence of statements",
            endlessStatements.Message,
            StringComparison.Ordinal,
            "The message should name the outermost construct, and not the loop inside it."
        )

    [<TestMethod>]
    member _.``A mutable variable must be reported at the binding that is assigned to when an outer binding has the same variable``
        ()
        =
        // One mutable variable that two bindings bind, one inside the other; only the inner one is assigned to.
        //     fun p -> let mutable count = 111 in (let mutable count = 222 in count <- 333; count > p.Age)
        let count = Var ("count", typeof<int>, true)
        let person = Var ("p", typeof<Person>)

        let inner =
            Expr.Let (
                count,
                <@@ 222 @@>,
                Expr.Sequential (
                    Expr.VarSet (count, <@@ 333 @@>),
                    <@@ %%(Expr.Var count) > (%%(Expr.Var person) : Person).Age @@>
                )
            )

        let error = NormalizerAssert.WantError (Expr.Lambda (person, Expr.Let (count, <@@ 111 @@>, inner)))

        Assert.Contains (
            "a mutable variable",
            error.Message,
            StringComparison.Ordinal,
            "The inner binding is assigned to, which a query cannot express."
        )
        Assert.Contains ("222", error.Fragment, StringComparison.Ordinal, "The fragment should be the inner binding.")
        Assert.DoesNotContain (
            "111",
            error.Fragment,
            StringComparison.Ordinal,
            "Nothing assigns to the outer binding, so it should be gone: the inner binding hides the variable."
        )

    [<TestMethod>]
    member _.``A captured function that is applied to the document must be reported with its name and advice`` () =
        let isVip = fun (p : Person) -> p.Tags |> List.contains "vip"
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> p.Active && isVip p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "A compiled function cannot be read, so it cannot be applied in a query."
        )

        Assert.Contains ("'isVip'", error.Message, StringComparison.Ordinal, "The message should name the function.")
        Assert.Contains (
            "isVip",
            error.Fragment,
            StringComparison.Ordinal,
            "The fragment should be the application of the function."
        )
        Assert.HasCount (2, error.Hints, "The error should offer the reflected definition and the spliced quotation.")

    [<TestMethod>]
    member _.``An error must point at the place in the source when the compiler recorded it`` () =
        let isVip = fun (p : Person) -> p.Tags |> List.contains "vip"
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> isVip p @>
        let location =
            Assert.WantValueSome (
                error.Location,
                "The tests are compiled with --quotations-debug, so the quotation has its place."
            )

        Assert.EndsWith ("NormalizerErrorTests.fs", location.File, StringComparison.Ordinal, "The place should be in this file.")
        Assert.IsGreaterThan (0, location.StartLine, "The first line of a file is 1.")
        Assert.IsGreaterThanOrEqualTo (location.StartLine, location.EndLine, "A range does not end before it starts.")

    [<TestMethod>]
    member _.``An error about a quotation that was built through the API must have no place in the source`` () =
        let variable = Var ("value", typeof<int>)
        let error = NormalizerAssert.WantError (Expr.Lambda (variable, Expr.AddressOf (Expr.Var variable)))

        Assert.IsValueNone (error.Location, "The compiler never saw the quotation, so it recorded no place.")

    [<TestMethod>]
    member _.``A function that a call returns must be reported as a compiled function when it is applied to the document`` () =
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.olderThan 18 p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "What the call returns is a compiled function."
        )
        Assert.Contains (
            "'olderThan'",
            error.Message,
            StringComparison.Ordinal,
            "The message should name the function that returns it."
        )

        // The same holds when the call is given a value of the document: it is the definition of the called function
        // that cannot be read, and the advice is to make it readable.
        let ofDocument = NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.olderThan p.Age p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            ofDocument.Code,
            "What the call returns is a compiled function, whatever the call is given."
        )
        Assert.Contains (
            "'olderThan'",
            ofDocument.Message,
            StringComparison.Ordinal,
            "The message should name the function that returns it, also when it is given a value of the document."
        )

    [<TestMethod>]
    member _.``A function that a captured value holds must be reported as a compiled function when it is applied to the document``
        ()
        =
        let rules = {| IsListed = fun (p : Person) -> p.Active |}
        let ofProperty = NormalizerAssert.WantError <@ fun (p : Person) -> rules.IsListed p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            ofProperty.Code,
            "The property of the captured value holds a compiled function."
        )
        Assert.Contains ("'IsListed'", ofProperty.Message, StringComparison.Ordinal, "The message should name the property.")
        Assert.HasCount (2, ofProperty.Hints, "The error should offer the reflected definition and the spliced quotation.")

        // A field of a captured value, which only code builds a reading of: F# reads the item of a struct tuple by a
        // pattern.
        let pair = struct ((fun (p : Person) -> p.Active), 0)
        let person = Var ("p", typeof<Person>)

        let ofField =
            Expr.Lambda (
                person,
                Expr.Application (Expr.FieldGet (Expr.Value pair, nonNull (pair.GetType().GetField "Item1")), Expr.Var person)
            )
            |> NormalizerAssert.WantError

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            ofField.Code,
            "The field of the captured value holds a compiled function."
        )
        Assert.Contains ("'Item1'", ofField.Message, StringComparison.Ordinal, "The message should name the field.")

    [<TestMethod>]
    member _.``A function that the document chooses must be reported as one that is chosen when the query runs`` () =
        let isVip = fun (p : Person) -> p.Tags |> List.contains "vip"
        let isAdult = fun (p : Person) -> p.Age >= 18
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> (if p.Active then isVip else isAdult) p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.Unsupported,
            error.Code,
            "Which function is applied depends on the document, so no advice about one compiled function fits."
        )
        Assert.Contains (
            "chosen or computed when the query runs",
            error.Message,
            StringComparison.Ordinal,
            "The message should say that the function is chosen for every document."
        )

    [<TestMethod>]
    member _.``A module-level function value must be reported as a compiled function when it is applied to the document`` () =
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.isSenior p @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "The value is a compiled function."
        )
        Assert.Contains ("'isSenior'", error.Message, StringComparison.Ordinal, "The message should name the value.")

    [<TestMethod>]
    member _.``A captured function must be reported when a function with a reflected definition applies it to the document`` () =
        let isLong = fun (tag : string) -> tag.Length > 3
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> p |> Conditions.hasTagThat isLong @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "The definition is written into the quotation, and there the captured function is applied to a tag of the document."
        )

        Assert.Contains ("'isLong'", error.Message, StringComparison.Ordinal, "The message should name the function.")

    [<TestMethod>]
    member _.``A composition with a captured function must be reported`` () =
        let isVip = fun (p : Person) -> p.Tags |> List.contains "vip"
        let error = NormalizerAssert.WantError <@ isVip >> not @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "The composition applies the captured function to its parameter, which is the document."
        )

    [<TestMethod>]
    member _.``A captured function that is applied to captured values only must stay an application`` () =
        let addOne = fun (number : int) -> number + 1
        let normal = NormalizerAssert.WantNormal <@ fun (p : Person) -> p.Age > addOne 17 @>

        let isApplication (expr : Expr) =
            match expr with
            | Application _ -> true
            | _ -> false

        Assert.IsTrue (
            Quotation.exists isApplication normal,
            "The application does not depend on the document: it is a value to compute outside the query, not an error."
        )

    [<TestMethod>]
    member _.``A function with a reflected definition that calls itself must be reported instead of followed without end`` () =
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.factorial p.Age > 100 @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.NormalizationDiverged,
            error.Code,
            "A recursive definition has no end."
        )
        Assert.Contains (
            "'factorial'",
            error.Message,
            StringComparison.Ordinal,
            "The message should name the recursive function."
        )
        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")

        Assert.Contains (
            "Age",
            error.Fragment,
            StringComparison.Ordinal,
            "The error should show the call as the quotation has it, with the argument it passes."
        )

    [<TestMethod>]
    member _.``The first of two functions that call themselves must be reported`` () =
        let error =
            NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.isEven p.Age && Conditions.factorial p.Age > 100 @>

        Assert.Contains (
            "'isEven'",
            error.Message,
            StringComparison.Ordinal,
            "Both calls stay in the normal form, and the first of them is the error."
        )

    [<TestMethod>]
    member _.``A property with a reflected definition that reads itself must be reported by its name`` () =
        let error = NormalizerAssert.WantError <@ fun (folder : Folder) -> folder.Depth > 2 @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.NormalizationDiverged,
            error.Code,
            "A definition that reads itself has no end."
        )
        Assert.Contains (
            "The property 'Depth'",
            error.Message,
            StringComparison.Ordinal,
            "The message should name the property as it is written, and not its getter."
        )

    [<TestMethod>]
    member _.``A call of a function that calls itself must stay a call when it reads nothing of the document`` () =
        let normal = NormalizerAssert.WantNormal <@ fun (p : Person) -> p.Age > Conditions.factorial 5 @>

        NormalizerAssert.AreEquivalent
            <@ fun (p : Person) -> p.Age > Conditions.factorial 5 @>
            normal
            "The call is a value to compute outside the query, as a call of any compiled function is: it stays as it is written, and its definition is not written into the quotation once."

        let root = { Name = "root"; Parent = None }
        let folder = { Name = "folder"; Parent = Some root }

        NormalizerAssert.AreEquivalent
            <@ fun (p : Person) -> p.Age > folder.Depth @>
            (NormalizerAssert.WantNormal <@ fun (p : Person) -> p.Age > folder.Depth @>)
            "The same holds for a property that reads itself."

    [<TestMethod>]
    member _.``Functions with reflected definitions that call each other must be reported instead of followed without end`` () =
        let error = NormalizerAssert.WantError <@ fun (p : Person) -> Conditions.isEven p.Age @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.NormalizationDiverged,
            error.Code,
            "Mutually recursive definitions have no end."
        )
        Assert.Contains (
            "'isEven'",
            error.Message,
            StringComparison.Ordinal,
            "The message should name the function that is called again."
        )

    [<TestMethod>]
    member _.``A quotation without a normal form must be reported when the limit of rewriting steps is reached`` () =
        // The function applies the function inside its argument to that argument, and its argument holds the function
        // itself: every rewriting step gives the quotation it started from.
        let error =
            NormalizerAssert.WantError
                <@
                    let self =
                        fun (loop : Loop) ->
                            match loop with
                            | Loop apply -> apply loop

                    self (Loop self)
                @>

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.NormalizationDiverged, error.Code, "The rewriting has no end.")
        Assert.Contains (
            "rewriting steps",
            error.Message,
            StringComparison.Ordinal,
            "The message should say that the limit of steps was reached."
        )
        Assert.AreEqual<string>("", error.Fragment, "The error is about the quotation as a whole.")

    [<TestMethod>]
    [<DataRow(false, DisplayName = "A quotation that is nested too deeply must be reported")>]
    [<DataRow(true,
              DisplayName =
                  "A quotation that is nested too deeply must be reported inside a construct that a query cannot express")>]
    member _.``A quotation that is nested too deeply must be reported`` (insideStatements : bool) =
        // A tree that only code builds: a negation inside a negation, a hundred thousand deep. Nothing may walk it to
        // its end, which takes more stack than a thread has.
        let negation =
            match <@@ not true @@> with
            | Call (None, negation, _) -> negation
            | other -> raise (AssertFailedException $"A negation should be a call of a function: %A{other}")

        let nested =
            seq { 1..100_000 }
            |> Seq.fold (fun (inner : Expr) _ -> Expr.Call (negation, [ inner ])) <@@ true @@>

        // The error of a sequence of statements shows the statements, which would walk the tree to print it. So what
        // is nested too deeply has to stay the error there.
        let quotation =
            if insideStatements then
                Expr.Sequential (Expr.Sequential (nested, <@@ () @@>), <@@ true @@>)
            else
                nested

        let error = NormalizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.NormalizationDiverged,
            error.Code,
            "The quotation has no normal form that the normalizer can reach."
        )

        Assert.Contains (
            "nested too deeply",
            error.Message,
            StringComparison.Ordinal,
            "The message should say that the quotation is nested too deeply."
        )

    [<TestMethod>]
    member _.``A quotation without a normal form must be reported when it grows deeper with every rewriting step`` () =
        // As above, but every step leaves the next application inside a negation, so the quotation grows deeper and
        // deeper: the normalizer has to stop before the stack of the thread is used up.
        let error =
            NormalizerAssert.WantError
                <@
                    let self =
                        fun (loop : Loop) ->
                            match loop with
                            | Loop apply -> not (apply loop)

                    self (Loop self)
                @>

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.NormalizationDiverged, error.Code, "The rewriting has no end.")
