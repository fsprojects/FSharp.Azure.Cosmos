/// <summary>
/// Brings a quotation into its normal form, the one form that every spelling of the same condition has. Pipes,
/// composition, <see langword="let"/> bindings, calls of functions marked with
/// <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> and splices of other quotations are only ways of
/// writing; after normalization they are gone, and what is left says what is computed from the document.
/// </summary>
/// <remarks>
/// <para>
/// A quotation is in normal form when none of these is left in it:
/// </para>
/// <list type="bullet">
/// <item><description>a call of a pipe or composition operator of FSharp.Core;</description></item>
/// <item><description>a function written in place and applied in place, or a <see langword="let"/> whose variable
/// is never assigned to;</description></item>
/// <item><description>a call of a function, method or property getter that has a reflected definition and can be
/// replaced by it. Four kinds of them stay: a call of a member that a derived class can override, the reading of a
/// module-level value, a call that does not pass the arguments its definition takes, which only a quotation built
/// through the API can hold, and a call of a function that calls itself, directly or through the functions it calls,
/// because writing its definition in its place would never end;</description></item>
/// <item><description>a call that passes witnesses, which the compiler writes for an operator with a member
/// constraint such as <c>+</c>;</description></item>
/// <item><description>the address of a value as the receiver of a property, which the compiler writes for a struct
/// anonymous record;</description></item>
/// <item><description>the reading of a part of a tuple, record or union case that is built in the same
/// place;</description></item>
/// <item><description>a condition that is a constant, a comparison of a boolean with a constant, or a double
/// negation.</description></item>
/// </list>
/// <para>
/// What a query cannot express even then is an error: see
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Normalizer.normalize(Microsoft.FSharp.Quotations.FSharpExpr)"/>.
/// </para>
/// <para>
/// A value bound by <see langword="let"/> is written at every place it is used, and so is the argument of a function
/// that is written and applied in place. Every such place holds the very same node, also after what is rewritten
/// around the node and inside it later, so a stage that computes a value outside the query can tell that the places
/// are one value and computes it once, as the code does.
/// </para>
/// <para>
/// A node that stands at several places of the quotation as it comes stays one node in the same way: a quotation that
/// is spliced into two places is the same nodes at both, and code can build a tree with one node twice.
/// </para>
/// </remarks>
module internal FSharp.Azure.Cosmos.Quotations.Normalizer

open System
open System.Collections.Concurrent
open System.Collections.Frozen
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open System.Runtime.CompilerServices
open FSharp.Quotations
open FSharp.Quotations.ExprShape
open FSharp.Quotations.Patterns
open FSharp.Reflection

open FSharp.Azure.Cosmos

/// The functions of FSharp.Core that normalization rewrites.
[<RequireQualifiedAccess; Struct>]
type private KnownFunction =
    /// The forward pipe of one value.
    | PipeRight
    /// The forward pipe of two values.
    | PipeRight2
    /// The forward pipe of three values.
    | PipeRight3
    /// The backward pipe of one value.
    | PipeLeft
    /// The backward pipe of two values.
    | PipeLeft2
    /// The backward pipe of three values.
    | PipeLeft3
    /// The forward composition of two functions.
    | ComposeRight
    /// The backward composition of two functions.
    | ComposeLeft
    /// The negation of a boolean.
    | Not
    /// The equality operator.
    | Equality
    /// The inequality operator.
    | Inequality
    /// The first item of a pair.
    | Fst
    /// The second item of a pair.
    | Snd

/// <summary>
/// The largest number of rewriting steps one normalization takes before it gives up with
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.NormalizationDiverged"/>. A step removes one pipe,
/// binding, call or projection, so a quotation written by hand needs a few dozen.
/// </summary>
[<Literal>]
let private MaxRewrites = 10_000

/// The name of a variable that normalization introduces; the compiler names its own generated variables this way.
[<Literal>]
let private GeneratedVariableName = "_arg1"

/// <summary>
/// The method that <paramref name="call"/> calls.
/// </summary>
let private calledMethod (call : Expr) : MethodInfo =
    match call with
    | Call (_, method, _) -> method
    | _ -> invalidArg (nameof call) $"The quotation is not a call: %A{call}"

/// <summary>
/// The negation of FSharp.Core, <see cref="M:Microsoft.FSharp.Core.Operators.Not(System.Boolean)"/>.
/// </summary>
let private notMethod = calledMethod <@ not true @>

/// <summary>
/// The module of FSharp.Core that declares every
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Normalizer.KnownFunction"/>:
/// <see cref="T:Microsoft.FSharp.Core.Operators"/>.
/// </summary>
let private operatorsModule = notMethod.DeclaringType

/// <summary>
/// The functions of FSharp.Core that normalization rewrites, by the metadata token of the method, which every
/// instantiation of a generic function shares. The methods are taken from quotations that call them, never from names,
/// so another function with the name of an operator is not mistaken for it.
/// </summary>
let private knownFunctions : FrozenDictionary<int, KnownFunction> =
    seq {
        struct (calledMethod <@ true |> not @>, KnownFunction.PipeRight)
        struct (calledMethod <@ (true, true) ||> (&&) @>, KnownFunction.PipeRight2)
        struct (calledMethod
                    <@
                        (true, true, true)
                        |||> (fun first second third -> first && second && third)
                    @>,
                KnownFunction.PipeRight3)
        struct (calledMethod <@ not <| true @>, KnownFunction.PipeLeft)
        struct (calledMethod <@ (&&) <|| (true, true) @>, KnownFunction.PipeLeft2)
        struct (calledMethod
                    <@
                        (fun first second third -> first && second && third)
                        <||| (true, true, true)
                    @>,
                KnownFunction.PipeLeft3)
        struct (calledMethod <@ not >> not @>, KnownFunction.ComposeRight)
        struct (calledMethod <@ not << not @>, KnownFunction.ComposeLeft)
        struct (notMethod, KnownFunction.Not)
        struct (calledMethod <@ true = true @>, KnownFunction.Equality)
        struct (calledMethod <@ true <> true @>, KnownFunction.Inequality)
        struct (calledMethod <@ fst (true, true) @>, KnownFunction.Fst)
        struct (calledMethod <@ snd (true, true) @>, KnownFunction.Snd)
    }
    |> _.ToFrozenDictionary((fun (struct (method, _)) -> method.MetadataToken), (fun (struct (_, known)) -> known))

/// <summary>
/// Which <see cref="T:FSharp.Azure.Cosmos.Quotations.Normalizer.KnownFunction"/> <paramref name="method"/> is, when it
/// is one.
/// </summary>
let private knownFunctionOf (method : MethodInfo) : KnownFunction voption =
    if Type.(=) (method.DeclaringType, operatorsModule) then
        match knownFunctions.TryGetValue method.MetadataToken with
        | true, known -> ValueSome known
        | _ -> ValueNone
    else
        ValueNone

/// <summary>
/// A call of one of the <see cref="T:FSharp.Azure.Cosmos.Quotations.Normalizer.KnownFunction"/>s, with its arguments.
/// </summary>
[<return : Struct>]
let private (|KnownCall|_|) (expr : Expr) : struct (KnownFunction * Expr list) voption =
    match expr with
    | Call (None, method, arguments) ->
        match knownFunctionOf method with
        | ValueSome known -> ValueSome struct (known, arguments)
        | ValueNone -> ValueNone
    | _ -> ValueNone

