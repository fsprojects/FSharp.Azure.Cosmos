namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Reflection
open FSharp.Quotations
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos.Sql

/// What one walk over a quotation gives: the key its translation is cached under, and the values to bind to the
/// parameters of the cached query text.
[<Struct>]
type QueryShape = {
    /// The structure of the quotation without the values in it: two quotations with equal keys differ only in the
    /// values of their slots, so they share one translation.
    Key : EquatableArray<int64>
    /// <summary>
    /// The value of every slot, in the order of the slots in
    /// <see cref="P:FSharp.Azure.Cosmos.Quotations.Benchmarks.QueryShape.Key"/>. A slot is a place in the quotation
    /// whose value is known before the query runs, so it becomes a parameter of the query: a captured local, a literal,
    /// or a subexpression that does not read the document.
    /// </summary>
    SlotValues : ImmutableArray<objnull>
}

/// The kinds of entries in a shape key. A kind is followed by what identifies the node: types and members by their
/// runtime handles and metadata tokens, a variable by its distance to its binding.
module private Token =

    [<Literal>]
    let Slot = 1L

    [<Literal>]
    let Variable = 2L

    [<Literal>]
    let Lambda = 3L

    [<Literal>]
    let Let = 4L

    [<Literal>]
    let And = 5L

    [<Literal>]
    let Or = 6L

    [<Literal>]
    let Conditional = 7L

    [<Literal>]
    let Property = 8L

    [<Literal>]
    let Field = 9L

    [<Literal>]
    let Call = 10L

/// A variable in scope. The variable of a closed binding, one that does not read the document, stands for its value.
[<Struct>]
type private ScopedVariable = { Variable : Var; IsClosed : bool; Value : objnull }

