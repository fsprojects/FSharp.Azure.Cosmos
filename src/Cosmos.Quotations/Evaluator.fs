/// <summary>
/// Computes the value of a part of a quotation that reads no variable. Such a part does not depend on the document, so
/// its value is known before the query runs and becomes a parameter of the query.
/// </summary>
/// <remarks>
/// <para>
/// The part has to be in the normal form that
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Normalizer.normalize(Microsoft.FSharp.Quotations.FSharpExpr)"/> gives:
/// bindings, pipes and calls of functions with reflected definitions are gone. What is left to compute is done by
/// reflection: reading properties and fields, calling methods and constructors, building and reading tuples, records,
/// unions and arrays, testing a type or a union case, choosing between two branches and applying a captured function.
/// </para>
/// <para>
/// An operator of FSharp.Core that refuses to be called by reflection, such as <c>-</c>, <c>/</c> and the conversions
/// between number types, is computed by the function that FSharp.Core has for operands of any types.
/// </para>
/// <para>
/// Nothing is compiled. Turning a quotation into a delegate costs hundreds of microseconds, a thousand times what
/// reflection costs for a property chain, so a part that holds a function written in the quotation has no value
/// here.
/// </para>
/// <para>
/// A choice computes only the branch that its condition chooses, as compiled code does. <c>&amp;&amp;</c> and
/// <c>||</c> are choices, so <c>text.IsSome &amp;&amp; text.Value.Length &gt; 0</c> over a captured option without a
/// value is <see langword="false"/> and raises nothing.
/// </para>
/// <para>
/// A node is computed once. The normalizer writes a value that a <see langword="let"/> bound at every place it is
/// used, as the very same node, and the code computes a bound value once: <c>let made = obj () in (made, made)</c> is
/// a pair of one object, here as in compiled code.
/// </para>
/// <para>
/// One node at two places is one value whatever put it there: one quotation that is spliced into two places, or a
/// tree that is built through the API with one node twice, is computed once as well, where compiled code would run it
/// at each place. For code without side effects the two cannot differ.
/// </para>
/// </remarks>
module internal FSharp.Azure.Cosmos.Quotations.Evaluator

open System
open System.Collections.Concurrent
open System.Collections.Frozen
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Runtime.CompilerServices
open FSharp.Quotations
open FSharp.Quotations.DerivedPatterns
open FSharp.Quotations.Patterns
open FSharp.Reflection

/// A part that has no value without compiling it or without a variable: a function, a variable, a quotation.
exception private NotComputable of part : Expr

/// The members of a type whatever their accessibility: a record or union of the caller may be internal or private.
[<Literal>]
let private AnyAccessibility = BindingFlags.Public ||| BindingFlags.NonPublic

// The reflection functions of FSharp.Core declare the values they take as not null. Null is a value they handle all
// the same: it is what a tuple, a record or a union case holds for a reference that is null, and how a union such as
// option represents its case without fields. These two functions are the one place that says so.
#nowarn "3261"

/// <summary>
/// <paramref name="values"/> as the reflection functions of FSharp.Core take them.
/// </summary>
let private asValues (values : objnull array) : obj array = values

/// <summary>
/// <paramref name="value"/> as the reflection functions of FSharp.Core take it.
/// </summary>
let private asValue (value : objnull) : obj = value

#warnon "3261"

/// <summary>
/// <paramref name="create"/> as a function that computes its result once for every key and keeps it for the process.
/// What is kept here is what reflection finds out about a type or a method, which takes microseconds to find and never
/// changes.
/// </summary>
let private memoize<'Key, 'Value when 'Key : not null> (create : 'Key -> 'Value) : 'Key -> 'Value =
    let results = ConcurrentDictionary<'Key, 'Value>()
    // The delegate is created once: a new one for every lookup would be an allocation for every lookup.
    let factory = Func<'Key, 'Value> create
    fun key -> results.GetOrAdd (key, factory)

/// The function that builds a tuple of a type from its items.
let private tupleConstructorOf =
    memoize (fun (tupleType : Type) -> FSharpValue.PreComputeTupleConstructor tupleType)

/// The function that takes a tuple of a type apart into its items, also the items beyond the seventh.
let private tupleReaderOf =
    memoize (fun (tupleType : Type) -> FSharpValue.PreComputeTupleReader tupleType)