/// <summary>
/// A boolean that is written in the quotation or left there by a rewrite. A captured boolean is not one: it is a
/// parameter of the query, and the text of a query never depends on the value of a parameter.
/// </summary>
/// <remarks>
/// The default value of the type is <see langword="false"/> written another way: the compiler writes it for a
/// boolean that is made with its constructor, <c>bool ()</c>, and a quotation that is built by hand may hold it. Left
/// as a condition, it would keep both branches in the quotation as code that runs.
/// </remarks>
[<return : Struct>]
let private (|Constant|_|) (expr : Expr) : bool voption =
    match expr with
    | ValueWithName _ -> ValueNone
    | Value (:? bool as value, _) -> ValueSome value
    | DefaultValue valueType when Type.(=) (valueType, typeof<bool>) -> ValueSome false
    | _ -> ValueNone

/// <summary>
/// The reflected definitions that were looked up, by method; no value for a method that has none. FSharp.Core searches
/// the resources of the declaring assembly on every lookup of a method without a definition, which takes microseconds,
/// and a predicate asks for every call in it, operators included. A reflected definition is a fact of the assembly,
/// so the answers are kept for the process.
/// </summary>
let private reflectedDefinitions = ConcurrentDictionary<MethodBase, Expr voption>()

/// <summary>
/// Looks the reflected definition of a method up in FSharp.Core; a delegate that is created once, for
/// <see cref="M:System.Collections.Concurrent.ConcurrentDictionary`2.GetOrAdd(`0,System.Func{`0,`1})"/>.
/// </summary>
let private lookUpReflectedDefinition =
    Func<MethodBase, Expr voption>(fun method ->
        Expr.TryGetReflectedDefinition method
        |> ValueOption.ofOption
    )

/// <summary>
/// The reflected definition of <paramref name="method"/>, when it has one.
/// </summary>
let private reflectedDefinitionOf (method : MethodBase) : Expr voption =
    reflectedDefinitions.GetOrAdd (method, lookUpReflectedDefinition)

/// Whether two methods are one method of one type, whatever types its generic parameters stand for.
let private isSameDefinition (first : MethodBase) (second : MethodBase) : bool =
    first.MetadataToken = second.MetadataToken
    && first.Module.Equals second.Module

/// <summary>
/// Whether <paramref name="method"/> is one of <paramref name="methods"/>, whatever types its generic parameters
/// stand for. A loop over the indexes, because the check of what is left asks for every call of a quotation, and the
/// list is empty nearly always.
/// </summary>
let private isAmong (methods : IReadOnlyList<MethodBase>) (method : MethodBase) : bool =
    let mutable found = false

    for index in 0 .. methods.Count - 1 do
        found <- found || isSameDefinition methods[index] method

    found

/// Whether two lists hold the very same nodes, which means that a rewrite of the first changed nothing.
let rec private areSame (first : Expr list) (second : Expr list) : bool =
    match first, second with
    | [], [] -> true
    | firstHead :: firstTail, secondHead :: secondTail ->
        obj.ReferenceEquals (firstHead, secondHead)
        && areSame firstTail secondTail
    | _ -> false

/// <summary>
/// What is known about the body of a binding before the bound value is written into it.
/// </summary>
[<Struct>]
type private Survey = {
    /// How many places read the variable.
    Uses : int
    /// <summary>
    /// Whether the value cannot simply be written at those places: the body binds the variable again, it binds a free
    /// variable of the value, or it holds a node that stands at several places and has to stay one node.
    /// </summary>
    NeedsCare : bool
}

/// <summary>
/// Looks at <paramref name="expr"/>, the body of a binding of <paramref name="variable"/>, before a value with the
/// free variables <paramref name="freeInValue"/> is written into it.
/// </summary>
/// <remarks>
/// The case of a function sees every binding, whatever construct makes it. A quotation keeps the variable of a
/// <see langword="let"/>, of a recursive function, of a loop, of an exception handler and of a delegate in a function
/// among the parts of that node, and those parts are what the shape of the node gives.
/// </remarks>
/// <param name="variable">The variable that the value is bound to.</param>
/// <param name="freeInValue">The free variables of the value.</param>
/// <param name="shared">The nodes that stand at several places, by their identity.</param>
/// <param name="expr">The body of the binding, or a part of it.</param>
let rec private survey (variable : Var) (freeInValue : HashSet<Var>) (shared : HashSet<obj>) (expr : Expr) : Survey =
    match expr with
    | ShapeVar candidate -> {
        Uses = (if obj.ReferenceEquals (candidate, variable) then 1 else 0)
        NeedsCare = false
      }
    | ShapeLambda (bound, body) ->
        let inside = survey variable freeInValue shared body

        {
            inside with
                NeedsCare =
                    inside.NeedsCare
                    || obj.ReferenceEquals (bound, variable)
                    || freeInValue.Contains bound
                    || (shared.Count > 0 && shared.Contains expr)
        }
    | ShapeCombination (_, parts) ->
        let mutable uses = 0
        let mutable needsCare = shared.Count > 0 && shared.Contains expr

        for part in parts do
            let inside = survey variable freeInValue shared part
            uses <- uses + inside.Uses
            needsCare <- needsCare || inside.NeedsCare

        { Uses = uses; NeedsCare = needsCare }

/// <summary>
/// Replaces the free occurrences of <paramref name="variable"/> in <paramref name="expr"/> with
/// <paramref name="value"/>, with care for the bindings inside <paramref name="expr"/> and for the nodes in it that
/// stand at several places.
/// </summary>
/// <remarks>
/// <para>
/// One variable can be bound at several places of a quotation: a quotation that is spliced twice arrives as the same
/// nodes each time, and every call of a function with a reflected definition gets nodes of its own that bind the
/// variables of the one definition. So an inner binding of
/// <paramref name="variable"/> hides it and is left alone, and a binding that would capture a free variable of
/// <paramref name="value"/> is renamed first.
/// </para>
/// <para>
/// A node without the variable is returned as it is, so a node that stands at several places stays one node. When such
/// a node holds the variable, it is built anew at the first of its places, and <paramref name="replaced"/> gives the
/// other places the same new node.
/// </para>
/// </remarks>
/// <param name="variable">The variable to replace.</param>
/// <param name="value">What takes its place.</param>
/// <param name="freeInValue">The free variables of <paramref name="value"/>.</param>
/// <param name="shared">
/// The nodes that stand at several places, by their identity. A node that takes the place of one of them is added.
/// </param>
/// <param name="replaced">
/// What this replacing has made of the nodes that stand at several places so far, by their identity.
/// </param>
/// <param name="expr">The quotation to replace it in.</param>
let rec private substituteWithCare
    (variable : Var)
    (value : Expr)
    (freeInValue : HashSet<Var>)
    (shared : HashSet<obj>)
    (replaced : Dictionary<obj, Expr>)
    (expr : Expr)
    : Expr =
    if shared.Count > 0 && shared.Contains expr then
        match replaced.TryGetValue expr with
        | true, known -> known
        | _ ->
            let replacement = substituteInParts variable value freeInValue shared replaced expr
            replaced.Add (expr, replacement)
            shared.Add replacement |> ignore
            replacement
    else
        substituteInParts variable value freeInValue shared replaced expr