/// <summary>
/// One walk over a quotation that writes its <see cref="T:FSharp.Azure.Cosmos.Quotations.Benchmarks.QueryShape"/>.
/// <para>
/// A node is closed when nothing under it reads a lambda parameter, which is where the document comes in. A closed
/// node is evaluated on the spot, by reflection, and becomes one slot; the entries its children wrote are taken back.
/// An open node writes its kind and identity, and its children follow.
/// </para>
/// </summary>
[<Sealed>]
type private ShapeWalker () =

    let tokens = ImmutableArray.CreateBuilder<int64> 64
    let slots = ImmutableArray.CreateBuilder<objnull> 8
    // The variables in scope, innermost last
    let scope = List<ScopedVariable>()
    // What every visited child leaves for its parent: its value when it is closed, null when it is open
    let values = List<objnull>()

    let typeToken (clrType : Type) = int64 clrType.TypeHandle.Value

    // The handle of the declaring type also tells the type arguments of a generic type apart, which the metadata
    // token of the member does not
    let declaringTypeToken (info : MemberInfo) =
        match info.DeclaringType with
        | null -> 0L
        | declaringType -> typeToken declaringType

    let closedAs (slotType : Type) (value : objnull) =
        tokens.Add Token.Slot
        tokens.Add (typeToken slotType)
        slots.Add value
        values.Add value
        true

    let opened () =
        values.Add null
        false

    // The values of the children of a closed member node lie in `values` from `valueMark` on, the target first
    let evaluate (info : MemberInfo) (hasTarget : bool) (valueMark : int) : objnull =
        let target = if hasTarget then values[valueMark] else null
        let first = if hasTarget then valueMark + 1 else valueMark
        let arguments = Array.zeroCreate<objnull>(values.Count - first)

        for index in 0 .. arguments.Length - 1 do
            arguments[index] <- values[first + index]

        match info with
        | :? MethodInfo as method -> method.Invoke (target, arguments)
        | :? PropertyInfo as property -> property.GetValue (target, arguments)
        | :? FieldInfo as field -> field.GetValue target
        | _ -> raise (NotSupportedException $"The shape key prototype does not evaluate a %s{info.GetType().Name}.")

    let rec visit (expression : Expr) : bool =
        match expression with
        | Value (value, valueType) -> closedAs valueType value
        | Var variable -> visitVariable variable
        | PropertyGet (target, property, arguments) -> visitMember Token.Property property target arguments expression
        | Call (target, method, arguments) -> visitMember Token.Call method target arguments expression
        | IfThenElse (condition, whenTrue, whenFalse) -> visitConditional condition whenTrue whenFalse
        | Lambda (variable, body) -> visitLambda variable body
        | Let (variable, binding, body) -> visitLet variable binding body
        | FieldGet (target, field) -> visitMember Token.Field field target [] expression
        | Coerce (inner, _) -> visit inner
        | _ -> raise (NotSupportedException $"The shape key prototype does not cover this node: %A{expression}")

    and visitVariable (variable : Var) : bool =
        let mutable index = scope.Count - 1

        while index >= 0
              && not (obj.ReferenceEquals (scope[index].Variable, variable)) do
            index <- index - 1

        if index < 0 then
            raise (NotSupportedException $"The quotation uses the variable %s{variable.Name} without binding it.")

        let scoped = scope[index]

        if scoped.IsClosed then
            closedAs variable.Type scoped.Value
        else
            tokens.Add Token.Variable
            // The distance to the binding instead of the name, so that renaming a variable keeps the key
            tokens.Add (int64 (scope.Count - 1 - index))
            opened ()

    and visitMember (kind : int64) (info : MemberInfo) (target : Expr option) (arguments : Expr list) (expression : Expr) : bool =
        let tokenMark = tokens.Count
        let slotMark = slots.Count
        let valueMark = values.Count
        tokens.Add kind
        tokens.Add (declaringTypeToken info)
        tokens.Add (int64 info.MetadataToken)

        match info with
        | :? MethodInfo as method when method.IsGenericMethod ->
            for typeArgument in method.GetGenericArguments () do
                tokens.Add (typeToken typeArgument)
        | _ -> ()

        let mutable closed = true

        match target with
        | Some target -> closed <- visit target
        | None -> ()

        for argument in arguments do
            // Every argument is visited, also after an open one: its entries belong to the key
            closed <- visit argument && closed

        if closed then
            let value = evaluate info target.IsSome valueMark
            tokens.Count <- tokenMark
            slots.Count <- slotMark
            values.RemoveRange (valueMark, values.Count - valueMark)
            closedAs expression.Type value
        else
            values.RemoveRange (valueMark, values.Count - valueMark)
            opened ()

    // && and || arrive as conditionals with a constant branch, and that constant is no slot. A conditional is never
    // evaluated here, so its closed parts are evaluated one by one, eagerly: `text <> null && text.Length > 0` over a
    // captured null would fail. The real pass has to evaluate a closed conditional as a whole, lazily.
    and visitConditional (condition : Expr) (whenTrue : Expr) (whenFalse : Expr) : bool =
        match whenTrue, whenFalse with
        | _, Value (:? bool as constant, _) when not constant -> visitBoth Token.And condition whenTrue
        | Value (:? bool as constant, _), _ when constant -> visitBoth Token.Or condition whenFalse
        | _ ->
            tokens.Add Token.Conditional
            visit condition |> ignore
            visit whenTrue |> ignore
            visit whenFalse |> ignore
            values.RemoveRange (values.Count - 3, 3)
            opened ()

    and visitBoth (kind : int64) (left : Expr) (right : Expr) : bool =
        tokens.Add kind
        visit left |> ignore
        visit right |> ignore
        values.RemoveRange (values.Count - 2, 2)
        opened ()

    and visitLambda (variable : Var) (body : Expr) : bool =
        tokens.Add Token.Lambda
        tokens.Add (typeToken variable.Type)
        scope.Add { Variable = variable; IsClosed = false; Value = null }
        visit body |> ignore
        scope.RemoveAt (scope.Count - 1)
        // A function is no parameter value, whatever its body is
        values[values.Count - 1] <- null
        false

    and visitLet (variable : Var) (binding : Expr) (body : Expr) : bool =
        let tokenMark = tokens.Count
        let slotMark = slots.Count
        tokens.Add Token.Let

        if visit binding then
            // A closed binding is substituted: the variable stands for its value and the node leaves nothing in the key
            tokens.Count <- tokenMark
            slots.Count <- slotMark
            let value = values[values.Count - 1]
            values.RemoveAt (values.Count - 1)
            scope.Add { Variable = variable; IsClosed = true; Value = value }
            let closed = visit body
            scope.RemoveAt (scope.Count - 1)
            closed
        else
            values.RemoveAt (values.Count - 1)
            scope.Add { Variable = variable; IsClosed = false; Value = null }
            visit body |> ignore
            scope.RemoveAt (scope.Count - 1)
            values[values.Count - 1] <- null
            false

    /// Walks the quotation and gives what the walk wrote; a walker serves one quotation.
    member _.Run (expression : Expr) : QueryShape =
        visit expression |> ignore
        {
            Key = EquatableArray (tokens.DrainToImmutable ())
            SlotValues = slots.DrainToImmutable ()
        }

/// <summary>
/// A prototype of the pass that the plan cache of the quotation translator needs (ADR 0001, section 11), written to
/// measure it and nothing else: it covers the nodes of
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Benchmarks.SampleQuery"/> and refuses every other one.
/// <para>
/// A quotation cannot be a dictionary key as it is: every evaluation of a quotation literal builds a new tree with new
/// variables, and two such trees are not equal. The pass walks the tree once and writes down its structure, with the
/// values taken out, as a sequence of numbers that can be compared and hashed.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module ShapeKey =

    /// <summary>
    /// Walks <paramref name="expression"/> once and gives its key and the values of its slots.
    /// </summary>
    /// <exception cref="T:System.NotSupportedException">The quotation holds a node the prototype does not cover.</exception>
    let compute (expression : Expr) : QueryShape = ShapeWalker().Run expression