/// The function that builds a record of a type, also of an anonymous record type, from the values of its fields.
let private recordConstructorOf =
    memoize (fun (recordType : Type) -> FSharpValue.PreComputeRecordConstructor (recordType, AnyAccessibility))

/// The function that builds a value of a union case from the values of its fields.
let private unionConstructorOf =
    memoize (fun (case : UnionCaseInfo) -> FSharpValue.PreComputeUnionConstructor (case, AnyAccessibility))

/// The function that reads the tag of a value of a union type.
let private tagReaderOf =
    memoize (fun (unionType : Type) -> FSharpValue.PreComputeUnionTagReader (unionType, AnyAccessibility))

/// <summary>
/// The method that applies a function value of a type to one argument:
/// <see cref="M:Microsoft.FSharp.Core.FSharpFunc`2.Invoke(`0)"/> of that type.
/// </summary>
let private invokerOf =
    memoize (fun (functionType : Type) ->
        functionType.GetMethod ("Invoke", [| FSharpType.GetFunctionElements functionType |> fst |])
        |> nonNull
    )

/// <summary>
/// The generic definition of the method that <paramref name="call"/> calls.
/// </summary>
let private calledDefinition (call : Expr) : MethodInfo =
    match call with
    | Call (_, method, _) -> method.GetGenericMethodDefinition ()
    | _ -> invalidArg (nameof call) $"The quotation is not a call: %A{call}"

/// <summary>
/// An operator of FSharp.Core that refuses to be called by reflection, with the function that stands in for it:
/// the one that the compiler passes to <paramref name="template"/>, a call of the operator on integers, as the
/// witness of the operator. Both as generic definitions.
/// </summary>
let private operatorWithWitness (template : Expr) : struct (MethodInfo * MethodInfo) =
    match template with
    | CallWithWitnesses (None, operator, _, [ Lambdas (_, Call (None, witness, _)) ], _) ->
        struct (operator.GetGenericMethodDefinition (), witness.GetGenericMethodDefinition ())
    | _ -> invalidArg (nameof template) $"The quotation is not a call of an operator with one witness: %A{template}"

// FS1204 says that these two functions are for dynamic invocation and not for direct use. Dynamic invocation is what
// they are taken for: the quotations only name them and are never run.
#nowarn "1204"

/// <summary>
/// The function of FSharp.Core that negates a value of any type and raises
/// <see cref="T:System.OverflowException"/> when the result does not fit.
/// </summary>
let private checkedNegation =
    calledDefinition <@ LanguagePrimitives.CheckedUnaryNegationDynamic<int, int> 1 @>

/// <summary>
/// The function of FSharp.Core that converts a value of any type to another type and raises
/// <see cref="T:System.OverflowException"/> when the value does not fit.
/// </summary>
let private checkedConversion =
    calledDefinition <@ LanguagePrimitives.CheckedExplicitDynamic<int, int> 1 @>

#warnon "1204"

