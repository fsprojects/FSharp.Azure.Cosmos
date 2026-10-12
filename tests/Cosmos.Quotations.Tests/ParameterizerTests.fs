namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Linq
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// Values declared as literals, which the compiler writes into a quotation in place of their names.
module Literals =

    /// The first letter of a name.
    [<Literal>]
    let Initial = "B"

    /// A rate, as a decimal.
    [<Literal>]
    let Rate = 2.5m

/// <summary>
/// What <see cref="T:FSharp.Azure.Cosmos.Quotations.Parameterizer"/> makes of the values in a quotation: which of them
/// are slots, under which names and with which values, which are parts of the key, and what is an error.
/// </summary>
[<TestClass; UnitTestCategory; ParameterizerTestCategory>]
type ParameterizerTests () =

    /// The value of a slot, which a test expects to have been computed.
    static let valueOf (index : int) (values : Collections.Immutable.ImmutableArray<objnull voption>) : objnull =
        Assert.WantValueSome (values[index], $"The slot %d{index} is in a branch that is taken, so it should have a value.")

    /// The module of the functions that say how a value gets into the query.
    static let queryModule =
        match <@ Query.constant 0 @> with
        | Call (None, method, _) -> method.DeclaringType
        | other -> invalidOp $"The quotation is not a call of a function: %A{other}"

    /// Whether a quotation calls a function of the Query module.
    static let callsQuery (expr : Expr) : bool =
        expr
        |> Quotation.exists (fun part ->
            match part with
            | Call (None, method, _) -> Type.(=) (method.DeclaringType, queryModule)
            | _ -> false
        )

    /// A quotation with a value of its own under the name that every quotation made here gives its value.
    static let olderThan (age : int) : Expr<Person -> bool> = <@ fun person -> person.Age > age @>

    /// A quotation that captures a name.
    static let named (name : string) : Expr<Person -> bool> = <@ fun person -> person.Name = name @>

    /// Another quotation that captures a variable of the same name.
    static let nicknamed (name : string) : Expr<Person -> bool> = <@ fun person -> person.Nick = Some name @>

    /// Quotations that build a value from a captured name, each with the types of the slots it has to give.
    static member BuiltValueRows : IEnumerable<TestDataRow<struct (Expr * Type array)>> =
        let name = "Bob"
        let limit = 30

        seq {
            struct ("An option", <@@ fun (p : Person) -> p.Nick = Some name @@>, [| typeof<string> |])
            struct ("A list", <@@ fun (p : Person) -> [ name; "Dee" ] |> List.contains p.Name @@>, [| typeof<string> |])
            struct ("An array", <@@ fun (p : Person) -> [| name; "Dee" |] |> Array.contains p.Name @@>, [| typeof<string> |])
            struct ("A tuple", <@@ fun (p : Person) -> p.Scores = (limit, 1) @@>, [| typeof<int> |])
            struct ("A struct tuple",
                    <@@ fun (p : Person) -> struct (p.Name, p.Age) = struct (name, limit) @@>,
                    [| typeof<string>; typeof<int> |])
            struct ("A record",
                    <@@ fun (p : Person) -> { Low = p.Age; High = 1 } = { Low = limit; High = 1 } @@>,
                    [| typeof<int> |])
            struct ("An anonymous record",
                    <@@ fun (p : Person) -> {| Name = p.Name |} = {| Name = name |} @@>,
                    [| typeof<string> |])
        }
        |> Seq.map (fun (struct (built, quotation, slots)) ->
            TestDataRow<struct (Expr * Type array)>(
                struct (quotation, slots),
                DisplayName = $"%s{built} that the quotation builds must stay what it is, with its parts as slots"
            )
        )

    /// Quotations that pass a function value where the query would need to read the function.
    static member FunctionValueRows : IEnumerable<TestDataRow<Expr>> =
        let isVip = fun (tag : string) -> tag = "vip"
        let isNew = Func<string, bool>(fun tag -> tag = "new")
        let hiddenFunction : obj = isVip
        let hiddenDelegate : obj = isNew
        let ownFunction = TagTest "vip"

        seq {
            struct ("A captured function that is passed into the query", <@@ fun (p : Person) -> p.Tags |> List.exists isVip @@>)
            struct ("A captured delegate that is passed into the query",
                    <@@ fun (p : Person) -> Enumerable.Any (p.Tags, isNew) @@>)
            struct ("A function that a call returns into the query",
                    <@@ fun (p : Person) -> [ p ] |> List.exists (Conditions.olderThan 18) @@>)
            struct ("A module-level function value that is passed into the query",
                    <@@ fun (p : Person) -> [ p ] |> List.exists Conditions.isSenior @@>)

            // The type of the part does not say that the value is a function
            struct ("A captured function that is passed as an object",
                    <@@ fun (p : Person) -> Object.Equals (p.Tags, hiddenFunction) @@>)
            struct ("A captured delegate that is passed as an object",
                    <@@ fun (p : Person) -> Object.Equals (p.Tags, hiddenDelegate) @@>)
            struct ("A function that a call returns as an object",
                    <@@ fun (p : Person) -> Object.Equals (p.Tags, box (Conditions.olderThan 18)) @@>)

            // The type of the part is a class made of the type of functions
            struct ("A captured function of a class that code declares",
                    <@@ fun (p : Person) -> Object.Equals (p.Tags, ownFunction) @@>)

            // A function is no constant of a query either
            struct ("A function that Query.constant is given",
                    <@@ fun (p : Person) -> p.Tags |> List.exists (Query.constant isVip) @@>)
            struct ("A function that Query.constant is given as an object",
                    <@@ fun (p : Person) -> Object.Equals (p.Tags, Query.constant hiddenFunction) @@>)

            // A function that is not there is no value of a query either. Only code builds these two: the compiler
            // writes no null of a function type.
            struct ("The null of a function type that code builds into the query",
                    <@@
                        fun (p : Person) ->
                            p.Tags
                            |> List.exists %%(Expr.Value (null, typeof<string -> bool>))
                    @@>)
            struct ("The default value of a function type that code builds into the query",
                    <@@
                        fun (p : Person) ->
                            p.Tags
                            |> List.exists %%(Expr.DefaultValue typeof<string -> bool>)
                    @@>)
        }
        |> Seq.map (fun (struct (passed, quotation)) ->
            TestDataRow<Expr>(quotation, DisplayName = $"%s{passed} must be reported")
        )

    /// Quotations that give Query.constant a value that a query cannot hold as a literal.
    static member NotLiteralRows : IEnumerable<TestDataRow<Expr>> =
        let range = { Low = 1; High = 2 }
        let tags = [| "vip" |]
        let rate = 2.5m
        let day = DateTime (2026, 1, 1)
        let address = Uri "http://localhost"
        let count = Nullable 5
        let noCount = Nullable<int>()
        let nobody : string option = None
        let unranked = Unranked

        seq {
            struct ("A record", <@@ fun (p : Person) -> { Low = p.Age; High = 2 } = Query.constant range @@>)
            struct ("An array", <@@ fun (p : Person) -> Query.constant tags |> Array.contains p.Name @@>)
            struct ("A decimal", <@@ fun (p : Person) -> decimal p.Age > Query.constant rate @@>)
            struct ("A date", <@@ fun (p : Person) -> DateTime (2000 + p.Age, 1, 1) > Query.constant day @@>)
            struct ("An object", <@@ fun (p : Person) -> Object.Equals (p.Name, Query.constant address) @@>)
            struct ("A nullable with a value", <@@ fun (p : Person) -> Nullable p.Age = Query.constant count @@>)
            struct ("A nullable without a value", <@@ fun (p : Person) -> Nullable p.Age = Query.constant noCount @@>)
            struct ("An option without a value", <@@ fun (p : Person) -> p.Nick = Query.constant nobody @@>)
            struct ("A case of a union that is null", <@@ fun (p : Person) -> Object.Equals (p.Name, Query.constant unranked) @@>)
        }
        |> Seq.map (fun (struct (given, quotation)) ->
            TestDataRow<Expr>(quotation, DisplayName = $"%s{given} that Query.constant is given must be reported")
        )

    // Slots

    [<TestMethod>]
    member _.``A captured value must be a slot that is named after its variable`` () =
        let limit = 18
        let template = ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > limit @>

        let slot = Assert.ContainsSingle (template.Slots, "The quotation captures one value.")
        Assert.AreEqual<string voption>(ValueSome "limit", slot.Name, "The slot should have the name of the variable.")
        Assert.AreEqual<Type>(typeof<int>, slot.Variable.Type, "The variable of the slot should have the type of the value.")
        Assert.AreEqual<objnull>(18, valueOf 0 template.Values, "The value of the slot should be the captured value.")

    [<TestMethod>]
    member _.``A captured variable that is used twice must be a slot at each place`` () =
        let limit = 18
        let name = "Bob"

        let ofValueType =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > limit || fst p.Scores > limit @>
        let ofReferenceType =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Name = name || p.Nick = Some name @>

        CollectionAssert.AreEqual (
            [| ValueSome "limit"; ValueSome "limit" |],
            ofValueType.Slots |> Seq.map _.Name |> Seq.toArray,
            "The compiler writes each use as a node of its own, and nothing in the nodes says that they are one variable."
        )

        CollectionAssert.AreEqual (
            [| box 18; box 18 |],
            [| valueOf 0 ofValueType.Values; valueOf 1 ofValueType.Values |],
            "Each slot should hold the value of the variable."
        )

        Assert.HasCount (2, ofReferenceType.Slots, "Two nodes that hold one object are two slots like any two nodes.")

    [<TestMethod>]
    member _.``Two captured variables of one name must be two slots whatever they hold`` () =
        // Each splice captured its own variable `age`.
        let both (first : int) (second : int) = <@ fun (p : Person) -> (%olderThan first) p && (%olderThan second) p @>

        let different = ParameterizerAssert.WantTemplate (both 18 65)
        let same = ParameterizerAssert.WantTemplate (both 18 18)

        CollectionAssert.AreEqual (
            [| ValueSome "age"; ValueSome "age" |],
            different.Slots |> Seq.map _.Name |> Seq.toArray,
            "Each slot should keep the name of its variable."
        )

        CollectionAssert.AreEqual (
            [| box 18; box 65 |],
            [| valueOf 0 different.Values; valueOf 1 different.Values |],
            "The slots should hold the two values in the order of their first use."
        )

        Assert.HasCount (2, same.Slots, "Two variables stay two parameters when they happen to hold one value.")

        Assert.AreEqual<ShapeKey>(
            different.Key,
            same.Key,
            "The key should not say whether two values are equal this time, or one query would have two texts."
        )

    [<TestMethod>]
    member _.``Two captured variables of one name must be two slots when they hold one object`` () =
        // Each splice captured its own variable `name`, and both variables hold the same string.
        let text = "Bob"
        let template =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> (%named text) p || (%nicknamed text) p @>

        CollectionAssert.AreEqual (
            [| ValueSome "name"; ValueSome "name" |],
            template.Slots |> Seq.map _.Name |> Seq.toArray,
            "What two variables hold does not make them one: the next time they can hold two objects."
        )

    [<TestMethod>]
    member _.``Two captured variables of one value must be two slots when their names differ`` () =
        let low = 18
        let high = 18
        let template = ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age >= low && p.Age <= high @>

        CollectionAssert.AreEqual (
            [| ValueSome "low"; ValueSome "high" |],
            template.Slots |> Seq.map _.Name |> Seq.toArray,
            "Two variables are two parameters, also when they happen to hold one value."
        )

    [<TestMethod>]
    member _.``A captured value that is bound by let must be one slot for all its uses`` () =
        let limit = 18

        let template =
            ParameterizerAssert.WantTemplate
                <@
                    fun (p : Person) ->
                        let age = limit
                        p.Age > age || fst p.Scores > age
                @>

        let slot =
            Assert.ContainsSingle (
                template.Slots,
                "The normalizer writes the bound node at both places, and one node is one slot."
            )

        Assert.AreEqual<string voption>(ValueSome "limit", slot.Name, "The slot should have the name of the captured variable.")

    [<TestMethod>]
    member _.``A quotation that is spliced into two places must be one slot for both`` () =
        let limit = 18

        // The quotation binds a value, so the normalizer rewrites it before the slots are told.
        let age =
            <@
                let years = limit
                years + 1
            @>

        let template =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > %age || fst p.Scores > %age @>

        Assert.ContainsSingle (
            template.Slots,
            "A splice puts one node at both places, the normalizer keeps it one node, and one node is one slot."
        )
        |> ignore

        Assert.AreEqual<objnull>(19, valueOf 0 template.Values, "The slot should hold the value that the spliced code computes.")

    [<TestMethod>]
    member _.``The name of a captured variable must be in the key`` () =
        let keyOf (expr : Expr) = (ParameterizerAssert.WantShape expr).Key
        let limit = 18
        let minimum = 18

        Assert.AreNotEqual<ShapeKey>(
            keyOf <@ fun (p : Person) -> p.Age > limit @>,
            keyOf <@ fun (p : Person) -> p.Age > minimum @>,
            "The parameter of a query is named after the variable, so another name is another query text."
        )

    [<TestMethod>]
    member _.``The type of a slot must be in the key`` () =
        let keyOf (expr : Expr) = (ParameterizerAssert.WantShape expr).Key

        let ofAddress =
            let value = Uri "http://localhost"
            keyOf <@ fun (p : Person) -> Object.Equals (p.Name, value) @>

        let ofText =
            let value = "5"
            keyOf <@ fun (p : Person) -> Object.Equals (p.Name, value) @>

        Assert.AreNotEqual<ShapeKey>(ofAddress, ofText, "A value of another type is written into the query in another way.")

    [<TestMethod>]
    member _.``The index of a slot must be in the key`` () =
        let limit = 18

        // The same condition twice; the second one binds the value, so it is one slot at both places.
        let ofTwoSlots =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > limit || fst p.Scores > limit @>

        let ofOneSlot =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let limit = limit
                        p.Age > limit || fst p.Scores > limit
                @>

        Assert.HasCount (2, ofTwoSlots.Values, "Two uses of a captured variable are two slots.")

        Assert.ContainsSingle (ofOneSlot.Values, "A bound value is one slot at all its places.")
        |> ignore

        Assert.AreNotEqual<ShapeKey>(
            ofTwoSlots.Key,
            ofOneSlot.Key,
            "One parameter at two places and two parameters are different query texts, and they take a different number of values."
        )

    [<TestMethod>]
    member _.``A part that reads no variable must be one slot without a name`` () =
        let limit = 18
        let template = ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > (limit + 1) * 2 @>

        let slot =
            Assert.ContainsSingle (template.Slots, "The largest part that can be computed should be one slot.")
        Assert.AreEqual<string voption>(ValueNone, slot.Name, "A computed value has no variable to be named after.")
        Assert.AreEqual<objnull>(38, valueOf 0 template.Values, "The slot should hold what the part computes.")

    [<TestMethod>]
    member _.``A choice that reads nothing of the document must be one slot`` () =
        let shapeOf (filter : string option) =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Name = (if filter.IsNone then "" else filter.Value.Trim ()) @>

        let without = shapeOf None
        let withFilter = shapeOf (Some " Bob ")

        CollectionAssert.AreEqual (
            [| ValueSome (box "") |],
            without.Values |> Seq.toArray,
            "The choice is computed as a whole, as compiled code computes it: nothing is trimmed without a filter."
        )

        CollectionAssert.AreEqual (
            [| ValueSome (box "Bob") |],
            withFilter.Values |> Seq.toArray,
            "The choice is one value, so it is one slot, whichever branch gives the value."
        )

        Assert.AreEqual<ShapeKey>(without.Key, withFilter.Key, "The key should not say which branch gave the value.")

    [<TestMethod>]
    member _.``A module-level value must be a slot`` () =
        let shape = ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > Conditions.retirementAge @>

        Assert.ContainsSingle (shape.Values, "The value is read before the query runs.")
        |> ignore

        Assert.AreEqual<objnull>(65, valueOf 0 shape.Values, "The slot should hold the value of the module.")

    [<TestMethod>]
    member _.``A value bound by let must be computed once for all its uses`` () =
        let meter = Meter ()

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let limit = meter.Read 18
                        p.Age > limit || fst p.Scores > limit
                @>

        Assert.ContainsSingle (shape.Values, "The normalizer writes the bound value at both places, and it is one value.")
        |> ignore

        Assert.AreEqual<int>(1, meter.Readings, "The code of the value should run once.")

    [<TestMethod>]
    member _.``Values bound by two lets must each be computed once for all their uses`` () =
        let low = Meter ()
        let high = Meter ()

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let least = low.Read 18
                        let most = high.Read 65
                        (p.Age > least && p.Age < most)
                        || (fst p.Scores > least && fst p.Scores < most)
                @>

        Assert.HasCount (
            2,
            shape.Values,
            "Each bound value should stay one node when the other one is written into the quotation."
        )

        Assert.AreEqual<int>(1, low.Readings, "The code of the value that is bound first should run once.")
        Assert.AreEqual<int>(1, high.Readings, "The code of the value that is bound second should run once.")

    [<TestMethod>]
    member _.``A value bound by let must be computed once when two computed parts read it`` () =
        let meter = Meter ()

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let limit = meter.Read 18
                        p.Age > limit + 1 || fst p.Scores > limit + 2
                @>

        CollectionAssert.AreEqual (
            [| ValueSome (box 19); ValueSome (box 20) |],
            shape.Values |> Seq.toArray,
            "Each sum is the largest part at its place that can be computed, so each is a slot of its own."
        )

        Assert.AreEqual<int>(1, meter.Readings, "Both sums read the one value that was bound, whose code should run once.")

    [<TestMethod>]
    member _.``A value bound by let must be computed once when a later value reads it`` () =
        let meter = Meter ()

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let limit = meter.Read 18
                        let twice = limit * 2
                        (p.Age > limit && p.Age < twice)
                        || (fst p.Scores > limit && fst p.Scores < twice)
                @>

        CollectionAssert.AreEqual (
            [| ValueSome (box 18); ValueSome (box 36) |],
            shape.Values |> Seq.toArray,
            "The value and what is made of it are one slot each, however often they are used."
        )

        Assert.AreEqual<int>(1, meter.Readings, "The code of the first value should run once, also for the value made of it.")

    // Literals

    [<TestMethod>]
    member _.``A literal must be a part of the key and no slot`` () =
        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        p.Age > 18
                        && float p.Age > 0.5
                        && p.Name <> ""
                        && p.Name.Contains 'o'
                        && p.Name.StartsWith ("B", StringComparison.Ordinal)
                        && box p.Nick <> null
                        && ignore p.Age = ()
                @>

        Assert.IsEmpty (shape.Values, "A literal is a part of the query text, whatever its type is.")

    [<TestMethod>]
    member _.``A decimal that is written must be a slot, also when it is declared as a literal`` () =
        // The compiler writes a decimal as a call that makes it, never as a value.
        let written = ParameterizerAssert.WantShape <@ fun (price : decimal) -> price > 18.5m @>
        let declared = ParameterizerAssert.WantShape <@ fun (price : decimal) -> price > Literals.Rate @>

        CollectionAssert.AreEqual (
            [| ValueSome (box 18.5m) |],
            written.Values |> Seq.toArray,
            "A decimal should be a parameter, which keeps its digits."
        )

        CollectionAssert.AreEqual (
            [| ValueSome (box 2.5m) |],
            declared.Values |> Seq.toArray,
            "A decimal that is declared as a literal is written into the quotation the same way."
        )

        Assert.AreEqual<ShapeKey>(written.Key, declared.Key, "Two decimals are two values of one query text.")

    [<TestMethod>]
    member _.``A value declared as a literal must be the literal`` () =
        let declared =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Name.StartsWith (Literals.Initial, StringComparison.Ordinal) @>

        let written =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Name.StartsWith ("B", StringComparison.Ordinal) @>

        Assert.IsEmpty (declared.Values, "The compiler writes the value into the quotation, so it is no slot.")
        Assert.AreEqual<ShapeKey>(written.Key, declared.Key, "The declared literal and the written one are one quotation.")

    [<TestMethod>]
    member _.``A value that code put into a quotation must be a slot unless the compiler could have written it`` () =
        let id = Guid.NewGuid ()
        let ofData =
            ParameterizerAssert.WantTemplate <@ fun (documentId : Guid) -> documentId = %%(Expr.Value id) @>
        let ofLiteral = ParameterizerAssert.WantTemplate <@ fun (age : int) -> age = %%(Expr.Value 5) @>

        let slot = Assert.ContainsSingle (ofData.Slots, "No literal is a Guid, so the value is data.")
        Assert.AreEqual<string voption>(ValueNone, slot.Name, "A value without a variable has no name.")
        Assert.AreEqual<objnull>(id, valueOf 0 ofData.Values, "The slot should hold the value.")
        Assert.IsEmpty (ofLiteral.Slots, "A number cannot be told from a literal, so it is one.")

    [<TestMethod>]
    member _.``A nullable that code put into a quotation must be a slot, with a value and without one`` () =
        // A nullable with a value is boxed as the value, so only the type of the node says that it is a nullable.
        let withValue =
            ParameterizerAssert.WantTemplate <@ fun (age : Nullable<int>) -> age = %%(Expr.Value (Nullable 5)) @>
        let withoutValue =
            ParameterizerAssert.WantTemplate <@ fun (age : Nullable<int>) -> age = %%(Expr.Value (Nullable<int>())) @>

        let slot =
            Assert.ContainsSingle (withValue.Slots, "The compiler writes no nullable as a value, so it is data.")
        Assert.AreEqual<Type>(typeof<Nullable<int>>, slot.Variable.Type, "The slot should have the type of the node.")
        Assert.AreEqual<objnull>(5, valueOf 0 withValue.Values, "The slot should hold the value.")

        Assert.ContainsSingle (withoutValue.Slots, "A nullable without a value is data as well, and no null literal.")
        |> ignore

        Assert.IsNull (valueOf 0 withoutValue.Values, "The slot should hold no value.")

    [<TestMethod>]
    member _.``A case of a union that is null must be a slot when code put it into a quotation as a value`` () =
        // The compiler writes a case of a union as a case, also the one that is represented by null. As a value such a
        // null can only come from code, and how it is written into a query is a matter of the union.
        let ofUnion =
            ParameterizerAssert.WantTemplate <@ fun (rank : Rank) -> rank = %%(Expr.Value (null, typeof<Rank>)) @>

        let ofOption =
            ParameterizerAssert.WantTemplate
                <@ fun (nick : string option) -> nick = %%(Expr.Value (null, typeof<string option>)) @>

        let ofText =
            ParameterizerAssert.WantTemplate <@ fun (name : string | null) -> name = %%(Expr.Value (null, typeof<string>)) @>

        let slot =
            Assert.ContainsSingle (ofUnion.Slots, "The null of a union type is a case of the union, so it is data.")
        Assert.AreEqual<Type>(typeof<Rank>, slot.Variable.Type, "The slot should have the type of the union.")
        Assert.IsNull (valueOf 0 ofUnion.Values, "The slot should hold the case, which is null.")

        Assert.ContainsSingle (ofOption.Slots, "An option is such a union, and its None is data as well.")
        |> ignore

        Assert.IsEmpty (ofText.Slots, "The compiler writes the null of a text as a value, so that one is a literal.")

    [<TestMethod>]
    member _.``A function with a definition that is called twice must have the slots of each call`` () =
        // The function binds a number, reads it and reads ten times the number: two values that a call computes.
        let template =
            ParameterizerAssert.WantTemplate
                <@
                    fun (p : Person) ->
                        Conditions.isWithinProcessors p
                        || not (Conditions.isWithinProcessors p)
                @>

        Assert.HasCount (
            4,
            template.Slots,
            "Each call computes its values for itself, so the second call should not have the slots of the first."
        )

    // Values that the quotation builds

    [<TestMethod; DynamicData(nameof ParameterizerTests.BuiltValueRows)>]
    member _.``A value that the quotation builds must stay what it is, with its parts as slots``
        (quotation : Expr, slots : Type array)
        =
        let template = ParameterizerAssert.WantTemplate quotation

        CollectionAssert.AreEqual (
            slots,
            template.Slots |> Seq.map _.Variable.Type |> Seq.toArray,
            "The captured parts should be the slots, and the value that is built of them no slot of its own."
        )

    // The functions of Query

    [<TestMethod>]
    member _.``Query.parameter must make a literal a slot`` () =
        let template = ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > Query.parameter 18 @>
        let other = ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > Query.parameter 21 @>
        let literal = ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > 18 @>

        let slot = Assert.ContainsSingle (template.Slots, "The literal should be a parameter.")
        Assert.AreEqual<string voption>(ValueNone, slot.Name, "A literal has no variable to be named after.")
        Assert.AreEqual<objnull>(18, valueOf 0 template.Values, "The slot should hold the literal.")
        Assert.AreEqual<ShapeKey>(template.Key, other.Key, "Another value of the parameter is the same query text.")
        Assert.AreNotEqual<ShapeKey>(template.Key, literal.Key, "A parameter and a literal are different query texts.")
        Assert.IsFalse (callsQuery template.Body, "The template should hold the variable of the slot, not the call.")

    [<TestMethod>]
    member _.``Query.parameter must leave a captured value the slot it is`` () =
        let limit = 18
        let marked =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > Query.parameter limit || fst p.Scores > limit @>
        let plain =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > limit || fst p.Scores > limit @>

        CollectionAssert.AreEqual (
            [| ValueSome "limit"; ValueSome "limit" |],
            marked.Slots |> Seq.map _.Name |> Seq.toArray,
            "The marked use should be a slot with the name of the variable, like the plain use."
        )

        Assert.AreEqual<ShapeKey>(plain.Key, marked.Key, "Saying what a captured value already is changes nothing.")

    [<TestMethod>]
    member _.``Query.parameter must compute its argument`` () =
        let name = "bob"
        let shape =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Name = Query.parameter (name.ToUpperInvariant ()) @>

        Assert.ContainsSingle (shape.Values, "The argument reads nothing of the document, so it is one slot.")
        |> ignore

        Assert.AreEqual<objnull>("BOB", valueOf 0 shape.Values, "The slot should hold what the argument computes.")

    [<TestMethod>]
    member _.``Query.constant must make a captured value a part of the key`` () =
        let templateOf (limit : int) =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> p.Age > Query.constant limit @>
        let template = templateOf 18
        let literal = NormalizerAssert.WantNormal <@ fun (p : Person) -> p.Age > 18 @>

        Assert.IsEmpty (template.Slots, "A constant is no parameter.")

        Assert.AreEqual<ShapeKey>(
            (ParameterizerAssert.WantShape literal).Key,
            template.Key,
            "The constant should be in the key as the literal with its value is."
        )

        Assert.AreNotEqual<ShapeKey>(template.Key, (templateOf 21).Key, "Another value of a constant is another query text.")

        NormalizerAssert.AreEquivalent
            literal
            template.Body
            "The template should hold the value where the call was, as if the literal had been written."

    [<TestMethod>]
    member _.``Query.constant must compute its argument`` () =
        let limit = 18
        let computed =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > Query.constant (limit + 1) @>
        let literal = ParameterizerAssert.WantShape <@ fun (p : Person) -> p.Age > 19 @>

        Assert.IsEmpty (computed.Values, "A constant is no parameter.")
        Assert.AreEqual<ShapeKey>(literal.Key, computed.Key, "The key should hold what the argument computes.")

    // Choices

    [<TestMethod>]
    member _.``A branch that is not taken must not be computed`` () =
        let shapeOf (filter : string option) =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> filter.IsNone || p.Name = filter.Value.Trim () @>

        let without = shapeOf None
        let withFilter = shapeOf (Some " Bob ")

        CollectionAssert.AreEqual (
            [| ValueSome (box true); ValueNone |],
            without.Values |> Seq.toArray,
            "Without a filter nothing is trimmed: the code would raise, as it would in the compiled function."
        )

        CollectionAssert.AreEqual (
            [| ValueSome (box false); ValueSome (box "Bob") |],
            withFilter.Values |> Seq.toArray,
            "With a filter the branch is taken and its part is computed."
        )

        Assert.AreEqual<ShapeKey>(without.Key, withFilter.Key, "The key should hold both branches, whichever is taken.")

    [<TestMethod>]
    member _.``The other branch of a choice must not be computed when the first one is taken`` () =
        let flag = true
        let filter : string option = None
        let limit = 18

        let shape =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> if flag then p.Age > limit else p.Name = filter.Value @>

        CollectionAssert.AreEqual (
            [| ValueSome (box true); ValueSome (box 18); ValueNone |],
            shape.Values |> Seq.toArray,
            "The branch that is taken has its value, the other one has none."
        )

    [<TestMethod>]
    member _.``A branch inside a branch that is not taken must not be computed`` () =
        let flag = false
        let filter : string option = None

        let shape =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> flag && (filter.Value.Length > 3 || p.Name = filter.Value) @>

        CollectionAssert.AreEqual (
            [| ValueSome (box false); ValueNone; ValueNone |],
            shape.Values |> Seq.toArray,
            "Nothing under a branch that is not taken is computed, whatever its own conditions are."
        )

    [<TestMethod>]
    member _.``A condition that is one slot at two places must decide its branch at each of them`` () =
        let flag = false
        let filter : string option = None

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let enabled = flag
                        (enabled && p.Name = filter.Value)
                        || (enabled && p.Name = filter.Value.Trim ())
                @>

        CollectionAssert.AreEqual (
            [| ValueSome (box false); ValueNone; ValueNone |],
            shape.Values |> Seq.toArray,
            "The slot of the flag is found again at the second place, and there too it should keep its branch from being computed."
        )

    [<TestMethod>]
    member _.``A condition that Query.constant makes a constant must decide its branch`` () =
        let shapeOf (flag : bool) (filter : string option) =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> Query.constant flag || p.Name = filter.Value.Trim () @>

        CollectionAssert.AreEqual (
            [| (ValueNone : objnull voption) |],
            (shapeOf true None).Values |> Seq.toArray,
            "The constant is true, so the other branch is not taken and nothing of it is computed."
        )

        CollectionAssert.AreEqual (
            [| ValueSome (box "Bob") |],
            (shapeOf false (Some " Bob ")).Values |> Seq.toArray,
            "The constant is false, so the other branch is taken and its part is computed."
        )

    [<TestMethod>]
    member _.``A condition that is the default value of a boolean must leave no branch to compute`` () =
        let filter : string option = None
        let shape =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> bool () && p.Name = filter.Value.Trim () @>

        Assert.IsEmpty (
            shape.Values,
            "The condition is false in the quotation itself, so its branch is gone from the normal form and nothing of it is computed."
        )

    [<TestMethod>]
    member _.``A choice by the document must have both its branches computed`` () =
        let limit = 18

        let shape =
            ParameterizerAssert.WantShape <@ fun (p : Person) -> if p.Active then p.Age > limit + 1 else p.Age > limit + 2 @>

        CollectionAssert.AreEqual (
            [| ValueSome (box 19); ValueSome (box 20) |],
            shape.Values |> Seq.toArray,
            "Which branch is taken is known only for a document, so the query needs the values of both."
        )

    [<TestMethod>]
    member _.``A part of a branch that is not taken must be computed when it is used outside the branch too`` () =
        let flag = false
        let meter = Meter ()

        let shape =
            ParameterizerAssert.WantShape
                <@
                    fun (p : Person) ->
                        let name = meter.Read "Bob"
                        (flag && p.Name = name) || p.Nick = Some name
                @>

        CollectionAssert.AreEqual (
            [| ValueSome (box false); ValueSome (box "Bob") |],
            shape.Values |> Seq.toArray,
            "The second use is in a branch that is taken, so the slot of both uses needs its value."
        )

        Assert.AreEqual<int>(1, meter.Readings, "The code of the value should run once.")

    [<TestMethod>]
    member _.``Query.constant must be computed in a branch that is not taken`` () =
        let meter = Meter ()
        let keyOf (flag : bool) =
            (ParameterizerAssert.WantShape <@ fun (p : Person) -> flag || p.Age > Query.constant (meter.Read 18) @>).Key

        let taken = keyOf false
        let readings = meter.Readings
        let notTaken = keyOf true

        Assert.AreEqual<ShapeKey>(taken, notTaken, "A constant is a part of the key, so the key needs it in either case.")
        Assert.AreEqual<int>(readings + 1, meter.Readings, "The constant should be computed although its branch is not taken.")

    // The key

    [<TestMethod>]
    member _.``The key must list the nodes of the quotation, every node before the nodes under it`` () =
        let limit = 18
        let quotation = <@ fun (p : Person) -> p.Age > limit || p.Name = "Bob" @>
        let shape = ParameterizerAssert.WantShape quotation

        let struct (greaterThan, age, equals, name) =
            match quotation with
            | Lambda (_,
                      IfThenElse (Call (None, greaterThan, [ PropertyGet (_, age, _); _ ]),
                                  _,
                                  Call (None, equals, [ PropertyGet (_, name, _); _ ]))) ->
                struct (greaterThan, age, equals, name)
            | other -> invalidOp $"The quotation is not the two comparisons it was written as: %A{other}"

        let token (kind : ShapeTokenKind) (number : int) (subject : objnull) (detail : objnull) : ShapeToken = {
            Kind = kind
            Number = number
            Subject = subject
            Detail = detail
        }

        CollectionAssert.AreEqual (
            [|
                token ShapeTokenKind.Lambda 0 typeof<Person> null
                token ShapeTokenKind.IfThenElse 0 null null
                token ShapeTokenKind.Call 0 greaterThan null
                token ShapeTokenKind.PropertyGet 0 age null
                token ShapeTokenKind.Variable 0 null null
                token ShapeTokenKind.Slot 0 typeof<int> "limit"
                token ShapeTokenKind.Constant 0 typeof<bool> true
                token ShapeTokenKind.Call 0 equals null
                token ShapeTokenKind.PropertyGet 0 name null
                token ShapeTokenKind.Variable 0 null null
                token ShapeTokenKind.Constant 0 typeof<string> "Bob"
            |],
            shape.Key.Tokens |> Seq.toArray,
            "The key should hold the function, the choice that `||` is, and each comparison with what it compares."
        )

        Assert.HasCount (
            shape.Key.Tokens.Length,
            (string shape.Key).Split('\n', StringSplitOptions.RemoveEmptyEntries),
            "The key should be written with one token on a line."
        )

    [<TestMethod>]
    member _.``Two keys with one hash code must be told apart by their tokens`` () =
        let keyOf (token : ShapeToken) : ShapeKey = {
            Tokens = System.Collections.Immutable.ImmutableArray.Create<ShapeToken> token
            Hash = 1
        }

        let token : ShapeToken = {
            Kind = ShapeTokenKind.Constant
            Number = 0
            Subject = typeof<int>
            Detail = 18
        }

        Assert.AreEqual<ShapeKey>(keyOf token, keyOf token, "Keys with equal tokens are equal, whichever arrays hold the tokens.")

        // A hash code is only the first thing two keys are compared by: after it, everything a token says counts.
        Assert.AreNotEqual<ShapeKey>(
            keyOf token,
            keyOf { token with Kind = ShapeTokenKind.Slot },
            "Tokens of different kinds are different tokens."
        )

        Assert.AreNotEqual<ShapeKey>(keyOf token, keyOf { token with Number = 1 }, "The number of a token is a part of it.")

        Assert.AreNotEqual<ShapeKey>(
            keyOf token,
            keyOf { token with Subject = typeof<int64> },
            "The subject of a token is a part of it."
        )

        Assert.AreNotEqual<ShapeKey>(keyOf token, keyOf { token with Detail = 21 }, "The detail of a token is a part of it.")

    [<TestMethod>]
    member _.``A variable must be in the key as the distance to its binding`` () =
        let keyOf (expr : Expr) = (ParameterizerAssert.WantShape expr).Key
        let outer = keyOf <@ fun (first : int) (second : int) -> first @>
        let inner = keyOf <@ fun (first : int) (second : int) -> second @>
        let renamed = keyOf <@ fun (a : int) (b : int) -> a @>

        Assert.AreNotEqual<ShapeKey>(outer, inner, "Functions that return different variables are different functions.")
        Assert.AreEqual<ShapeKey>(outer, renamed, "The names of the variables are not in the key.")

    [<TestMethod>]
    member _.``A struct tuple and a reference tuple must have different keys`` () =
        let keyOf (expr : Expr) = (ParameterizerAssert.WantShape expr).Key

        Assert.AreNotEqual<ShapeKey>(
            keyOf <@ fun (p : Person) -> (p.Name, p.Age) @>,
            keyOf <@ fun (p : Person) -> struct (p.Name, p.Age) @>,
            "The two kinds of tuples can be written into a document in different ways, so they are different queries."
        )

    [<TestMethod>]
    member _.``A variable of a delegate must be in the key as the distance to its binding`` () =
        let keyOf (expr : Expr) = (ParameterizerAssert.WantShape expr).Key
        let first = keyOf <@ Func<int, int, int>(fun first second -> first) @>
        let second = keyOf <@ Func<int, int, int>(fun first second -> second) @>
        let renamed = keyOf <@ Func<int, int, int>(fun a b -> a) @>

        Assert.AreNotEqual<ShapeKey>(first, second, "Delegates that return different variables are different delegates.")
        Assert.AreEqual<ShapeKey>(first, renamed, "The names of the variables are not in the key.")

    [<TestMethod>]
    member _.``A captured value inside a delegate must be a slot`` () =
        let name = "vip"

        let template =
            ParameterizerAssert.WantTemplate <@ fun (p : Person) -> Enumerable.Any (p.Tags, (fun tag -> tag = name)) @>

        let slot = Assert.ContainsSingle (template.Slots, "The delegate captures one value.")
        Assert.AreEqual<string voption>(ValueSome "name", slot.Name, "The slot should have the name of the variable.")

        CollectionAssert.AreEqual (
            [| slot.Variable |],
            template.Body.GetFreeVars () |> Seq.toArray,
            "The delegate should be built anew around the variable of the slot."
        )

    [<TestMethod>]
    member _.``A key that no code has filled must be equal to itself and hold no token`` () =
        let empty = Unchecked.defaultof<ShapeKey>

        Assert.AreEqual<ShapeKey>(empty, Unchecked.defaultof<ShapeKey>, "A key without tokens is a key like any other.")
        Assert.AreEqual<string>("", string empty, "It should be written as no tokens, not fail.")

    [<TestMethod>]
    member _.``A quotation without slots must be its own template`` () =
        let normal = NormalizerAssert.WantNormal <@ fun (p : Person) -> p.Age > 18 @>
        let template =
            Assert.WantOk (Parameterizer.templateOf normal, "A quotation of literals has a template.")

        Assert.AreSame<Expr>(normal, template.Body, "Nothing is replaced, so no node should be built.")

    // Errors

    [<TestMethod; DynamicData(nameof ParameterizerTests.FunctionValueRows)>]
    member _.``A function value in the place of a value of the query must be reported`` (quotation : Expr) =
        let error = ParameterizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.CapturedFunctionValue,
            error.Code,
            "A compiled function can be neither read nor passed as a parameter."
        )

        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")
        Assert.IsValueSome (error.Location, "The function was passed in this file, so the error has its place.")

    [<TestMethod; DynamicData(nameof ParameterizerTests.NotLiteralRows)>]
    member _.``A value that is no literal must not be made a constant`` (quotation : Expr) =
        let error = ParameterizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.Unsupported,
            error.Code,
            "The key holds a constant, and only a literal says by its equality what a query writes for it."
        )

        Assert.Contains (
            "Query.constant",
            error.Message,
            StringComparison.Ordinal,
            "The message should name the function that was given the value."
        )

        Assert.Contains (
            "the null of a reference type that is no union of F#, as an option is",
            error.Message,
            StringComparison.Ordinal,
            "The message should not call a case of a union that is null a literal: it is the null of a reference type, and two of these rows."
        )

        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")
        Assert.IsValueSome (error.Location, "The call was written in this file, so the error has its place.")

    [<TestMethod>]
    member _.``Code that raises an exception must be reported with the exception as the cause`` () =
        let zero = 0
        let error = ParameterizerAssert.WantError <@ fun (p : Person) -> p.Age > 10 / zero @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.ClientEvaluationFailed,
            error.Code,
            "The part has to be computed, and computing it failed."
        )

        let cause = Assert.WantValueSome (error.Cause, "The error should carry the exception.")
        Assert.IsInstanceOfType (cause, typeof<DivideByZeroException>, "The cause should be the exception of the division.")

    [<TestMethod>]
    member _.``Query.constant of code that raises an exception must be reported`` () =
        let zero = 0
        let error = ParameterizerAssert.WantError <@ fun (p : Person) -> p.Age > Query.constant (10 / zero) @>

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.ClientEvaluationFailed,
            error.Code,
            "The constant has to be computed, and computing it failed."
        )

    [<TestMethod>]
    [<DataRow(true, DisplayName = "Query.constant of a value of the document must be reported")>]
    [<DataRow(false, DisplayName = "Query.parameter of a value of the document must be reported")>]
    member _.``A function of Query over a value of the document must be reported`` (isConstant : bool) =
        let struct (name, quotation) =
            if isConstant then
                struct ("Query.constant", <@@ fun (p : Person) -> p.Age > Query.constant p.Age @@>)
            else
                struct ("Query.parameter", <@@ fun (p : Person) -> p.Age > Query.parameter p.Age @@>)

        let error = ParameterizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.Unsupported,
            error.Code,
            "A value of the document is not known before the query runs."
        )

        Assert.Contains (name, error.Message, StringComparison.Ordinal, "The message should name the function.")
        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")

    [<TestMethod>]
    member _.``A variable that nothing binds must be reported`` () =
        let stray = Var ("stray", typeof<int>)
        let quotation = <@ fun (p : Person) -> p.Age > %%(Expr.Var stray) @>
        let error = ParameterizerAssert.WantError quotation

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.Unsupported, error.Code, "The variable has no value.")
        Assert.Contains ("stray", error.Message, StringComparison.Ordinal, "The message should name the variable.")

    [<TestMethod>]
    member _.``A quotation inside the quotation must be reported`` () =
        let person = Var ("p", typeof<Person>)
        let quotation = Expr.Lambda (person, Expr.QuoteTyped <@@ 1 @@>)

        let error =
            Assert.WantError (Parameterizer.shapeOf quotation, "A quotation is neither a value nor an operation on the document.")

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.Unsupported, error.Code, "A query cannot express it.")