/// <summary>
/// What
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Normalizer.substituteWithCare(Microsoft.FSharp.Quotations.FSharpVar,Microsoft.FSharp.Quotations.FSharpExpr,System.Collections.Generic.HashSet{Microsoft.FSharp.Quotations.FSharpVar},System.Collections.Generic.HashSet{System.Object},System.Collections.Generic.Dictionary{System.Object,Microsoft.FSharp.Quotations.FSharpExpr},Microsoft.FSharp.Quotations.FSharpExpr)"/>
/// makes of a node that it has not replaced before.
/// </summary>
and private substituteInParts
    (variable : Var)
    (value : Expr)
    (freeInValue : HashSet<Var>)
    (shared : HashSet<obj>)
    (replaced : Dictionary<obj, Expr>)
    (expr : Expr)
    : Expr =
    match expr with
    | ShapeVar candidate ->
        if obj.ReferenceEquals (candidate, variable) then
            value
        else
            expr
    | ShapeLambda (bound, _) when obj.ReferenceEquals (bound, variable) -> expr
    | ShapeLambda (bound, body) when freeInValue.Contains bound ->
        let renamed = Var (bound.Name, bound.Type, bound.IsMutable)

        // The renaming is a replacing of its own, with its own account of what it made of which node.
        let renamedBody =
            substituteWithCare
                bound
                (Expr.Var renamed)
                (HashSet [ renamed ])
                shared
                (Dictionary ReferenceEqualityComparer.Instance)
                body

        Expr.Lambda (renamed, substituteWithCare variable value freeInValue shared replaced renamedBody)
    | ShapeLambda (bound, body) ->
        let newBody = substituteWithCare variable value freeInValue shared replaced body
        if obj.ReferenceEquals (newBody, body) then
            expr
        else
            Expr.Lambda (bound, newBody)
    | ShapeCombination (shape, parts) ->
        let newParts =
            parts
            |> List.map (substituteWithCare variable value freeInValue shared replaced)
        if areSame parts newParts then
            expr
        else
            RebuildShapeCombination (shape, newParts)

/// <summary>
/// <paramref name="body"/> with <paramref name="value"/> at every place where <paramref name="variable"/> is used.
/// </summary>
/// <remarks>
/// <para>
/// A value that is written at more than one place is the same node at all of them, and it is added to
/// <paramref name="shared"/>, the nodes that have to stay one node from then on. The code computes such a value once,
/// and a later stage tells by the node that the places are one value.
/// </para>
/// <para>
/// When nothing inside <paramref name="body"/> needs care, which is nearly always, the replacing is left to
/// <see cref="M:Microsoft.FSharp.Quotations.FSharpExpr.Substitute(Microsoft.FSharp.Core.FSharpFunc{Microsoft.FSharp.Quotations.FSharpVar,Microsoft.FSharp.Core.FSharpOption{Microsoft.FSharp.Quotations.FSharpExpr}})"/>.
/// It builds its nodes without validating them, while
/// <see cref="M:Microsoft.FSharp.Quotations.ExprShapeModule.RebuildShapeCombination(System.Object,Microsoft.FSharp.Collections.FSharpList{Microsoft.FSharp.Quotations.FSharpExpr})"/>
/// asks reflection whether the declaring type of every member it rebuilds an access to is a union, which takes
/// microseconds for each access.
/// </para>
/// <para>
/// Otherwise
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Normalizer.substituteWithCare(Microsoft.FSharp.Quotations.FSharpVar,Microsoft.FSharp.Quotations.FSharpExpr,System.Collections.Generic.HashSet{Microsoft.FSharp.Quotations.FSharpVar},System.Collections.Generic.HashSet{System.Object},System.Collections.Generic.Dictionary{System.Object,Microsoft.FSharp.Quotations.FSharpExpr},Microsoft.FSharp.Quotations.FSharpExpr)"/>
/// does it. The function of FSharp.Core replaces a variable inside an inner binding of the same variable too, and it
/// builds every node of the body anew, so a node that stands at two places would come out as two nodes.
/// </para>
/// </remarks>
let private bind (shared : HashSet<obj>) (variable : Var) (value : Expr) (body : Expr) : Expr =
    let freeInValue = HashSet<Var>(value.GetFreeVars ())
    let found = survey variable freeInValue shared body

    if found.Uses = 0 then
        body
    else
        // A variable is no node to keep: every place that reads it is a node of its own already.
        match value with
        | Var _ -> ()
        | _ when found.Uses > 1 -> shared.Add value |> ignore
        | _ -> ()

        if found.NeedsCare then
            substituteWithCare variable value freeInValue shared (Dictionary ReferenceEqualityComparer.Instance) body
        else
            body.Substitute (fun candidate ->
                if obj.ReferenceEquals (candidate, variable) then
                    Some value
                else
                    None
            )

/// <summary>
/// Whether <paramref name="expr"/> assigns to <paramref name="variable"/> or takes its address, through which it can
/// be assigned to.
/// </summary>
/// <remarks>
/// A binding of the same variable inside <paramref name="expr"/> hides it: what is assigned to under that binding is
/// the variable of that binding. One variable is bound at two nested places in a quotation that is built through the
/// API or spliced into itself.
/// </remarks>
let rec private isChanged (variable : Var) (expr : Expr) : bool =
    match expr with
    | VarSet (target, _) when obj.ReferenceEquals (target, variable) -> true
    | AddressOf (Var target) when obj.ReferenceEquals (target, variable) -> true
    | ShapeVar _ -> false
    | ShapeLambda (bound, _) when obj.ReferenceEquals (bound, variable) -> false
    | ShapeLambda (_, body) -> isChanged variable body
    | ShapeCombination (_, parts) -> parts |> List.exists (isChanged variable)

/// <summary>
/// The function that applies <paramref name="first"/> and then <paramref name="second"/>, written as a function of
/// one parameter. The parameter takes the name of the parameter of <paramref name="first"/> when that is written as a
/// function, so that a name the user chose survives.
/// </summary>
let private compose (first : Expr) (second : Expr) : Expr =
    let name =
        match first with
        | Lambda (parameter, _) -> parameter.Name
        | _ -> GeneratedVariableName

    let parameter = Var (name, FSharpType.GetFunctionElements first.Type |> fst)
    Expr.Lambda (parameter, Expr.Application (second, Expr.Application (first, Expr.Var parameter)))

