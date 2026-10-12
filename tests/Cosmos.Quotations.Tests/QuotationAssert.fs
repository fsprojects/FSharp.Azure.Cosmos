namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Collections.Concurrent
open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Quotations
open FSharp.Quotations.ExprShape
open FSharp.Quotations.Patterns

open Swensen.Unquote

open FSharp.Azure.Cosmos.Quotations

/// Questions about a quotation that the assertions and the tests ask.
[<RequireQualifiedAccess>]
module Quotation =

    /// <summary>
    /// The variables that stand for the bound variables of every quotation, by nesting depth and type. They are shared,
    /// so that two quotations made canonical hold the very same variables.
    /// </summary>
    let private canonicalVariables = ConcurrentDictionary<struct (int * Type), Var>()

    /// <summary>
    /// Creates the variable for a nesting depth and a type; a delegate that is created once, for
    /// <see cref="M:System.Collections.Concurrent.ConcurrentDictionary`2.GetOrAdd(`0,System.Func{`0,`1})"/>.
    /// </summary>
    let private createCanonicalVariable =
        Func<struct (int * Type), Var>(fun (struct (depth, variableType)) -> Var ($"v%d{depth}", variableType))

    /// <summary>
    /// <paramref name="expr"/> with the variables of <paramref name="replacements"/> replaced, and every variable bound
    /// inside it replaced by the canonical variable of its depth, counted from <paramref name="depth"/>. An inner
    /// binding of a variable hides an outer one of the same variable.
    /// </summary>
    let rec private rename (replacements : Map<Var, Var>) (depth : int) (expr : Expr) : Expr =
        match expr with
        | ShapeVar variable ->
            match replacements |> Map.tryFind variable with
            | Some replacement -> Expr.Var replacement
            | None -> expr
        | ShapeLambda (variable, body) ->
            let replacement = canonicalVariables.GetOrAdd (struct (depth, variable.Type), createCanonicalVariable)
            Expr.Lambda (
                replacement,
                body
                |> rename (replacements |> Map.add variable replacement) (depth + 1)
            )
        | ShapeCombination (shape, parts) -> RebuildShapeCombination (shape, parts |> List.map (rename replacements depth))

    /// <summary>
    /// <paramref name="expr"/> with every bound variable replaced by the variable that stands for its nesting depth and
    /// type. Two quotations that differ only in their bound variables become equal: a quotation compares its variables
    /// by identity, and every quotation literal creates variables of its own.
    /// </summary>
    let canonical (expr : Expr) : Expr = expr |> rename Map.empty 0

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are the same quotation up to their bound
    /// variables.
    /// </summary>
    let areEquivalent (first : Expr) (second : Expr) : bool = (canonical first).Equals(canonical second)

    /// <summary>
    /// The parts of <paramref name="expr"/>, itself included, that satisfy <paramref name="predicate"/>, outermost
    /// first.
    /// </summary>
    let rec filter (predicate : Expr -> bool) (expr : Expr) : Expr list = [
        if predicate expr then
            expr

        match expr with
        | ShapeVar _ -> ()
        | ShapeLambda (_, body) -> yield! filter predicate body
        | ShapeCombination (_, parts) ->
            for part in parts do
                yield! filter predicate part
    ]

    /// <summary>
    /// Whether <paramref name="expr"/> or a part of it satisfies <paramref name="predicate"/>.
    /// </summary>
    let exists (predicate : Expr -> bool) (expr : Expr) : bool = not (filter predicate expr).IsEmpty

/// <summary>
/// A function as compiled code together with the quotation of the same code.
/// </summary>
type Quoted =

    /// <summary>
    /// The two parts of <paramref name="code"/>, which a parameter marked with
    /// <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> that asks for the value received: the compiled
    /// code and its quotation.
    /// </summary>
    static member Split (code : Expr<'Function>) : struct {| Compiled : 'Function; Quotation : Expr<'Function> |} =
        match code with
        | WithValue (compiled, _, quotation) -> struct {|
            Compiled = compiled :?> 'Function
            Quotation = Expr.Cast<'Function> quotation
          |}
        | _ -> invalidArg (nameof code) $"The compiler did not pass the compiled value with the quotation: %A{code}"

    /// <summary>
    /// <paramref name="code"/> as compiled code and as a quotation. The compiler passes both for an argument that is
    /// marked with <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> and asks for the value, so a test
    /// writes a function once and can compare what its quotation means with what the code does.
    /// </summary>
    static member Of
        ([<ReflectedDefinition(true)>] code : Expr<'Function>)
        : struct {| Compiled : 'Function; Quotation : Expr<'Function> |}
        =
        Quoted.Split code

/// Assertions on what the normalizer makes of a quotation.
[<RequireQualifiedAccess>]
module NormalizerAssert =

    /// <summary>
    /// The normal form of <paramref name="expr"/>; fails when the normalizer reports an error.
    /// </summary>
    let WantNormal (expr : Expr) : Expr =
        match Normalizer.normalize expr with
        | Ok normal -> normal
        | Error error ->
            raise (
                AssertFailedException
                    $"The quotation should have a normal form, but: %s{error.Message}\n%s{error.Fragment}\n%A{expr}"
            )

    /// <summary>
    /// The error the normalizer reports for <paramref name="expr"/>; fails when it gives a normal form.
    /// </summary>
    let WantError (expr : Expr) : TranslationError =
        match Normalizer.normalize expr with
        | Ok normal -> raise (AssertFailedException $"The quotation should be an error, but has the normal form\n%A{normal}")
        | Error error -> error

    /// <summary>
    /// Asserts that <paramref name="expected"/> and <paramref name="actual"/> are the same quotation up to their bound
    /// variables.
    /// </summary>
    let AreEquivalent (expected : Expr) (actual : Expr) (message : string) : unit =
        if not (Quotation.areEquivalent expected actual) then
            raise (AssertFailedException $"%s{message}\nExpected:\n%A{expected}\nActual:\n%A{actual}")

    /// <summary>
    /// Asserts that the parts of <paramref name="normal"/> that satisfy <paramref name="predicate"/> stand at
    /// <paramref name="places"/> places and are <paramref name="nodes"/> different nodes.
    /// </summary>
    let AreNodes (nodes : int) (places : int) (predicate : Expr -> bool) (message : string) (normal : Expr) : unit =
        let found = Quotation.filter predicate normal

        Assert.HasCount (places, found, $"The part should stand at %d{places} places.\n%A{normal}")
        let different = HashSet<obj>(Seq.cast<obj> found, ReferenceEqualityComparer.Instance)
        Assert.HasCount (nodes, different, $"%s{message}\n%A{normal}")

    /// <summary>
    /// Asserts that the parts of <paramref name="normal"/> that satisfy <paramref name="predicate"/> are one and the
    /// same node, which stands at <paramref name="places"/> places.
    /// </summary>
    let IsOneNode (places : int) (predicate : Expr -> bool) (message : string) (normal : Expr) : unit =
        AreNodes 1 places predicate message normal

    /// <summary>
    /// Asserts that the function <paramref name="quotation"/> computes, for every one of <paramref name="inputs"/>,
    /// what <paramref name="compiled"/> computes.
    /// </summary>
    let ComputesTheSame (compiled : 'Input -> 'Result) (inputs : 'Input seq) (quotation : Expr) : unit =
        let evaluated = evalRaw<'Input -> 'Result> quotation

        for input in inputs do
            Assert.AreEqual<'Result>(
                compiled input,
                evaluated input,
                $"The quotation should compute what the code computes for %A{input}.\n%A{quotation}"
            )