/// <summary>
/// The operators of FSharp.Core that refuse to be called by reflection, by the metadata token of the method, each with
/// the function of FSharp.Core that computes it for operands whose types are known only at run time.
/// </summary>
/// <remarks>
/// <para>
/// These operators are compiled into the code that uses them, and their own methods raise
/// <see cref="T:System.NotSupportedException"/>, "Dynamic invocation of op_Subtraction is not supported". For every one
/// of them <see cref="T:Microsoft.FSharp.Core.LanguagePrimitives"/> has a function that does the same for any types:
/// for the built-in number types directly, and for another type through the operator that the type defines. The
/// compiler passes that function to the operator as its witness, which is where the pairs are taken from.
/// </para>
/// <para>
/// The checked operators are paired by hand, because the compiler passes them the unchecked functions: a checked
/// conversion that does not fit must raise, as it does in compiled code.
/// </para>
/// <para>
/// <c>+</c>, <c>*</c>, the comparisons and the mathematical functions are not here: reflection can call them.
/// <c>int</c> is here although it can be called, because its own method fails for an enumeration value.
/// </para>
/// </remarks>
let dynamicOperators : FrozenDictionary<int, MethodInfo> =
    seq {
        yield!
            seq {
                <@@ 1 - 1 @@>
                <@@ 1 / 1 @@>
                <@@ 1 % 1 @@>
                <@@ -(1) @@>
                <@@ +(1) @@>
                <@@ ~~~1 @@>
                <@@ 1 &&& 1 @@>
                <@@ 1 ||| 1 @@>
                <@@ 1 ^^^ 1 @@>
                <@@ 1 <<< 1 @@>
                <@@ 1 >>> 1 @@>
                <@@ byte 1 @@>
                <@@ sbyte 1 @@>
                <@@ int16 1 @@>
                <@@ uint16 1 @@>
                <@@ int 1L @@>
                <@@ int32 1L @@>
                <@@ uint32 1 @@>
                <@@ int64 1 @@>
                <@@ uint64 1 @@>
                <@@ nativeint 1 @@>
                <@@ unativeint 1 @@>
                <@@ float 1 @@>
                <@@ float32 1 @@>
                <@@ decimal 1 @@>
                <@@ char 1 @@>
            }
            |> Seq.map operatorWithWitness

        yield struct (calledDefinition <@@ Checked.(~-) 1 @@>, checkedNegation)

        yield!
            seq {
                <@@ Checked.byte 1 @@>
                <@@ Checked.sbyte 1 @@>
                <@@ Checked.int16 1 @@>
                <@@ Checked.uint16 1 @@>
                <@@ Checked.int 1L @@>
                <@@ Checked.int32 1L @@>
                <@@ Checked.uint32 1 @@>
                <@@ Checked.int64 1 @@>
                <@@ Checked.uint64 1 @@>
                <@@ Checked.nativeint 1 @@>
                <@@ Checked.unativeint 1 @@>
                <@@ Checked.char 1 @@>
            }
            |> Seq.map (fun template -> struct (calledDefinition template, checkedConversion))
    }
    |> _.ToFrozenDictionary((fun (struct (operator, _)) -> operator.MetadataToken), (fun (struct (_, dynamic)) -> dynamic))

/// The module of FSharp.Core, in which the metadata tokens of its operators are unique.
let private coreModule = typeof<unit>.Module

/// <summary>
/// The function that stands in for a call of one of the
/// <see cref="P:FSharp.Azure.Cosmos.Quotations.Evaluator.dynamicOperators"/> with its type arguments.
/// </summary>
[<Struct>]
type private DynamicCall = {
    /// The function of FSharp.Core, made for the types of the operands and of the result.
    Function : MethodInfo
    /// <summary>
    /// The enumeration type of the result, when the operator gives one. The functions of FSharp.Core fail for an
    /// enumeration type, so they are made for the underlying number type; reflection passes them an enumeration value
    /// as its number, and a number that comes back becomes a value of this type again.
    /// </summary>
    ResultEnumeration : Type voption
}

/// <summary>
/// <paramref name="valueType"/>, or the number type under it when it is an enumeration type.
/// </summary>
let private numberTypeOf (valueType : Type) : Type =
    if valueType.IsEnum then
        Enum.GetUnderlyingType valueType
    else
        valueType