/// <summary>
/// Pipes and composition: <c>x |&gt; f</c> and <c>f &lt;| x</c> are <c>f x</c>, the pipes of two and three values
/// apply the function to the values in turn, and <c>f &gt;&gt; g</c> and <c>g &lt;&lt; f</c> are
/// <c>fun x -&gt; g (f x)</c>.
/// </summary>
let private eliminatePipeOrComposition (known : KnownFunction) (arguments : Expr list) : Expr voption =
    match known, arguments with
    | KnownFunction.PipeRight, [ argument; func ]
    | KnownFunction.PipeLeft, [ func; argument ] -> ValueSome (Expr.Application (func, argument))
    | KnownFunction.PipeRight2, [ first; second; func ]
    | KnownFunction.PipeLeft2, [ func; first; second ] -> ValueSome (Expr.Applications (func, [ [ first ]; [ second ] ]))
    | KnownFunction.PipeRight3, [ first; second; third; func ]
    | KnownFunction.PipeLeft3, [ func; first; second; third ] ->
        ValueSome (Expr.Applications (func, [ [ first ]; [ second ]; [ third ] ]))
    | KnownFunction.ComposeRight, [ first; second ]
    | KnownFunction.ComposeLeft, [ second; first ] -> ValueSome (compose first second)
    | _ -> ValueNone

/// <summary>
/// <see cref="M:Microsoft.FSharp.Core.Operators.Fst``2(System.Tuple{``0,``1})"/> and
/// <see cref="M:Microsoft.FSharp.Core.Operators.Snd``2(System.Tuple{``0,``1})"/> are the reading of an item of a
/// tuple, the one form the other rules and the later stages know.
/// </summary>
let private eliminatePairFunction (known : KnownFunction) (arguments : Expr list) : Expr voption =
    match known, arguments with
    | KnownFunction.Fst, [ pair ] -> ValueSome (Expr.TupleGet (pair, 0))
    | KnownFunction.Snd, [ pair ] -> ValueSome (Expr.TupleGet (pair, 1))
    | _ -> ValueNone

/// <summary>
/// The negation of <paramref name="condition"/>.
/// </summary>
let private negate (condition : Expr) : Expr = Expr.Call (notMethod, [ condition ])

/// <summary>
/// Booleans in one form, the calls: a boolean compared with a constant is itself or its negation, the negation of a
/// constant is a constant, and a double negation is gone.
/// </summary>
let private canonicalBooleanCall (known : KnownFunction) (arguments : Expr list) : Expr voption =
    match known, arguments with
    | KnownFunction.Not, [ Constant value ] -> ValueSome (Expr.Value (not value))
    | KnownFunction.Not, [ KnownCall (KnownFunction.Not, [ condition ]) ] -> ValueSome condition
    | KnownFunction.Equality, [ condition; Constant true ]
    | KnownFunction.Equality, [ Constant true; condition ]
    | KnownFunction.Inequality, [ condition; Constant false ]
    | KnownFunction.Inequality, [ Constant false; condition ] -> ValueSome condition
    | KnownFunction.Equality, [ condition; Constant false ]
    | KnownFunction.Equality, [ Constant false; condition ]
    | KnownFunction.Inequality, [ condition; Constant true ]
    | KnownFunction.Inequality, [ Constant true; condition ] -> ValueSome (negate condition)
    | _ -> ValueNone

/// <summary>
/// Booleans in one form, the choices: a condition that is a constant is the branch it chooses, and a choice between
/// <see langword="true"/> and <see langword="false"/> is its condition or the negation of it. <c>&amp;&amp;</c> and
/// <c>||</c> need no rule of their own: the compiler writes them as a choice already.
/// </summary>
let private canonicalChoice (condition : Expr) (whenTrue : Expr) (whenFalse : Expr) : Expr voption =
    match condition, whenTrue, whenFalse with
    | Constant chosen, _, _ -> ValueSome (if chosen then whenTrue else whenFalse)
    | _, Constant true, Constant false -> ValueSome condition
    | _, Constant false, Constant true -> ValueSome (negate condition)
    | _ -> ValueNone

/// <summary>
/// The index of the item that the property of a reference tuple with <paramref name="name"/> reads:
/// <c>Item1</c> to <c>Item7</c>.
/// </summary>
let private tupleItemIndex (name : string) : int voption =
    if
        name.Length = 5
        && name.StartsWith ("Item", StringComparison.Ordinal)
        && name[4] >= '1'
        && name[4] <= '7'
    then
        ValueSome (int name[4] - int '1')
    else
        ValueNone

/// <summary>
/// The value among <paramref name="values"/> that <paramref name="property"/> reads, when it is one of
/// <paramref name="fields"/>, which are in the order of the values.
/// </summary>
let private valueOfField (fields : PropertyInfo array) (values : Expr list) (property : PropertyInfo) : Expr voption =
    fields
    |> Array.tryFindIndex (fun field -> String.Equals (field.Name, property.Name, StringComparison.Ordinal))
    |> ValueOption.map (fun index -> values[index])

/// <summary>
/// The reading of a field of a record, an anonymous record or a union case that is built in the same place is the value
/// the field was given. The properties <c>Item1</c> to <c>Item7</c> of a reference tuple become the reading of an item
/// of the tuple.
/// </summary>
let private eliminatePropertyOfBuiltValue (receiver : Expr) (property : PropertyInfo) : Expr voption =
    match receiver with
    | NewRecord (recordType, values) ->
        property
        |> valueOfField (FSharpType.GetRecordFields (recordType, BindingFlags.Public ||| BindingFlags.NonPublic)) values
    | NewUnionCase (case, values) -> property |> valueOfField (case.GetFields ()) values
    | _ ->
        match tupleItemIndex property.Name with
        | ValueSome index when
            FSharpType.IsTuple receiver.Type
            && not receiver.Type.IsValueType
            ->
            ValueSome (Expr.TupleGet (receiver, index))
        | _ -> ValueNone

/// <summary>
/// Whether a call of <paramref name="method"/> always runs the code of <paramref name="method"/>. A member that a
/// derived class can override does not: its definition says nothing about what the override does, so such a call is
/// left a call.
/// </summary>
let private isStaticallyBound (method : MethodBase) : bool =
    not method.IsVirtual
    || method.IsFinal
    || (
        match method.DeclaringType with
        | null -> false
        | declaringType -> declaringType.IsSealed
    )

/// <summary>
/// The sizes of the groups that the definitions of methods take their arguments in, by method; empty for a method that
/// takes them as one group. They are kept for the process like the definitions, because reading an attribute takes
/// microseconds too.
/// </summary>
let private argumentGroupSizes = ConcurrentDictionary<MethodBase, ImmutableArray<int>>()

/// <summary>
/// Reads the sizes of the argument groups of a method from its
/// <see cref="T:Microsoft.FSharp.Core.CompilationArgumentCountsAttribute"/>; a delegate that is created once, for
/// <see cref="M:System.Collections.Concurrent.ConcurrentDictionary`2.GetOrAdd(`0,System.Func{`0,`1})"/>.
/// </summary>
let private readArgumentGroupSizes =
    Func<MethodBase, ImmutableArray<int>>(fun method ->
        match method.GetCustomAttribute<CompilationArgumentCountsAttribute>() with
        | null -> ImmutableArray.Empty
        | counts -> ImmutableArray.CreateRange counts.Counts
    )

/// <summary>
/// Splits the arguments of a call into the groups that the definition of the called function takes one after another.
/// The compiler passes the arguments of a curried function as one flat list and records the sizes of the groups in
/// <see cref="T:Microsoft.FSharp.Core.CompilationArgumentCountsAttribute"/>; without it there is one group. An empty
/// group stands for the unit argument.
/// </summary>
let private argumentGroups (method : MethodBase) (arguments : Expr list) : Expr list list =
    let sizes = argumentGroupSizes.GetOrAdd (method, readArgumentGroupSizes)

    if sizes.IsEmpty then
        [ arguments ]
    else
        sizes
        |> Seq.mapFold (fun rest size -> rest |> List.splitAt size) arguments
        |> fst
        |> Seq.toList

