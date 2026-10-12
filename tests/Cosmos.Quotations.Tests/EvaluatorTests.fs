namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.Reflection
open System.Reflection.Emit
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos.Quotations
open FSharp.Azure.Cosmos.Tests

/// <summary>
/// A value written as code that reads no variable of a quotation: the part of the quotation to compute, and what the
/// compiled code gives for it.
/// </summary>
type Computation = {
    /// What kind of code computes the value; the name of a test result starts with it.
    Name : string
    /// The code in the normal form of the normalizer, without the function it was written in.
    Part : Expr
    /// Fails unless a value is what the compiled code gives.
    IsExpected : objnull -> unit
} with

    /// <summary>
    /// The part of a quotation that <paramref name="code"/> computes. The compiler passes the code together with its
    /// quotation, so the expected value is what the code gives when it runs.
    /// </summary>
    static member Of<'Result when 'Result : equality>
        (name : string, [<ReflectedDefinition(true)>] code : Expr<unit -> 'Result>)
        : Computation
        =
        let code = Quoted.Split code

        {
            Name = name
            Part = Computation.PartOf code.Quotation
            IsExpected =
                fun value ->
                    Assert.AreEqual<'Result>(
                        code.Compiled (),
                        (value :?> 'Result),
                        "The evaluator should compute what the compiled code computes."
                    )

                    // A boxed number can be taken out as an enumeration value, so the cast above does not tell them
                    // apart, but a parameter of a query is written by the type its value has.
                    match value with
                    | null -> ()
                    | value when typeof<'Result>.IsEnum ->
                        Assert.AreEqual<Type>(
                            typeof<'Result>,
                            value.GetType (),
                            "A value of an enumeration type should be of that type, not the number under it."
                        )
                    | _ -> ()
        }

    /// <summary>
    /// The body of the function of the unit value that <paramref name="code"/> is, in normal form: a part that reads no
    /// variable.
    /// </summary>
    static member PartOf (code : Expr) : Expr =
        match NormalizerAssert.WantNormal code with
        | Lambda (_, part) -> part
        | normal -> invalidArg (nameof code) $"The code should be a function of the unit value: %A{normal}"

/// <summary>
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.tryEvaluate(Microsoft.FSharp.Quotations.FSharpExpr)"/>: the
/// value of a part of a quotation that reads no variable is what the compiled code of that part gives, and an exception
/// of that code is an error with the exception as its cause.
/// </summary>
[<TestClass; EvaluatorUnitTestCategory>]
type EvaluatorTests () =

    static let computations =
        // Captured by the code below, as a query captures the values it is asked with.
        let limit = 30
        let price = 12.5
        let amount = 19.99m
        let name = "Bob"
        // A date is read as the field of a captured record: the compiler refuses to read a member of a captured local
        // of a value type in a quotation (FS3155).
        let meeting = {| At = DateTime (2024, 2, 29, 13, 30, 0, DateTimeKind.Utc) |}
        let nick = Some "bobby"
        let noNick : string option = None
        let alias = ValueSome "bo"
        let pair = (3, "three")
        let structPair = struct (4, "four")
        let eight = (1, 2, 3, 4, 5, 6, 7, "eight")
        let range = { Low = 18; High = 65 }
        let tags = [ "vip"; "new" ]
        let filter = OlderThan 18
        let addOne = fun (number : int) -> number + 1
        let add = fun (first : int) (second : int) -> first + second
        let wait = {| For = TimeSpan.FromMinutes 90.0 |}
        // A nullable is read as the field of a captured record, like the date above.
        let optional = {| Limit = Nullable 30; Missing = Nullable<int>() |}
        let flags = BindingFlags.Public ||| BindingFlags.Instance

        seq {
            // Values and operators
            Computation.Of ("a captured value", (fun () -> limit))
            Computation.Of ("a constant", (fun () -> 42))
            Computation.Of ("the sum of a captured value and a constant", (fun () -> limit + 1))
            Computation.Of ("arithmetic on integers", (fun () -> (limit - 4) * 3 / 2 % 7))
            Computation.Of ("the negation of a number", (fun () -> -limit))
            Computation.Of ("arithmetic on floating-point numbers", (fun () -> price * 2.0 + 0.5))
            Computation.Of ("arithmetic on decimals", (fun () -> amount * 2m + 0.01m))
            Computation.Of ("arithmetic on 64-bit integers", (fun () -> int64 limit * 4_000_000_000L))
            Computation.Of ("a conversion to a floating-point number", (fun () -> float limit / 4.0))
            Computation.Of ("a conversion to an integer", (fun () -> int price))
            Computation.Of ("a conversion to a string", (fun () -> string limit))
            Computation.Of ("the concatenation of strings", (fun () -> name + "!"))
            Computation.Of ("a comparison", (fun () -> limit > 18))
            Computation.Of ("an equality of strings", (fun () -> name = "Bob"))
            Computation.Of ("a negation", (fun () -> not (limit > 18)))

            // Operators that FSharp.Core does not let reflection call
            Computation.Of ("a subtraction", (fun () -> limit - 4))
            Computation.Of ("a division", (fun () -> limit / 4))
            Computation.Of ("a remainder", (fun () -> limit % 4))
            Computation.Of ("a division of floating-point numbers", (fun () -> price / 4.0))
            Computation.Of ("a number with a plus sign", (fun () -> +limit))
            Computation.Of ("the complement of the bits of a number", (fun () -> ~~~limit))
            Computation.Of ("a bitwise and", (fun () -> limit &&& 12))
            Computation.Of ("a bitwise or", (fun () -> limit ||| 1))
            Computation.Of ("a bitwise exclusive or", (fun () -> limit ^^^ 5))
            Computation.Of ("a shift to the left", (fun () -> limit <<< 2))
            Computation.Of ("a shift to the right", (fun () -> limit >>> 2))
            Computation.Of ("a conversion to a byte", (fun () -> byte limit))
            Computation.Of ("a conversion to a signed byte", (fun () -> sbyte limit))
            Computation.Of ("a conversion to a 16-bit integer", (fun () -> int16 limit))
            Computation.Of ("a conversion to an unsigned 16-bit integer", (fun () -> uint16 limit))
            Computation.Of ("a conversion to a 32-bit integer", (fun () -> int32 price))
            Computation.Of ("a conversion to an unsigned 32-bit integer", (fun () -> uint32 limit))
            Computation.Of ("a conversion to a 64-bit integer", (fun () -> int64 price))
            Computation.Of ("a conversion to an unsigned 64-bit integer", (fun () -> uint64 limit))
            Computation.Of ("a conversion to a native integer", (fun () -> nativeint limit))
            Computation.Of ("a conversion to an unsigned native integer", (fun () -> unativeint limit))
            Computation.Of ("a conversion to a single-precision number", (fun () -> float32 limit))
            Computation.Of ("a conversion to a decimal", (fun () -> decimal limit))
            Computation.Of ("a conversion to a character", (fun () -> char (limit + 35)))
            Computation.Of ("a conversion that cuts the value off", (fun () -> byte (limit * 10)))
            Computation.Of ("a checked negation", (fun () -> Checked.(~-) limit))
            Computation.Of ("a checked conversion to a byte", (fun () -> Checked.byte limit))
            Computation.Of ("a checked conversion to a signed byte", (fun () -> Checked.sbyte limit))
            Computation.Of ("a checked conversion to a 16-bit integer", (fun () -> Checked.int16 limit))
            Computation.Of ("a checked conversion to an unsigned 16-bit integer", (fun () -> Checked.uint16 limit))
            Computation.Of ("a checked conversion to a 32-bit integer", (fun () -> Checked.int32 price))
            Computation.Of ("a checked conversion to an unsigned 32-bit integer", (fun () -> Checked.uint32 limit))
            Computation.Of ("a checked conversion to a 64-bit integer", (fun () -> Checked.int64 price))
            Computation.Of ("a checked conversion to an unsigned 64-bit integer", (fun () -> Checked.uint64 limit))
            Computation.Of ("a checked conversion to a native integer", (fun () -> Checked.nativeint limit))
            Computation.Of ("a checked conversion to an unsigned native integer", (fun () -> Checked.unativeint limit))
            Computation.Of ("a checked conversion to a character", (fun () -> Checked.char (limit + 35)))
            Computation.Of ("a subtraction of decimals", (fun () -> amount - 0.99m))
            Computation.Of ("a division of a type with its own operator", (fun () -> wait.For / 2.0))
            Computation.Of ("the negation of a type with its own operator", (fun () -> -wait.For))
            Computation.Of ("the number of an enumeration value", (fun () -> int meeting.At.DayOfWeek))
            Computation.Of ("the 64-bit number of an enumeration value", (fun () -> int64 meeting.At.DayOfWeek))
            Computation.Of ("the checked number of an enumeration value", (fun () -> Checked.int meeting.At.DayOfWeek))
            Computation.Of ("the enumeration value of a number", (fun () -> enum<DayOfWeek>(limit - 25)))
            Computation.Of ("a combination of flags", (fun () -> BindingFlags.Public ||| BindingFlags.Static))
            Computation.Of ("the flags that two values share", (fun () -> flags &&& BindingFlags.Public))
            Computation.Of ("the complement of flags", (fun () -> ~~~flags))
            Computation.Of ("flags without one of them", (fun () -> flags &&& ~~~BindingFlags.Instance))

            // Choices, which compute one branch only
            Computation.Of ("a choice", (fun () -> if limit > 18 then "adult" else "minor"))
            Computation.Of (
                "a conjunction whose second condition would fail",
                (fun () -> noNick.IsSome && noNick.Value.Length > 0)
            )
            Computation.Of (
                "a disjunction whose second condition would fail",
                (fun () -> noNick.IsNone || noNick.Value.Length > 0)
            )
            Computation.Of ("a conjunction of two conditions that hold", (fun () -> nick.IsSome && nick.Value.Length > 0))

            // Members
            Computation.Of ("a static field", (fun () -> DateTime.UnixEpoch))
            Computation.Of ("a static property", (fun () -> CultureInfo.InvariantCulture.Name))
            Computation.Of ("a module-level value", (fun () -> Conditions.retirementAge))
            Computation.Of ("a property of a string", (fun () -> name.Length))
            Computation.Of ("a chain of properties", (fun () -> meeting.At.Date.Year))
            Computation.Of ("a method with an argument", (fun () -> meeting.At.AddDays 2.0))
            Computation.Of ("a method of a string", (fun () -> name.ToUpperInvariant().Substring 1))
            Computation.Of ("a static method", (fun () -> Math.Max (limit, 18)))
            Computation.Of ("a static method that parses", (fun () -> Guid.Parse "0d8f4b51-7d0d-4c35-9a53-4e0b3a1f6f10"))
            Computation.Of ("a generic function", (fun () -> List.length tags))
            Computation.Of ("a function of a module", (fun () -> String.concat "," tags))
            Computation.Of ("a function without a reflected definition", (fun () -> Conditions.isTeenager Persons.child))
            Computation.Of ("a function with a reflected definition that calls itself", (fun () -> Conditions.factorial 5))
            Computation.Of ("a method of a captured record", (fun () -> Persons.retired.IsRetired ()))
            Computation.Of ("an operator of a type", (fun () -> (meeting.At - DateTime.UnixEpoch).TotalDays))
            Computation.Of ("a member of an enumeration value", (fun () -> meeting.At.DayOfWeek = DayOfWeek.Thursday))

            // Values that are built
            Computation.Of ("a constructor", (fun () -> DateTime (2020, 1, 2)))
            Computation.Of ("a nullable value", (fun () -> Nullable limit))
            Computation.Of ("a tuple", (fun () -> (limit, name)))
            Computation.Of ("a struct tuple", (fun () -> struct (limit, name)))
            Computation.Of ("a record", (fun () -> { Low = limit; High = 65 }))
            Computation.Of ("an anonymous record", (fun () -> {| Name = name; Limit = limit |}))
            Computation.Of ("a struct anonymous record", (fun () -> struct {| Name = name; Limit = limit |}))
            Computation.Of ("a union case with a field", (fun () -> ByName name))
            Computation.Of ("an option with a value", (fun () -> Some limit))
            Computation.Of ("an option without a value", (fun () -> (None : int option)))
            Computation.Of ("a value option with a value", (fun () -> ValueSome name))
            Computation.Of ("a list", (fun () -> [ limit; 1 ]))
            Computation.Of ("a default value of a struct", (fun () -> Unchecked.defaultof<DateTime>))
            Computation.Of ("a default value of a class", (fun () -> Unchecked.defaultof<Uri>))
            Computation.Of ("a struct that is made without arguments", (fun () -> DateTime ()))
            Computation.Of ("a nullable without a value", (fun () -> Nullable<int>()))

            // Values that are taken apart
            Computation.Of ("the first item of a captured pair", (fun () -> fst pair))
            Computation.Of ("the second item of a captured pair", (fun () -> snd pair))
            Computation.Of (
                "an item of a captured struct tuple",
                (fun () ->
                    let struct (number, _) = structPair
                    number
                )
            )
            Computation.Of (
                "the eighth item of a captured tuple",
                (fun () ->
                    let (_, _, _, _, _, _, _, last) = eight
                    last
                )
            )
            Computation.Of ("a field of a captured record", (fun () -> range.High))
            Computation.Of (
                "a match on a captured option with a value",
                (fun () ->
                    match nick with
                    | Some text -> text
                    | None -> ""
                )
            )
            Computation.Of (
                "a match on a captured option without a value",
                (fun () ->
                    match noNick with
                    | Some text -> text
                    | None -> "nobody"
                )
            )
            Computation.Of (
                "a match on a captured value option",
                (fun () ->
                    match alias with
                    | ValueSome text -> text
                    | ValueNone -> ""
                )
            )
            Computation.Of (
                "a match on a captured union",
                (fun () ->
                    match filter with
                    | ByName text -> text
                    | OlderThan age -> string age
                )
            )
            Computation.Of (
                "a test of the type of a value",
                (fun () ->
                    match box name with
                    | :? string as text -> text.Length
                    | _ -> -1
                )
            )
            Computation.Of (
                "a test of the type of a value of another type",
                (fun () ->
                    match box limit with
                    | :? string as text -> text.Length
                    | _ -> -1
                )
            )
            Computation.Of ("a cast to an interface", (fun () -> (tags :> seq<string>) |> Seq.length))

            // Members of a nullable, which reflection has nothing to call on: a nullable is boxed as its value or as null
            Computation.Of ("a nullable with a value that is asked whether it has one", (fun () -> optional.Limit.HasValue))
            Computation.Of ("a nullable without a value that is asked whether it has one", (fun () -> optional.Missing.HasValue))
            Computation.Of ("the value of a nullable", (fun () -> optional.Limit.Value))
            Computation.Of ("the value of a nullable or the default of its type", (fun () -> optional.Limit.GetValueOrDefault ()))
            Computation.Of (
                "the default of its type for a nullable without a value",
                (fun () -> optional.Missing.GetValueOrDefault ())
            )
            Computation.Of ("the value of a nullable or a default", (fun () -> optional.Limit.GetValueOrDefault 5))
            Computation.Of ("a default for a nullable without a value", (fun () -> optional.Missing.GetValueOrDefault 5))
            Computation.Of ("a nullable with a value as text", (fun () -> optional.Limit.ToString ()))
            Computation.Of ("a nullable without a value as text", (fun () -> optional.Missing.ToString ()))
            Computation.Of ("a nullable that is compared with a value", (fun () -> optional.Limit.Equals 30))
            Computation.Of ("a nullable without a value that is compared with null", (fun () -> optional.Missing.Equals null))
            Computation.Of ("the hash code of a nullable", (fun () -> optional.Limit.GetHashCode ()))
            Computation.Of ("the hash code of a nullable without a value", (fun () -> optional.Missing.GetHashCode ()))
            Computation.Of ("a nullable that is made of a value", (fun () -> Nullable limit))
            Computation.Of ("a nullable that a function takes", (fun () -> Option.ofNullable optional.Missing))

            // Captured functions
            Computation.Of ("a captured function that is applied", (fun () -> addOne limit))
            Computation.Of ("a captured function of two arguments", (fun () -> add limit 1))
            Computation.Of ("a captured function that is applied to what another gives", (fun () -> addOne (add limit 1)))
        }
        |> ImmutableArray.CreateRange

    static let failures =
        let name = "Bob"
        let limit = 30
        let noNick : string option = None
        let refuse : unit -> int = fun () -> invalidOp "No value."
        let optional = {| Missing = Nullable<int>() |}
        let missing : int -> int = Unchecked.defaultof<int -> int>
        let noName : string = Unchecked.defaultof<string>

        seq {
            struct ("a text that is no number", Computation.PartOf <@ fun () -> Int32.Parse name @>, typeof<FormatException>)
            struct ("a division by zero", Computation.PartOf <@ fun () -> limit / (limit - 30) @>, typeof<DivideByZeroException>)
            struct ("a checked conversion that does not fit",
                    Computation.PartOf <@ fun () -> Checked.byte (limit * 10) @>,
                    typeof<OverflowException>)
            struct ("a checked negation that does not fit",
                    Computation.PartOf <@ fun () -> Checked.(~-) (Int32.MinValue + limit - 30) @>,
                    typeof<OverflowException>)
            struct ("a member of a value that is null",
                    Computation.PartOf <@ fun () -> noNick.Value.Length @>,
                    typeof<NullReferenceException>)
            struct ("a captured function that raises",
                    Computation.PartOf <@ fun () -> refuse () + 1 @>,
                    typeof<InvalidOperationException>)
            struct ("the value of a nullable without a value",
                    Computation.PartOf <@ fun () -> optional.Missing.Value @>,
                    typeof<InvalidOperationException>)
            struct ("a function that is null and is applied",
                    Computation.PartOf <@ fun () -> missing limit @>,
                    typeof<NullReferenceException>)
            // Compiled code computes the arguments before it finds that there is nothing to call.
            struct ("a method of a value that is null with an argument that raises",
                    Computation.PartOf <@ fun () -> noName.Substring (limit / (limit - 30)) @>,
                    typeof<DivideByZeroException>)
            struct ("an indexed property of a value that is null with an argument that raises",
                    Computation.PartOf <@ fun () -> noName.Chars (limit / (limit - 30)) @>,
                    typeof<DivideByZeroException>)
            struct ("a function that is null and is applied to an argument that raises",
                    Computation.PartOf <@ fun () -> missing (limit / (limit - 30)) @>,
                    typeof<DivideByZeroException>)
        }
        |> ImmutableArray.CreateRange

    /// <summary>
    /// Every computation as a row of a data-driven test. The name of a row replaces the name of the test method in the
    /// results, so it says what is asserted.
    /// </summary>
    static member Computations : IEnumerable<TestDataRow<Computation>> =
        computations
        |> Seq.map (fun computation ->
            TestDataRow<Computation>(
                computation,
                DisplayName = $"%s{computation.Name} must have the value that the compiled code gives"
            )
        )

    /// <summary>
    /// Every code that raises an exception, with the type of the exception, as a row of a data-driven test.
    /// </summary>
    static member Failures : IEnumerable<TestDataRow<struct (Expr * Type)>> =
        failures
        |> Seq.map (fun (struct (name, part, cause)) ->
            TestDataRow<struct (Expr * Type)>(
                struct (part, cause),
                DisplayName = $"%s{name} must be reported with the exception as the cause"
            )
        )

    [<TestMethod; DynamicData(nameof EvaluatorTests.Computations)>]
    member _.``A part that reads no variable must have the value that the compiled code gives`` (computation : Computation) =
        match Evaluator.tryEvaluate computation.Part with
        | Ok value -> computation.IsExpected value
        | Error error -> Assert.Fail $"The part should have a value, but: %s{error.Message}\n%s{error.Fragment}"

    [<TestMethod; DynamicData(nameof EvaluatorTests.Failures)>]
    member _.``Code that raises an exception must be reported with the exception as the cause`` (part : Expr, cause : Type) =
        let error =
            Assert.WantError (Evaluator.tryEvaluate part, "The code raises an exception, so the part has no value.")

        Assert.AreEqual<TranslationErrorCode>(
            TranslationErrorCode.ClientEvaluationFailed,
            error.Code,
            "An exception of the caller's code is a failed evaluation."
        )

        let raised = Assert.WantValueSome (error.Cause, "The error should carry the exception.")
        Assert.IsInstanceOfType (raised, cause, "The cause should be the exception of the code, not a wrapper of reflection.")
        Assert.Contains (cause.Name, error.Message, StringComparison.Ordinal, "The message should name the exception.")
        Assert.IsNotEmpty (error.Hints, "The error should say what to write instead.")
        Assert.IsValueSome (error.Location, "The part was written in this file, so it has its place.")

    [<TestMethod>]
    member _.``Every operator that FSharp.Core does not let reflection call must have a function that stands in for it`` () =
        // FSharp.Core marks the methods that raise when reflection calls them. Rethrow and Reraise are among them, and
        // have nothing to compute.
        let isMarked (method : MethodInfo) =
            method.GetCustomAttributesData ()
            |> Seq.exists (fun attribute -> attribute.AttributeType.Name = "NoDynamicInvocationAttribute")

        let withoutStandIn =
            seq {
                typeof<unit>.Assembly.GetType("Microsoft.FSharp.Core.Operators", true)
                typeof<unit>.Assembly.GetType("Microsoft.FSharp.Core.Operators+Checked", true)
            }
            |> Seq.collect (fun operators ->
                match operators with
                | null -> Seq.empty
                | operators ->
                    operators.GetMethods (BindingFlags.Public ||| BindingFlags.Static)
                    |> Seq.ofArray
            )
            |> Seq.filter (fun method ->
                isMarked method
                && not (method.Name.EndsWith ("$W", StringComparison.Ordinal))
                && method.Name <> "Rethrow"
                && method.Name <> "Reraise"
                && not (Evaluator.dynamicOperators.ContainsKey method.MetadataToken)
            )
            |> Seq.map (fun method -> $"%s{(nonNull method.DeclaringType).Name}.%s{method.Name}")
            |> Seq.toArray

        Assert.IsEmpty (
            withoutStandIn,
            "A version of FSharp.Core that keeps another operator from reflection needs another row in the evaluator."
        )

    [<TestMethod>]
    member _.``Every part that the evaluator computes must be told computable`` () =
        let refused =
            seq {
                for computation in computations do
                    if not (Evaluator.canEvaluate (Evaluator.Refused ()) computation.Part) then
                        computation.Name

                for struct (name, part, _) in failures do
                    if not (Evaluator.canEvaluate (Evaluator.Refused ()) part) then
                        name
            }
            |> Seq.toArray

        Assert.IsEmpty (
            refused,
            "A part that has a value, or that raises while it is computed, is made only of what the evaluator computes."
        )

    [<TestMethod>]
    member _.``A part that holds a function, a variable or a quotation must be told not computable, and none of it must run`` () =
        let meter = Meter ()
        let variable = Expr.Var (Var ("number", typeof<int>))

        // In each of the first two the part before the one that cannot be computed can be: asking must not run it.
        let withVariable = <@@ [| meter.Read 1; %%variable |] @@>
        let withFunction = <@@ List.map (fun number -> number + 1) [ meter.Read 1 ] @@>
        let quotation = Expr.QuoteTyped <@@ 1 @@>

        let refused = Evaluator.Refused ()

        Assert.IsFalse (Evaluator.canEvaluate refused withVariable, "A variable has a value only inside the query.")
        Assert.IsFalse (Evaluator.canEvaluate refused withFunction, "A function cannot be computed without compiling it.")
        Assert.IsFalse (Evaluator.canEvaluate refused quotation, "A quotation inside the quotation is not computed.")
        Assert.AreEqual<int>(0, meter.Readings, "Asking whether a part can be computed should run no code of it.")

        Assert.IsFalse (Evaluator.canEvaluate refused withVariable, "A part that was refused stays refused.")
        Assert.IsTrue (refused.Holds variable, "The variable is the reason, so it is remembered with the array around it.")

        match withVariable with
        | NewArray (_, [ first; _ ]) ->
            Assert.IsTrue (Evaluator.canEvaluate refused first, "The item before the variable can be computed on its own.")
        | other -> Assert.Fail $"The quotation should be an array of two items: %A{other}"

    [<TestMethod>]
    member _.``A value that is bound once must be computed once for all its places in a part`` () =
        let meter = Meter ()

        let part =
            Computation.PartOf
                <@
                    fun () ->
                        let made = meter.Read (obj ())
                        obj.ReferenceEquals (made, made)
                @>

        Assert.AreEqual<objnull>(
            true,
            Assert.WantOk (Evaluator.tryEvaluate part, "A comparison of values that read no variable has a value."),
            "Both places hold the node that was bound, so both should get the one object it makes, as in compiled code."
        )

        Assert.AreEqual<int>(1, meter.Readings, "The code of the bound value should run once.")

    [<TestMethod>]
    member _.``A node that was computed must not be computed again for a caller that keeps what was computed`` () =
        let meter = Meter ()

        // Both parts hold the very same node: a splice puts the node it is given into the quotation.
        let reading = <@ meter.Read 18 @>
        let first = NormalizerAssert.WantNormal <@@ %reading + 1 @@>
        let second = NormalizerAssert.WantNormal <@@ %reading + 2 @@>
        let computed = Evaluator.Computed ()

        Assert.AreEqual<objnull>(
            19,
            Assert.WantOk (Evaluator.tryEvaluateOnce computed first, "A sum of values that read no variable has a value."),
            "The first part should be computed with the value of the node."
        )

        Assert.AreEqual<objnull>(
            20,
            Assert.WantOk (Evaluator.tryEvaluateOnce computed second, "A sum of values that read no variable has a value."),
            "The second part should be computed with the value that the node had for the first one."
        )

        Assert.AreEqual<int>(1, meter.Readings, "The code of the node should run once for both parts.")

        Assert.WantOk (Evaluator.tryEvaluate second, "A sum of values that read no variable has a value.")
        |> ignore

        Assert.AreEqual<int>(2, meter.Readings, "A caller that keeps nothing has the part computed anew.")

    [<TestMethod>]
    member _.``An array must be built with its items in their order`` () =
        let limit = 30
        let part = Computation.PartOf <@ fun () -> [| limit; 1; limit + 1 |] @>
        let value =
            Assert.WantOk (Evaluator.tryEvaluate part, "An array of values that read no variable has a value.")

        match value with
        | :? (int array) as items ->
            CollectionAssert.AreEqual (
                [| 30; 1; 31 |],
                items,
                "The array should hold the three values in the order they are written."
            )
        | other -> Assert.Fail $"The value should be an array of integers, but is %A{other}."

    [<TestMethod>]
    member _.``The default value of a struct must be its zeroed value when the struct has a constructor without parameters`` () =
        // F# declares no struct with a constructor without parameters, as C# can, so the test makes one: its
        // constructor sets the field to 7.
        let declared =
            AssemblyBuilder
                .DefineDynamicAssembly(AssemblyName "EvaluatorTests.Dynamic", AssemblyBuilderAccess.Run)
                .DefineDynamicModule("Structs")
                .DefineType(
                    "Seven",
                    TypeAttributes.Public
                    ||| TypeAttributes.Sealed
                    ||| TypeAttributes.SequentialLayout,
                    typeof<ValueType>
                )

        let field = declared.DefineField ("Value", typeof<int>, FieldAttributes.Public)

        let code =
            declared.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator()

        code.Emit OpCodes.Ldarg_0
        code.Emit (OpCodes.Ldc_I4, 7)
        code.Emit (OpCodes.Stfld, field)
        code.Emit OpCodes.Ret

        let seven = declared.CreateType ()
        let valueOf (instance : objnull) = (nonNull (seven.GetField "Value")).GetValue instance

        Assert.AreEqual<objnull>(
            box 7,
            valueOf (Activator.CreateInstance seven),
            "The constructor of the struct sets the field, so a value that it made is told from the default value."
        )

        match Evaluator.tryEvaluate (Expr.DefaultValue seven) with
        | Ok value ->
            Assert.AreEqual<objnull>(
                box 0,
                valueOf value,
                "The default value is the zeroed value: no constructor runs for it, as none runs in compiled code."
            )
        | Error error -> Assert.Fail $"The default value should be computed, but: %s{error.Message}"

    [<TestMethod>]
    member _.``A coercion must give the value only when the value is of the target type`` () =
        // The compiler writes a coercion only where the cast cannot fail, so these are built through the API.
        let coerced (value : Expr) (targetType : Type) = Evaluator.tryEvaluate (Expr.Coerce (value, targetType))

        let causeOf (result : Result<objnull, TranslationError>) =
            let error = Assert.WantError (result, "A cast that fails in compiled code has no value.")

            Assert.AreEqual<TranslationErrorCode>(
                TranslationErrorCode.ClientEvaluationFailed,
                error.Code,
                "A failed cast is an exception of the code that is run."
            )

            Assert.WantValueSome (error.Cause, "The error should carry the exception.")

        Assert.AreEqual<objnull>(
            5,
            Assert.WantOk (coerced (Expr.Value 5) typeof<Nullable<int>>, "A number is a value of the nullable of its type."),
            "The value should be the number."
        )

        Assert.IsNull (
            Assert.WantOk (coerced (Expr.Value (null, typeof<string>)) typeof<obj>, "Null is a value of every reference type."),
            "The value should stay null."
        )

        Assert.IsInstanceOfType (
            causeOf (coerced (Expr.Value (box "text", typeof<obj>)) typeof<int>),
            typeof<InvalidCastException>,
            "A text is no number, whatever type the coercion names."
        )

        Assert.IsInstanceOfType (
            causeOf (coerced (Expr.Value 5) typeof<byte>),
            typeof<InvalidCastException>,
            "A cast converts nothing: a boxed integer is no byte."
        )

        Assert.IsInstanceOfType (
            causeOf (coerced (Expr.Value (null, typeof<obj>)) typeof<int>),
            typeof<NullReferenceException>,
            "Null is no value of a value type that is not a nullable."
        )

    [<TestMethod>]
    member _.``A function that is written in the quotation must be reported as having no value`` () =
        let error =
            Assert.WantError (
                Evaluator.tryEvaluate <@@ fun (number : int) -> number + 1 @@>,
                "A function cannot be computed without compiling it."
            )

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.Unsupported, error.Code, "Nothing was run, so nothing failed.")
        Assert.IsValueNone (error.Cause, "No exception is the reason.")

    [<TestMethod>]
    member _.``A variable must be reported as having no value`` () =
        let error =
            Assert.WantError (
                Evaluator.tryEvaluate (Expr.Var (Var ("number", typeof<int>))),
                "A variable has a value only inside the query."
            )

        Assert.AreEqual<TranslationErrorCode>(TranslationErrorCode.Unsupported, error.Code, "Nothing was run, so nothing failed.")