/// <summary>
/// The members of <see cref="T:System.Nullable`1"/> as functions that take the nullable.
/// </summary>
/// <remarks>
/// Reflection has nothing to call a member of a nullable on: a nullable is boxed as its value or, when it has none, as
/// <see langword="null"/>, never as itself. As an argument it can be passed, because there <see langword="null"/> is
/// taken for the nullable without a value.
/// </remarks>
[<AbstractClass; Sealed>]
type private NullableMembers<'Value when 'Value : struct and 'Value : (new : unit -> 'Value) and 'Value :> ValueType> =

    /// <summary>
    /// <see cref="P:System.Nullable`1.HasValue"/> of <paramref name="nullable"/>.
    /// </summary>
    static member HasValue (nullable : Nullable<'Value>) : bool = nullable.HasValue

    /// <summary>
    /// <see cref="P:System.Nullable`1.Value"/> of <paramref name="nullable"/>.
    /// </summary>
    static member Value (nullable : Nullable<'Value>) : 'Value = nullable.Value

    /// <summary>
    /// <see cref="M:System.Nullable`1.GetValueOrDefault"/> of <paramref name="nullable"/>.
    /// </summary>
    static member GetValueOrDefault (nullable : Nullable<'Value>) : 'Value = nullable.GetValueOrDefault ()

    /// <summary>
    /// <see cref="M:System.Nullable`1.GetValueOrDefault(`0)"/> of <paramref name="nullable"/>.
    /// </summary>
    static member GetValueOrDefault (nullable : Nullable<'Value>, defaultValue : 'Value) : 'Value =
        nullable.GetValueOrDefault defaultValue

    /// <summary>
    /// <see cref="M:System.Nullable`1.Equals(System.Object)"/> of <paramref name="nullable"/>.
    /// </summary>
    static member Equals (nullable : Nullable<'Value>, other : objnull) : bool = nullable.Equals other

    /// <summary>
    /// <see cref="M:System.Nullable`1.GetHashCode"/> of <paramref name="nullable"/>.
    /// </summary>
    static member GetHashCode (nullable : Nullable<'Value>) : int = nullable.GetHashCode ()

    /// <summary>
    /// <see cref="M:System.Nullable`1.ToString"/> of <paramref name="nullable"/>.
    /// </summary>
    static member ToString (nullable : Nullable<'Value>) : string | null = nullable.ToString ()

/// <summary>
/// Whether <paramref name="memberOfType"/> is a member of <see cref="T:System.Nullable`1"/>.
/// </summary>
let private isOfNullable (memberOfType : MemberInfo) : bool =
    match memberOfType.DeclaringType with
    | null -> false
    | declaringType ->
        // Nearly every member is one of a class, which the first test tells.
        declaringType.IsValueType
        && declaringType.IsGenericType
        && Type.(=) (declaringType.GetGenericTypeDefinition (), typedefof<Nullable<_>>)

/// <summary>
/// The function that stands in for a method of <see cref="T:System.Nullable`1"/>, also for the getter of a property:
/// the function of the same name that takes the nullable before the arguments of the method.
/// </summary>
let private nullableStandInOf =
    memoize (fun (method : MethodInfo) ->
        let nullableType = nonNull method.DeclaringType
        // The getter of a property has the name of the property after "get_".
        let name =
            if method.IsSpecialName then
                method.Name.Substring 4
            else
                method.Name

        let parameterTypes = [|
            nullableType
            for parameter in method.GetParameters () do
                parameter.ParameterType
        |]

        typedefof<NullableMembers<int>>
            .MakeGenericType(nullableType.GetGenericArguments ())
            .GetMethod(
                name,
                BindingFlags.Static
                ||| BindingFlags.DeclaredOnly
                ||| AnyAccessibility,
                parameterTypes
            )
        |> nonNull
    )

/// <summary>
/// The function that stands in for a call of a method with its type arguments, when the method is one of the
/// <see cref="P:FSharp.Azure.Cosmos.Quotations.Evaluator.dynamicOperators"/>; no value for a method that is called
/// itself. The type arguments of the function are the types of the operands and then the type of the result, as many
/// as the function takes.
/// </summary>
let private dynamicCallOf =
    memoize (fun (method : MethodInfo) ->
        if method.Module.Equals coreModule then
            match dynamicOperators.TryGetValue method.MetadataToken with
            | true, dynamic ->
                let types = [|
                    for parameter in method.GetParameters () do
                        numberTypeOf parameter.ParameterType
                    numberTypeOf method.ReturnType
                |]

                ValueSome {
                    Function = dynamic.MakeGenericMethod (types |> Array.take (dynamic.GetGenericArguments().Length))
                    ResultEnumeration =
                        if method.ReturnType.IsEnum then
                            ValueSome method.ReturnType
                        else
                            ValueNone
                }
            | _ -> ValueNone
        else
            ValueNone
    )

/// <summary>
/// <paramref name="value"/> as a value of <paramref name="targetType"/>, as a cast gives it: the value itself when it
/// is of that type, and the exception of a failed cast when it is not.
/// </summary>
/// <remarks>
/// The compiler writes a coercion only where the cast cannot fail, for <c>:&gt;</c> and for an argument that is passed
/// as a base type. A quotation that is built through the API can hold one that fails, and without this check its
/// value would be of another type than the part it is the value of. A cast converts nothing: a boxed integer is no
/// byte, in compiled code either.
/// </remarks>
let private castTo (targetType : Type) (value : objnull) : objnull =
    match value with
    | null ->
        // Null is a value of every reference type and of a nullable; unboxing it as any other value type fails.
        match targetType.IsValueType, Nullable.GetUnderlyingType targetType with
        | true, null -> raise (NullReferenceException ())
        | _ -> null
    | value when targetType.IsInstanceOfType value -> value
    | value ->
        raise (
            InvalidCastException
                $"Unable to cast object of type '%s{value.GetType().FullName}' to type '%s{targetType.FullName}'."
        )

/// <summary>
/// The values of the nodes of quotations that have been computed, by the identity of the node. The normalizer writes a
/// value that a <see langword="let"/> bound at every place it is used, as the very same node; with the values kept
/// here such a node is computed at the first place that needs its value and never again, as the code computes a
/// bound value once.
/// </summary>
/// <remarks>
/// One of these serves one part, or all the parts of one quotation for a caller that computes several of them: see
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.tryEvaluateOnce(FSharp.Azure.Cosmos.Quotations.Evaluator.Computed,Microsoft.FSharp.Quotations.FSharpExpr)"/>.
/// </remarks>
[<Sealed>]
type Computed () =

    // By identity, because a quotation compares and hashes itself by walking all of itself. Made when the first value
    // is kept: a part that is a captured value keeps none.
    let mutable values : Dictionary<obj, objnull> | null = null

    /// <summary>
    /// The value of <paramref name="node"/>, when it has been computed.
    /// </summary>
    member _.TryFind (node : Expr) : objnull voption =
        match values with
        | null -> ValueNone
        | values ->
            match values.TryGetValue node with
            | true, value -> ValueSome value
            | _ -> ValueNone

    /// <summary>
    /// Remembers that <paramref name="node"/> has <paramref name="value"/>.
    /// </summary>
    member _.Add (node : Expr, value : objnull) : unit =
        match values with
        | null ->
            let created = Dictionary<obj, objnull> ReferenceEqualityComparer.Instance
            created.Add (node, value)
            values <- created
        | values -> values.Add (node, value)

/// <summary>
/// <paramref name="target"/>, the value that a member is called on or that is applied as a function. When it is null,
/// this raises what compiled code raises for it, and not what reflection raises for a call on nothing.
/// </summary>
/// <remarks>
/// Compiled code computes the arguments of a call before it finds that the receiver is null, so this is asked after
/// the arguments are computed: when one of them raises, its exception is the one that the code raises.
/// </remarks>
let private called (target : objnull) : obj =
    match target with
    | null -> raise (NullReferenceException ())
    | target -> target

/// <summary>
/// <paramref name="target"/> as a member takes its receiver: the value for a member of a value, checked with
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.called(System.Object)"/>, and <see langword="null"/> for a
/// static member.
/// </summary>
let private calledOn (receiver : Expr option) (target : objnull) : objnull =
    match receiver with
    | None -> null
    | Some _ -> called target

/// <summary>
/// The value of <paramref name="expr"/>. Exceptions of the code it runs come out as they are raised, mostly wrapped
/// in a <see cref="T:System.Reflection.TargetInvocationException"/>.
/// </summary>
/// <param name="computed">
/// What has been computed so far: the value of a node that is in it is taken from it, and the value of every other
/// node that runs code is added to it.
/// </param>
/// <param name="expr">The part to compute.</param>
let rec private valueOf (computed : Computed) (expr : Expr) : objnull =
    match expr with
    | Value (value, _) -> value
    | _ ->
        match computed.TryFind expr with
        | ValueSome value -> value
        | ValueNone ->
            let value = computedValueOf computed expr
            computed.Add (expr, value)
            value

/// <summary>
/// The value of a node that is no value written in the quotation: what its code gives now.
/// </summary>
and private computedValueOf (computed : Computed) (expr : Expr) : objnull =
    match expr with
    | PropertyGet (receiver, property, arguments) ->
        match receiver with
        | Some nullable when isOfNullable property -> nullableMember computed (nonNull property.GetMethod) nullable arguments
        | _ ->
            let target = receiverOf computed receiver
            let values = valuesOf computed arguments
            property.GetValue (calledOn receiver target, values)
    | FieldGet (receiver, field) -> field.GetValue (calledOn receiver (receiverOf computed receiver))
    | Call (None, method, arguments) ->
        match dynamicCallOf method with
        | ValueSome dynamic ->
            match dynamic.ResultEnumeration, dynamic.Function.Invoke (null, valuesOf computed arguments) with
            | ValueSome enumeration, (NonNull number) -> Enum.ToObject (enumeration, number)
            | _, result -> result
        | ValueNone -> method.Invoke (null, valuesOf computed arguments)
    | Call (receiver, method, arguments) ->
        match receiver with
        | Some nullable when isOfNullable method -> nullableMember computed method nullable arguments
        | _ ->
            let target = receiverOf computed receiver
            let values = valuesOf computed arguments
            method.Invoke (calledOn receiver target, values)
    | IfThenElse (condition, whenTrue, whenFalse) ->
        if valueOf computed condition :?> bool then
            valueOf computed whenTrue
        else
            valueOf computed whenFalse
    | NewObject (constructor, arguments) -> constructor.Invoke (valuesOf computed arguments)
    | NewTuple items -> tupleConstructorOf expr.Type (asValues (valuesOf computed items))
    | NewRecord (recordType, items) -> recordConstructorOf recordType (asValues (valuesOf computed items))
    | NewUnionCase (case, items) -> unionConstructorOf case (asValues (valuesOf computed items))
    | NewArray (elementType, items) ->
        let array = Array.CreateInstance (elementType, items.Length)
        items
        |> List.iteri (fun index item -> array.SetValue (valueOf computed item, index))
        array
    | TupleGet (tuple, index) -> (tupleReaderOf tuple.Type (asValue (valueOf computed tuple)))[index]
    | UnionCaseTest (union, case) -> tagReaderOf case.DeclaringType (asValue (valueOf computed union)) = case.Tag
    | TypeTest (value, tested) -> tested.IsInstanceOfType (valueOf computed value)
    | Coerce (value, targetType) -> castTo targetType (valueOf computed value)
    | DefaultValue valueType ->
        // The zeroed value of a struct, for which no constructor runs: a struct of another language can declare a
        // constructor without parameters, and the default value is not what that constructor makes. A nullable
        // without a value is null as an object, like the default value of a class.
        match Nullable.GetUnderlyingType valueType with
        | null when valueType.IsValueType -> RuntimeHelpers.GetUninitializedObject valueType
        | _ -> null
    | Application (func, argument) ->
        let target = valueOf computed func
        let value = valueOf computed argument
        (invokerOf func.Type).Invoke(called target, [| value |])
    | _ -> raise (NotComputable expr)

/// <summary>
/// The value of the receiver of a member, <see langword="null"/> for a static member and for a receiver that is null.
/// </summary>
and private receiverOf (computed : Computed) (receiver : Expr option) : objnull =
    match receiver with
    | None -> null
    | Some receiver -> valueOf computed receiver

/// <summary>
/// The value of a member of a nullable. The nullable is passed to the function that stands in for the member, so a
/// nullable without a value is one, and not a receiver that is null.
/// </summary>
and private nullableMember (computed : Computed) (method : MethodInfo) (nullable : Expr) (arguments : Expr list) : objnull =
    (nullableStandInOf method).Invoke(null, [| valueOf computed nullable; yield! valuesOf computed arguments |])

/// The values of the arguments of a member, in their order.
and private valuesOf (computed : Computed) (arguments : Expr list) : objnull array =
    arguments |> List.map (valueOf computed) |> List.toArray

/// <summary>
/// The parts of quotations that
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.canEvaluate(FSharp.Azure.Cosmos.Quotations.Evaluator.Refused,Microsoft.FSharp.Quotations.FSharpExpr)"/>
/// has refused, by their identity. A caller that asks about a part and
/// then about the parts under it keeps one of these for all its questions: to refuse a part, every part on the way
/// down to the reason was looked at, and is not looked at again.
/// </summary>
[<Sealed>]
type Refused () =

    // By identity, because a quotation compares and hashes itself by walking all of itself.
    let parts = HashSet<obj> ReferenceEqualityComparer.Instance

    /// <summary>
    /// Whether <paramref name="part"/> has been refused.
    /// </summary>
    member _.Holds (part : Expr) : bool = parts.Contains part

    /// <summary>
    /// Remembers that <paramref name="part"/> is refused.
    /// </summary>
    member _.Add (part : Expr) : unit = parts.Add part |> ignore

/// <summary>
/// Whether <paramref name="expr"/> is made only of what
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.tryEvaluate(Microsoft.FSharp.Quotations.FSharpExpr)"/>
/// computes. Such a part reads no variable and holds no function that is written in the quotation, so it has a value
/// before the query runs, unless the code in it raises an exception.
/// </summary>
/// <remarks>
/// The answer is given without running any code of the part: a caller asks it to choose the parts to compute, and a
/// part that turned out not to be computable half way would have run a half of its code for nothing.
/// </remarks>
/// <param name="refused">
/// The parts refused so far. A part that is refused now is added, with every part between it and the reason.
/// </param>
/// <param name="expr">The part of a quotation in normal form.</param>
let rec canEvaluate (refused : Refused) (expr : Expr) : bool =
    not (refused.Holds expr)
    && (let can =
            match expr with
            | Var _ -> false
            | Value _
            | DefaultValue _ -> true
            | PropertyGet (receiver, _, arguments)
            | Call (receiver, _, arguments) ->
                receiver |> Option.forall (canEvaluate refused)
                && arguments |> List.forall (canEvaluate refused)
            | IfThenElse (condition, whenTrue, whenFalse) ->
                canEvaluate refused condition
                && canEvaluate refused whenTrue
                && canEvaluate refused whenFalse
            | FieldGet (receiver, _) -> receiver |> Option.forall (canEvaluate refused)
            | NewObject (_, items)
            | NewRecord (_, items)
            | NewUnionCase (_, items)
            | NewArray (_, items)
            | NewTuple items -> items |> List.forall (canEvaluate refused)
            | TupleGet (value, _)
            | UnionCaseTest (value, _)
            | TypeTest (value, _)
            | Coerce (value, _) -> canEvaluate refused value
            | Application (func, argument) -> canEvaluate refused func && canEvaluate refused argument
            | _ -> false

        if not can then
            refused.Add expr

        can)

/// <summary>
/// The error for a part that raised <paramref name="cause"/> while it was computed.
/// </summary>
let private failed (part : Expr) (cause : exn) : TranslationError =
    part
    |> TranslationError.ofException
        TranslationErrorCode.ClientEvaluationFailed
        $"A part of the quotation that reads nothing of the document has to be computed before the query runs, and computing it raised %s{cause.GetType().Name}: %s{cause.Message}"
        (ImmutableArray.Create<string>
            "Compute the value before the quotation, where the exception can be handled, and use the result in the quotation.")
        cause

/// <summary>
/// The error for a part that has no value without compiling it.
/// </summary>
let private notComputable (part : Expr) : TranslationError =
    part
    |> TranslationError.ofQuotation
        TranslationErrorCode.Unsupported
        "A part of the quotation that reads nothing of the document has to be computed before the query runs, but it holds a function, a variable or another quotation, which cannot be computed without compiling the quotation."
        (ImmutableArray.Create<string> "Compute the value before the quotation and use the result in the quotation.")

/// <summary>
/// The value of <paramref name="expr"/> or the error that says why it has none, with <paramref name="computed"/> for
/// the nodes that were computed before.
/// </summary>
let private evaluate (computed : Computed) (expr : Expr) : Result<objnull, TranslationError> =
    try
        Ok (valueOf computed expr)
    with
    | NotComputable part -> Error (notComputable part)
    | :? TargetInvocationException as invocation ->
        match invocation.InnerException with
        | null -> Error (failed expr invocation)
        | cause -> Error (failed expr cause)
    | cause -> Error (failed expr cause)

/// <summary>
/// The value of <paramref name="expr"/>, a part of a quotation in normal form that reads no variable, or the error
/// that says why it has none. A node that stands at several places of the part is computed once.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The code of the part raised an exception:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.ClientEvaluationFailed"/> with the exception as
/// its <see cref="P:FSharp.Azure.Cosmos.Quotations.TranslationError.Cause"/>. Any exception is taken, because the code
/// is the caller's and may raise any.</description></item>
/// <item><description>The part holds a function, a variable or another quotation:
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.Unsupported"/>.</description></item>
/// </list>
/// </remarks>
let tryEvaluate (expr : Expr) : Result<objnull, TranslationError> = evaluate (Computed ()) expr

/// <summary>
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.tryEvaluate(Microsoft.FSharp.Quotations.FSharpExpr)"/> for a
/// caller that computes several parts of one quotation: a node that was computed for an earlier part is not computed
/// again for this one.
/// </summary>
/// <param name="computed">What has been computed so far; what is computed now is added.</param>
/// <param name="expr">A part of a quotation in normal form that reads no variable.</param>
let tryEvaluateOnce (computed : Computed) (expr : Expr) : Result<objnull, TranslationError> = evaluate computed expr