/// <summary>
/// <paramref name="receiver"/> as <paramref name="definition"/> takes it after its first <paramref name="skipped"/>
/// parameters, which take the witnesses: a member of a base class is defined for the base class, so a receiver of a
/// derived class is passed as the base class.
/// </summary>
let rec private receiverFor (skipped : int) (definition : Expr) (receiver : Expr) : Expr =
    match definition with
    | Lambda (_, body) when skipped > 0 -> receiverFor (skipped - 1) body receiver
    | Lambda (parameter, _) when not (Type.(=) (parameter.Type, receiver.Type)) -> Expr.Coerce (receiver, parameter.Type)
    | _ -> receiver

/// <summary>
/// The reflected definition of an inline function or member that is called with witnesses, with the method that the
/// definition is kept for. The compiler keeps the definition of a function of a module for the form of the function
/// that takes the witnesses, and the definition of a member of a type for the member itself. Either definition takes
/// the witnesses first, then the receiver, then the arguments.
/// </summary>
let private definitionWithWitnesses (method : MethodInfo) (witnessMethod : MethodInfo) : struct (MethodBase * Expr) voption =
    match reflectedDefinitionOf witnessMethod with
    | ValueSome definition -> ValueSome struct (witnessMethod :> MethodBase, definition)
    | ValueNone ->
        reflectedDefinitionOf method
        |> ValueOption.map (fun definition -> struct (method :> MethodBase, definition))

/// <summary>
/// Whether a function of <paramref name="functionType"/> takes <paramref name="groups"/> one after another, every
/// group as one argument: a tuple when the group has several values, the unit value when it has none.
/// </summary>
/// <remarks>
/// A definition is applied only to arguments it takes. The compiler calls a method with the arguments of its
/// definition, but a quotation that is built through the API can call an inline member without the witnesses that the
/// definition of the member takes first, and
/// <see cref="M:Microsoft.FSharp.Quotations.FSharpExpr.Applications(Microsoft.FSharp.Quotations.FSharpExpr,Microsoft.FSharp.Collections.FSharpList{Microsoft.FSharp.Collections.FSharpList{Microsoft.FSharp.Quotations.FSharpExpr}})"/>
/// raises an exception for an argument of another type.
/// </remarks>
let rec private takes (functionType : Type) (groups : Expr list list) : bool =
    match groups with
    | [] -> true
    | group :: rest ->
        functionType.IsGenericType
        && Type.(=) (functionType.GetGenericTypeDefinition (), typedefof<_ -> _>)
        && (let types = functionType.GetGenericArguments ()

            let argumentType =
                match group with
                | [] -> typeof<unit>
                | [ argument ] -> argument.Type
                | arguments -> FSharpType.MakeTupleType (arguments |> Seq.map _.Type |> Seq.toArray)

            Type.(=) (types[0], argumentType) && takes types[1] rest)

/// <summary>
/// The error for a call of a function with a reflected definition that calls itself, when the call reads a value of
/// the document.
/// </summary>
/// <param name="subject">What calls itself, as the message names it.</param>
/// <param name="call">The call.</param>
let private recursiveDefinition (subject : string) (call : Expr) : TranslationError =
    call
    |> TranslationError.ofQuotation
        TranslationErrorCode.NormalizationDiverged
        $"%s{subject} calls itself, directly or through the functions it calls, so its definition cannot be written into a query."
        (ImmutableArray.Create<string>
            "Call the function outside the quotation and use its result, or write the condition without recursion.")

/// <summary>
/// The error for a quotation whose simplification does not end. It names no part of the quotation: what is at hand
/// when the limit is reached is a product of the rewriting, not something the user wrote.
/// </summary>
let private tooManyRewrites : TranslationError =
    TranslationError.ofWhole
        TranslationErrorCode.NormalizationDiverged
        $"The quotation was not brought into its simplest form within %d{MaxRewrites} rewriting steps: its simplification does not end."

/// <summary>
/// The error for a quotation that is nested more deeply than the stack of the thread can follow. It names no part of
/// the quotation, because printing that part would need the stack that has run out.
/// </summary>
let private nestedTooDeeply : TranslationError =
    TranslationError.ofWhole
        TranslationErrorCode.NormalizationDiverged
        "The quotation is nested too deeply to be brought into its simplest form."

/// <summary>
/// The error for a construct of F# that a query has no counterpart for.
/// </summary>
let private unsupported (construct : string) (hint : string) (expr : Expr) : TranslationError =
    expr
    |> TranslationError.ofQuotation
        TranslationErrorCode.Unsupported
        $"A query cannot express %s{construct}."
        (ImmutableArray.Create<string> hint)

/// <summary>
/// The error for <paramref name="expr"/> when it is a construct that a query cannot express whatever stands inside it:
/// a recursive function, an assignment, a sequence of statements, a loop or exception handling.
/// </summary>
let private neverExpressed (expr : Expr) : TranslationError voption =
    match expr with
    | LetRecursive _ ->
        expr
        |> unsupported "a recursive function" "Write the condition without recursion."
        |> ValueSome
    | VarSet _
    | PropertySet _
    | FieldSet _
    | AddressSet _ ->
        expr
        |> unsupported "an assignment" "A query only reads the document."
        |> ValueSome
    | Sequential _ ->
        expr
        |> unsupported "a sequence of statements" "Write one expression."
        |> ValueSome
    | ForIntegerRangeLoop _
    | WhileLoop _ ->
        expr
        |> unsupported "a loop" "Use the functions of the List, Array and Seq modules on the collections of the document."
        |> ValueSome
    | TryWith _
    | TryFinally _ ->
        expr
        |> unsupported "exception handling" "A query does not raise exceptions."
        |> ValueSome
    | _ -> ValueNone

/// <summary>
/// The nodes of <paramref name="expr"/> that stand at more than one place of it, by their identity.
/// </summary>
/// <remarks>
/// <para>
/// The compiler makes a node of its own for every place of a quotation, so a node stands at two places only when
/// something put it there: a quotation that is spliced into two places is the same nodes at both, and code can build
/// a tree with one node twice. A later stage takes one node for one value, so such a node has to stay one node
/// through every rewrite, as a value that a <see langword="let"/> binds does.
/// </para>
/// <para>
/// A variable is no such node: every place that reads it reads the one variable anyway. A quotation that is nested
/// too deeply is not walked to its end; the normalization that follows reports it.
/// </para>
/// </remarks>
let private nodesAtSeveralPlacesOf (expr : Expr) : HashSet<obj> =
    let seen = HashSet<obj> ReferenceEqualityComparer.Instance
    let several = HashSet<obj> ReferenceEqualityComparer.Instance

    let rec visit (expr : Expr) : unit =
        match expr with
        | Var _ -> ()
        | _ ->
            if not (seen.Add expr) then
                // Its parts were walked at the first place.
                several.Add expr |> ignore
            elif RuntimeHelpers.TryEnsureSufficientExecutionStack () then
                match expr with
                | ShapeLambda (_, body) -> visit body
                | ShapeCombination (_, parts) -> List.iter visit parts
                | ShapeVar _ -> ()

    visit expr
    several

/// <summary>
/// One normalization: it holds what the rewriting of one quotation has to remember.
/// </summary>
/// <param name="quotation">The quotation that is normalized.</param>
type private Normalization (quotation : Expr) =

    /// <summary>
    /// The functions whose reflected definitions are being normalized right now, the innermost on top. A call of one
    /// of them inside such a definition is a recursion.
    /// </summary>
    let inlining = Stack<MethodBase>()

    /// <summary>
    /// The methods whose reflected definitions have been written into the quotation already. The definition of a
    /// method is one tree for the process, so a second call of the method needs a copy of it: the nodes of a call are
    /// its own, as the values are that the call computes.
    /// </summary>
    let expanded = HashSet<MethodBase>()

    /// <summary>
    /// The functions that were found to call themselves, directly or through the functions they call. A call of one
    /// of them stays a call.
    /// </summary>
    let recursive = List<MethodBase>()

    /// <summary>
    /// The nodes that stand at several places of the quotation, by their identity: the ones that it comes with, and
    /// every value that was bound by <see langword="let"/> or passed as an argument and is used more than once. Such
    /// a node has to stay one node whatever is rewritten around it and inside it.
    /// </summary>
    let shared = nodesAtSeveralPlacesOf quotation

    /// <summary>
    /// The normal forms of the nodes that stand at several places, by their identity, so that every place gets the
    /// same one.
    /// </summary>
    let normalOfShared = Dictionary<obj, Expr> ReferenceEqualityComparer.Instance

    /// The number of rewriting steps taken so far.
    let mutable rewrites = 0

    /// The first error; after it nothing is rewritten any more.
    let mutable failure : TranslationError voption = ValueNone

    /// Whether the error is that the quotation is nested too deeply, so that no part of it can be printed.
    let mutable isTooDeep = false

    /// The error that stopped the normalization, when one did.
    member _.Failure = failure

    /// The functions that the normalization found to call themselves, whose calls it left calls.
    member _.Recursive : IReadOnlyList<MethodBase> = recursive

    /// <summary>
    /// The reflected definition of <paramref name="method"/> applied to the arguments of the call. No value when the
    /// definition does not take these arguments, and none when <paramref name="method"/> calls itself, directly or
    /// through the functions it calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A function that calls itself is found while its definition is normalized: the definition holds a call of a
    /// function whose definition is being normalized. That call stays a call, and so does the call that led to it,
    /// whose normalized definition is not used. What becomes of such calls is decided when the whole quotation is
    /// normalized: one that is left and reads a value of the document is the error, and one in code that never runs
    /// is gone by then.
    /// </para>
    /// <para>
    /// The normal form of a definition is not kept for the next call of the method. A value that the definition binds
    /// and reads twice is one node at both places, which says that it is computed once; kept, it would be that one
    /// node in every call, and the calls would share a value that each of them computes for itself. So every call
    /// after the first normalizes a copy of the definition:
    /// <see cref="M:Microsoft.FSharp.Quotations.FSharpExpr.Substitute(Microsoft.FSharp.Core.FSharpFunc{Microsoft.FSharp.Quotations.FSharpVar,Microsoft.FSharp.Core.FSharpOption{Microsoft.FSharp.Quotations.FSharpExpr}})"/>
    /// builds every node anew, also when it replaces nothing.
    /// </para>
    /// </remarks>
    /// <param name="method">The called method.</param>
    /// <param name="definition">Its reflected definition.</param>
    /// <param name="groups">The arguments, in the groups the definition takes them in.</param>
    member private this.Inline (method : MethodBase, definition : Expr, groups : Expr list list) : Expr voption =
        if inlining |> Seq.exists (isSameDefinition method) then
            recursive.Add method
            ValueNone
        else
            let ownDefinition =
                if expanded.Add method then
                    definition
                else
                    definition.Substitute (fun _ -> None)

            inlining.Push method
            let normalDefinition = this.Normalize ownDefinition
            inlining.Pop () |> ignore

            if
                failure.IsNone
                && not (isAmong recursive method)
                && takes normalDefinition.Type groups
            then
                ValueSome (Expr.Applications (normalDefinition, groups))
            else
                ValueNone

    /// <summary>
    /// A call of a function or method that has a reflected definition is that definition applied to the receiver and
    /// the arguments of the call, unless a derived class can override the method, the definition does not take these
    /// arguments, or the function calls itself.
    /// </summary>
    member private this.InlineCall (receiver : Expr option, method : MethodInfo, arguments : Expr list) : Expr voption =
        match reflectedDefinitionOf method with
        | ValueSome definition when isStaticallyBound method ->
            let groups =
                match receiver with
                | Some receiver ->
                    [ receiverFor 0 definition receiver ]
                    :: argumentGroups method arguments
                | None -> argumentGroups method arguments

            this.Inline (method, definition, groups)
        | _ -> ValueNone

    /// <summary>
    /// The reading of a property whose getter has a reflected definition is that definition applied to the receiver
    /// and the arguments, unless a derived class can override the getter, the definition does not take these
    /// arguments, or the getter calls itself. A module-level value is not such a reading: it is computed once, when
    /// its module is initialized, so it stays a reference to that value. It is told from a static property by its
    /// definition, which is the value and not a function of the unit value.
    /// </summary>
    member private this.InlineGetter (receiver : Expr option, property : PropertyInfo, arguments : Expr list) : Expr voption =
        match property.GetMethod with
        | null -> ValueNone
        | getter ->
            match reflectedDefinitionOf getter, receiver with
            | ValueSome definition, Some receiver when isStaticallyBound getter ->
                this.Inline (getter, definition, [ [ receiverFor 0 definition receiver ]; arguments ])
            | ValueSome definition, None when not (Type.(=) (definition.Type, property.PropertyType)) ->
                this.Inline (getter, definition, [ arguments ])
            | _ -> ValueNone

    /// <summary>
    /// The rewrites that look at a node before its parts are normalized, because they replace what the parts are.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>A quotation that carries its compiled value, which a parameter marked with
    /// <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> receives, is the
    /// quotation.</description></item>
    /// <item><description>The address of a value as the receiver of a property is the value. Only a struct anonymous
    /// record is read this way, and such a node has to go first:
    /// <see cref="M:Microsoft.FSharp.Quotations.ExprShapeModule.RebuildShapeCombination(System.Object,Microsoft.FSharp.Collections.FSharpList{Microsoft.FSharp.Quotations.FSharpExpr})"/>
    /// refuses to rebuild it.</description></item>
    /// <item><description>A call that passes witnesses is the call without them, or, when the called function or
    /// member is <see langword="inline"/> and has a reflected definition, that definition applied to the witnesses,
    /// the receiver and the arguments. The witnesses are functions the compiler writes for the operators of a member
    /// constraint; they are not a part of what the user wrote and are not normalized.</description></item>
    /// </list>
    /// </remarks>
    member private this.RewriteBeforeParts (expr : Expr) : Expr voption =
        match expr with
        | WithValue (_, _, quotation) -> ValueSome quotation
        | PropertyGet (Some (AddressOf receiver), property, arguments) ->
            ValueSome (Expr.PropertyGet (receiver, property, arguments))
        | CallWithWitnesses (receiver, method, witnessMethod, witnesses, arguments) ->
            let inlined =
                match definitionWithWitnesses method witnessMethod with
                | ValueSome (struct (defined, definition)) when isStaticallyBound method ->
                    let groups = [
                        for witness in witnesses do
                            [ witness ]
                        match receiver with
                        | Some receiver -> [ receiverFor witnesses.Length definition receiver ]
                        | None -> ()
                        yield! argumentGroups method arguments
                    ]

                    this.Inline (defined, definition, groups)
                | _ -> ValueNone

            match inlined, receiver with
            | ValueSome _, _ -> inlined
            | ValueNone, _ when failure.IsSome -> ValueNone
            | ValueNone, Some receiver -> ValueSome (Expr.Call (receiver, method, arguments))
            | ValueNone, None -> ValueSome (Expr.Call (method, arguments))
        | _ -> ValueNone

    /// <summary>
    /// One rewriting step at a node whose parts are in normal form, when a rule applies to it.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>A function written in place and applied in place is its body with the argument at the places
    /// of the parameter. A function of a tuple that is applied to a tuple is covered by that: its body reads the items
    /// of the tuple, which the rule for items takes from the tuple.</description></item>
    /// <item><description>A <see langword="let"/> is its body with the bound value at the places of the variable,
    /// unless the variable is mutable and the body assigns to it. This covers the bindings the compiler writes itself:
    /// the copy of a struct before a member of it is read, which is a mutable variable that nothing assigns to when the
    /// member is not known to leave the struct as it is, the value a <see langword="match"/> looks at, and the parts of
    /// a tuple pattern.</description></item>
    /// <item><description>The reading of an item of a tuple that is built in the same place is that item, and the test
    /// for a case of a union that is built in the same place is a constant.</description></item>
    /// <item><description>The rules for choices, for the functions of FSharp.Core, for properties of values built in
    /// place and for reflected definitions are functions and members of their own.</description></item>
    /// </list>
    /// </remarks>
    member private this.Rewrite (expr : Expr) : Expr voption =
        match expr with
        | Application (Lambda (parameter, body), argument) -> ValueSome (bind shared parameter argument body)
        | Let (variable, value, body) when not variable.IsMutable || not (isChanged variable body) ->
            ValueSome (bind shared variable value body)
        | IfThenElse (condition, whenTrue, whenFalse) -> canonicalChoice condition whenTrue whenFalse
        | TupleGet (NewTuple items, index) -> ValueSome items[index]
        | UnionCaseTest (NewUnionCase (case, _), tested) -> ValueSome (Expr.Value (case.Tag = tested.Tag))
        | Call (receiver, method, arguments) ->
            match receiver, knownFunctionOf method with
            | None, ValueSome known ->
                eliminatePipeOrComposition known arguments
                |> ValueOption.orElseWith (fun () -> eliminatePairFunction known arguments)
                |> ValueOption.orElseWith (fun () -> canonicalBooleanCall known arguments)
            | _ -> this.InlineCall (receiver, method, arguments)
        | PropertyGet (receiver, property, arguments) ->
            match receiver, arguments with
            | Some built, [] ->
                eliminatePropertyOfBuiltValue built property
                |> ValueOption.orElseWith (fun () -> this.InlineGetter (receiver, property, arguments))
            | _ -> this.InlineGetter (receiver, property, arguments)
        | _ -> ValueNone

    /// <summary>
    /// <paramref name="expr"/> with its parts in normal form; the same node when they already were.
    /// </summary>
    /// <remarks>
    /// A choice has its condition normalized first. When the condition is a constant then, the branches are left as
    /// they are: the rewrite that follows takes the branch the constant chooses, and the other branch is code that
    /// never runs, which may have no normal form at all.
    /// </remarks>
    member private this.NormalizeParts (expr : Expr) : Expr =
        match expr with
        | IfThenElse (condition, whenTrue, whenFalse) ->
            let normalCondition = this.Normalize condition

            let struct (normalWhenTrue, normalWhenFalse) =
                match normalCondition with
                | Constant _ -> struct (whenTrue, whenFalse)
                | _ -> struct (this.Normalize whenTrue, this.Normalize whenFalse)

            if
                obj.ReferenceEquals (normalCondition, condition)
                && obj.ReferenceEquals (normalWhenTrue, whenTrue)
                && obj.ReferenceEquals (normalWhenFalse, whenFalse)
            then
                expr
            else
                Expr.IfThenElse (normalCondition, normalWhenTrue, normalWhenFalse)
        | ShapeVar _ -> expr
        | ShapeLambda (parameter, body) ->
            let normalBody = this.Normalize body

            if obj.ReferenceEquals (normalBody, body) then
                expr
            else
                Expr.Lambda (parameter, normalBody)
        | ShapeCombination (shape, parts) ->
            let normalParts = parts |> List.map this.Normalize

            if areSame parts normalParts then
                expr
            else
                RebuildShapeCombination (shape, normalParts)

    /// <summary>
    /// The normal form of <paramref name="expr"/>, or any quotation and a
    /// <see cref="P:FSharp.Azure.Cosmos.Quotations.Normalizer.Normalization.Failure"/>.
    /// </summary>
    /// <remarks>
    /// A node that stands at several places is normalized at the first of them, and the other places get the same
    /// normal form: normalizing it again at every place would give every place a node of its own.
    /// </remarks>
    member this.Normalize (expr : Expr) : Expr =
        if shared.Count > 0 && shared.Contains expr then
            match normalOfShared.TryGetValue expr with
            | true, normal -> normal
            | _ ->
                let normal = this.NormalizeAnew expr

                if failure.IsNone then
                    normalOfShared.Add (expr, normal)

                    // The normal form stands at those places from now on, and it is its own normal form.
                    if shared.Add normal then
                        normalOfShared.Add (normal, normal)

                normal
        else
            this.NormalizeAnew expr

    /// <summary>
    /// The normal form of <paramref name="expr"/>, worked out now, or any quotation and a
    /// <see cref="P:FSharp.Azure.Cosmos.Quotations.Normalizer.Normalization.Failure"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parts of a node are normalized first, then the node is rewritten, and what a rewrite gives is normalized
    /// again, in a loop: a quotation that has no normal form would otherwise use up the stack before it uses up
    /// <see cref="P:FSharp.Azure.Cosmos.Quotations.Normalizer.MaxRewrites"/>.
    /// </para>
    /// <para>
    /// When the parts of a node fail and the node is a construct that a query cannot express whatever is inside it,
    /// the node is the failure. So the outermost such construct is reported, as it is when nothing fails inside it.
    /// A quotation that is nested too deeply stays the failure whatever stands around it: the error of a construct
    /// shows the construct, and nothing that deep can be printed with the stack that is left.
    /// </para>
    /// </remarks>
    member private this.NormalizeAnew (expr : Expr) : Expr =
        let mutable current = expr
        let mutable isNormal = false

        while not isNormal && failure.IsNone do
            if not (RuntimeHelpers.TryEnsureSufficientExecutionStack ()) then
                failure <- ValueSome nestedTooDeeply
                isTooDeep <- true
            else
                let rewritten =
                    match this.RewriteBeforeParts current with
                    | ValueSome _ as rewritten -> rewritten
                    | ValueNone when failure.IsSome -> ValueNone
                    | ValueNone ->
                        let node = current
                        current <- this.NormalizeParts node

                        if failure.IsSome then
                            if not isTooDeep then
                                failure <- neverExpressed node |> ValueOption.orElse failure

                            ValueNone
                        else
                            this.Rewrite current

                match rewritten with
                | ValueSome next when rewrites < MaxRewrites ->
                    rewrites <- rewrites + 1
                    current <- next
                | ValueSome _ -> failure <- ValueSome tooManyRewrites
                | ValueNone -> isNormal <- true

        current

/// The function at the head of a chain of applications: what is applied to the first argument.
let rec private appliedFunction (expr : Expr) : Expr =
    match expr with
    | Application (func, _) -> appliedFunction func
    | _ -> expr

/// <summary>
/// The error for a function that is applied to a value of the document and is not written in the quotation.
/// </summary>
/// <remarks>
/// A function that reads no variable of the quotation is a compiled value that the quotation has captured, however
/// the quotation gets to it: by its name, or from a property or a field of a captured value. What a call returns is
/// compiled whatever the call is given, because it is the definition of the called function that cannot be read. Any
/// other function that reads a variable is chosen or computed for every document.
/// </remarks>
let private appliedFunctionValue (application : Expr) : TranslationError =
    let applied = appliedFunction application

    let compiledFunction =
        match applied with
        | Call (_, method, _) -> ValueSome $"The function that '%s{method.Name}' returns"
        | _ when not (Seq.isEmpty (applied.GetFreeVars ())) -> ValueNone
        | ValueWithName (_, _, name) -> ValueSome $"The function '%s{name}'"
        | PropertyGet (_, property, _) -> ValueSome $"The function '%s{property.Name}'"
        | FieldGet (_, field) -> ValueSome $"The function '%s{field.Name}'"
        | _ -> ValueSome "A function"

    match compiledFunction with
    | ValueSome compiledFunction ->
        application
        |> TranslationError.ofQuotation
            TranslationErrorCode.CapturedFunctionValue
            $"%s{compiledFunction} is a compiled value and is applied to a value of the document. A query cannot apply it, because its definition cannot be read."
            (ImmutableArray.Create<string>(
                "Mark the function with [<ReflectedDefinition>], so that its definition can be read.",
                "Or write the condition as a quotation and splice it into the query with %."
            ))
    | ValueNone ->
        application
        |> TranslationError.ofQuotation
            TranslationErrorCode.Unsupported
            "A function that is chosen or computed when the query runs is applied to a value of the document. A query can apply only a function whose definition is written in the quotation."
            ImmutableArray.Empty

/// <summary>
/// The error for <paramref name="expr"/> when it is a construct that a query cannot express, whatever its parts are.
/// </summary>
/// <remarks>
/// An application of a function that is not written in the quotation is an error only when it depends on a variable
/// of the quotation, which is how a value of the document is reached. Without one it is a value like any other captured
/// value, and the stage that computes such values outside the query computes it. The same holds for a call of a
/// function that calls itself, which the normalization left a call: the compiled function computes such a value.
/// </remarks>
/// <param name="recursive">The functions that the normalization found to call themselves.</param>
/// <param name="expr">A part of a quotation in normal form.</param>
let private residueAt (recursive : IReadOnlyList<MethodBase>) (expr : Expr) : TranslationError voption =
    let readsDocument () = not (Seq.isEmpty (expr.GetFreeVars ()))

    let callsItself (method : MethodBase | null) =
        match method with
        | null -> false
        | method -> isAmong recursive method

    match expr with
    | Application _ when readsDocument () -> ValueSome (appliedFunctionValue expr)
    | Call (_, method, _) when callsItself method && readsDocument () ->
        ValueSome (recursiveDefinition $"The function '%s{method.Name}'" expr)
    | PropertyGet (_, property, _) when callsItself property.GetMethod && readsDocument () ->
        ValueSome (recursiveDefinition $"The property '%s{property.Name}'" expr)
    | Let _ ->
        expr
        |> unsupported "a mutable variable that is assigned to" "Bind every value once, with an immutable let."
        |> ValueSome
    | AddressOf _ ->
        expr
        |> unsupported "the address of a value" "Read the value itself."
        |> ValueSome
    | _ -> neverExpressed expr

/// <summary>
/// The first part of a quotation in normal form that a query cannot express, as an error; an outer part before the
/// parts inside it.
/// </summary>
let rec private findResidue (recursive : IReadOnlyList<MethodBase>) (expr : Expr) : TranslationError voption =
    match expr with
    | ShapeVar _ -> ValueNone
    | ShapeLambda (_, body) -> findResidue recursive body
    | ShapeCombination (_, parts) ->
        residueAt recursive expr
        |> ValueOption.orElseWith (fun () -> parts |> List.tryPick (findResidue recursive))

/// <summary>
/// The normal form of <paramref name="expr"/>, or the reason it has none that a query can express.
/// </summary>
/// <remarks>
/// <para>
/// The normal form computes what <paramref name="expr"/> computes, as long as the code in it has no side effects: a
/// value bound by <see langword="let"/> is written at every place it is used, and a bound value or a part of a tuple
/// or record that nothing reads is dropped with its code. An exception is such an effect: code that is dropped raises
/// none, and code that is written at the places of its variable runs where they are.
/// </para>
/// <para>
/// These are errors:
/// </para>
/// <list type="bullet">
/// <item><description>a call of a function with a reflected definition that calls itself, when the call reads a value
/// of the document, and a quotation whose simplification does not end,
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.NormalizationDiverged"/>;</description></item>
/// <item><description>a function that is a compiled value and is applied to a value of the document,
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.CapturedFunctionValue"/>;</description></item>
/// <item><description>a recursive function, a mutable variable that is assigned to, the address of a value, a sequence of
/// statements, a loop and exception handling,
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.Unsupported"/>.</description></item>
/// </list>
/// <para>
/// Of the constructs that stay in the normal form, the outermost one is the error. A recursive function, an assignment,
/// a sequence of statements, a loop and exception handling are errors whatever is inside them, so the outermost of
/// these is the error also when the quotation inside it has no normal form at all.
/// </para>
/// <para>
/// Only what stays is an error. Code that never runs is not in the normal form, so it may hold any of these: the
/// branch that a constant condition does not choose, also when the constant gets to the condition through a
/// <see langword="let"/>, a parameter or a reflected definition. What cannot be told that way is a quotation whose
/// simplification does not end, which stops the normalization where it is met.
/// </para>
/// </remarks>
let normalize (expr : Expr) : Result<Expr, TranslationError> =
    let normalization = Normalization expr
    let normal = normalization.Normalize expr

    match
        normalization.Failure
        |> ValueOption.orElseWith (fun () -> findResidue normalization.Recursive normal)
    with
    | ValueSome failure -> Error failure
    | ValueNone -> Ok normal
