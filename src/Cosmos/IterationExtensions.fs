namespace Microsoft.Azure.Cosmos

open System
open System.Collections.Generic
open System.Linq
open System.Linq.Expressions
open System.Reflection
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading
open Microsoft.Azure.Cosmos
open Microsoft.Azure.Cosmos.Linq
open FSharp.Control

[<AutoOpen>]
module FeedIteratorExtensions =

    // See https://github.com/Azure/azure-cosmos-dotnet-v3/issues/903
    type FeedIterator<'T> with

        /// Converts the iterator to an async sequence of items.
        member iterator.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) = taskSeq {
            while iterator.HasMoreResults do
                let! page = iterator.ReadNextAsync (cancellationToken)

                for item in page do
                    cancellationToken.ThrowIfCancellationRequested ()
                    yield item
        }

[<Sealed>]
type private ParameterReplaceExpressionVisitor (replacements : IReadOnlyDictionary<ParameterExpression, Expression>) =
    inherit ExpressionVisitor ()

    override _.VisitParameter parameter =
        match replacements.TryGetValue parameter with
        | true, replacement -> replacement
        | _ -> parameter :> Expression

[<RequireQualifiedAccess>]
module LinqTranslation =

    type ExpressionTranslator = Expression -> Expression voption

    let private containsMethodDefinition =
        typeof<Enumerable>.GetMethods ()
        |> Array.tryFind (fun methodInfo -> methodInfo.Name = "Contains" && methodInfo.GetParameters().Length = 2)
        |> ValueOption.ofOption
        |> ValueOption.defaultWith (fun () -> invalidOp "System.Linq.Enumerable.Contains method is not available.")

    let private replaceParameters (replacements : (ParameterExpression * Expression) seq) (expressionToReplace : Expression) =
        let replacementMap = Dictionary<ParameterExpression, Expression> ()
        replacements |> Seq.iter (fun (parameter, replacement) -> replacementMap[parameter] <- replacement)
        let visitor = ParameterReplaceExpressionVisitor replacementMap
        visitor.Visit expressionToReplace

    let rec private tryGetLambda (functionExpression : Expression) : LambdaExpression voption =
        let tryFromBlock (block : BlockExpression) =
            if block.Expressions.Count = 0 then
                ValueNone
            else
                let assignments =
                    block.Expressions
                    |> Seq.take (block.Expressions.Count - 1)
                    |> Seq.choose (fun expression ->
                        match expression with
                        | :? BinaryExpression as assignment when assignment.NodeType = ExpressionType.Assign ->
                            match assignment.Left with
                            | :? ParameterExpression as parameter -> Some (parameter, assignment.Right)
                            | _ -> None
                        | _ ->
                            None)
                    |> Seq.toArray
                if assignments.Length <> block.Expressions.Count - 1 then
                    ValueNone
                else
                    let reduced =
                        assignments
                        |> Seq.fold
                            (fun current (parameter, replacement) -> replaceParameters [ parameter, replacement ] current)
                            block.Expressions[block.Expressions.Count - 1]
                    tryGetLambda reduced

        let tryCompose (name : string) (left : LambdaExpression) (right : LambdaExpression) =
            if left.Parameters.Count = 1 && right.Parameters.Count = 1 then
                match name with
                | "op_ComposeRight" ->
                    let parameter = Expression.Parameter (left.Parameters[0].Type, left.Parameters[0].Name)
                    let leftBody = replaceParameters [ left.Parameters[0], parameter ] left.Body
                    let rightBody = replaceParameters [ right.Parameters[0], leftBody ] right.Body
                    ValueSome (Expression.Lambda (rightBody, [| parameter |]))
                | "op_ComposeLeft" ->
                    let parameter = Expression.Parameter (right.Parameters[0].Type, right.Parameters[0].Name)
                    let rightBody = replaceParameters [ right.Parameters[0], parameter ] right.Body
                    let leftBody = replaceParameters [ left.Parameters[0], rightBody ] left.Body
                    ValueSome (Expression.Lambda (leftBody, [| parameter |]))
                | _ ->
                    ValueNone
            else
                ValueNone

        match functionExpression with
        | :? LambdaExpression as lambda ->
            ValueSome lambda
        | :? UnaryExpression as unary when unary.NodeType = ExpressionType.Quote ->
            match unary.Operand with
            | :? LambdaExpression as lambda -> ValueSome lambda
            | _ -> ValueNone
        | :? MethodCallExpression as call
            when call.Method.Name = "ToFSharpFunc"
                 && call.Arguments.Count = 1 ->
            tryGetLambda call.Arguments[0]
        | :? BlockExpression as block ->
            tryFromBlock block
        | :? MethodCallExpression as call
            when call.Method.Name = "op_ComposeRight" || call.Method.Name = "op_ComposeLeft"
                 && call.Arguments.Count = 2 ->
            match tryGetLambda call.Arguments[0], tryGetLambda call.Arguments[1] with
            | ValueSome left, ValueSome right -> tryCompose call.Method.Name left right
            | _ -> ValueNone
        | _ ->
            ValueNone

    let private inlineLambdaInvocation (lambda : LambdaExpression) (argument : Expression) =
        if lambda.Parameters.Count <> 1 then
            ValueNone
        else
            ValueSome (replaceParameters [ lambda.Parameters[0], argument ] lambda.Body)

    let private quotationTranslator : ExpressionTranslator =
        fun expression ->
            match expression with
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter"
                     && call.Method.Name.StartsWith ("QuotationTo", StringComparison.Ordinal)
                     && call.Arguments |> Seq.forall (fun argument -> argument.NodeType = ExpressionType.Constant) ->
                try
                    let arguments =
                        call.Arguments
                        |> Seq.map (fun argument -> (argument :?> ConstantExpression).Value)
                        |> Seq.toArray
                    match call.Method.Invoke (null, arguments) with
                    | :? Expression as translated -> ValueSome translated
                    | _ -> ValueNone
                with
                | :? ArgumentException
                | :? InvalidOperationException
                | :? TargetInvocationException ->
                    ValueNone
            | _ ->
                ValueNone

    let private functionalOperatorTranslator : ExpressionTranslator =
        fun expression ->
            let isCallableInvoke (call : MethodCallExpression) =
                call.Method.Name = "Invoke" && not (isNull call.Object) && call.Arguments.Count = 1

            match expression with
            | :? MethodCallExpression as call
                when call.Method.Name = "op_PipeRight"
                     && call.Arguments.Count = 2 ->
                match tryGetLambda call.Arguments[1] with
                | ValueSome lambda -> inlineLambdaInvocation lambda call.Arguments[0]
                | ValueNone -> ValueSome (Expression.Invoke (call.Arguments[1], call.Arguments[0]) :> Expression)
            | :? MethodCallExpression as call
                when call.Method.Name = "op_PipeLeft"
                     && call.Arguments.Count = 2 ->
                match tryGetLambda call.Arguments[0] with
                | ValueSome lambda -> inlineLambdaInvocation lambda call.Arguments[1]
                | ValueNone -> ValueSome (Expression.Invoke (call.Arguments[0], call.Arguments[1]) :> Expression)
            | :? MethodCallExpression as call when isCallableInvoke call ->
                match tryGetLambda call.Object with
                | ValueSome lambda -> inlineLambdaInvocation lambda call.Arguments[0]
                | ValueNone -> ValueNone
            | :? InvocationExpression as invocation when invocation.Arguments.Count = 1 ->
                match tryGetLambda invocation.Expression with
                | ValueSome lambda -> inlineLambdaInvocation lambda invocation.Arguments[0]
                | ValueNone -> ValueNone
            | _ ->
                ValueNone

    let private optionTranslator : ExpressionTranslator =
        fun expression ->
            match expression with
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.OptionModule"
                     && call.Arguments.Count = 1
                     && call.Method.Name = "IsSome" ->
                ValueSome (Expression.NotEqual (call.Arguments[0], Expression.Constant (null, call.Arguments[0].Type)) :> Expression)
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.OptionModule"
                     && call.Arguments.Count = 1
                     && call.Method.Name = "IsNone" ->
                ValueSome (Expression.Equal (call.Arguments[0], Expression.Constant (null, call.Arguments[0].Type)) :> Expression)
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.OptionModule"
                     && call.Arguments.Count = 2
                     && call.Method.Name = "DefaultValue" ->
                let optionValue = call.Arguments[1]
                let valueProperty = optionValue.Type.GetProperty ("Value")
                if isNull valueProperty then
                    ValueNone
                else
                    ValueSome (
                        Expression.Condition (
                            Expression.NotEqual (optionValue, Expression.Constant (null, optionValue.Type)),
                            Expression.Property (optionValue, valueProperty),
                            call.Arguments[0]
                        )
                        :> Expression
                    )
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.ValueOption"
                     && call.Arguments.Count = 1
                     && call.Method.Name = "IsSome" ->
                let valueOption = call.Arguments[0]
                let isSomeProperty = valueOption.Type.GetProperty ("IsSome")
                if isNull isSomeProperty then
                    ValueNone
                else
                    ValueSome (Expression.Property (valueOption, isSomeProperty) :> Expression)
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.ValueOption"
                     && call.Arguments.Count = 1
                     && call.Method.Name = "IsNone" ->
                let valueOption = call.Arguments[0]
                let isSomeProperty = valueOption.Type.GetProperty ("IsSome")
                if isNull isSomeProperty then
                    ValueNone
                else
                    ValueSome (Expression.Not (Expression.Property (valueOption, isSomeProperty)) :> Expression)
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && call.Method.DeclaringType.FullName = "Microsoft.FSharp.Core.ValueOption"
                     && call.Arguments.Count = 2
                     && call.Method.Name = "DefaultValue" ->
                let valueOption = call.Arguments[1]
                let isSomeProperty = valueOption.Type.GetProperty ("IsSome")
                let valueProperty = valueOption.Type.GetProperty ("Value")
                if isNull isSomeProperty || isNull valueProperty then
                    ValueNone
                else
                    ValueSome (
                        Expression.Condition (
                            Expression.Property (valueOption, isSomeProperty),
                            Expression.Property (valueOption, valueProperty),
                            call.Arguments[0]
                        )
                        :> Expression
                    )
            | _ ->
                ValueNone

    let private collectionContainsTranslator : ExpressionTranslator =
        fun expression ->
            match expression with
            | :? MethodCallExpression as call
                when not (isNull call.Method.DeclaringType)
                     && (call.Method.DeclaringType.FullName = "Microsoft.FSharp.Collections.SeqModule"
                         || call.Method.DeclaringType.FullName = "Microsoft.FSharp.Collections.ListModule"
                         || call.Method.DeclaringType.FullName = "Microsoft.FSharp.Collections.ArrayModule")
                     && call.Method.Name = "Contains"
                     && call.Arguments.Count = 2 ->
                let elementType = call.Method.GetGenericArguments ()[0]
                let enumerableType = typedefof<IEnumerable<_>>.MakeGenericType [| elementType |]
                let source =
                    if enumerableType.IsAssignableFrom call.Arguments[1].Type then
                        call.Arguments[1]
                    else
                        Expression.Convert (call.Arguments[1], enumerableType)
                ValueSome (
                    Expression.Call (containsMethodDefinition.MakeGenericMethod [| elementType |], source, call.Arguments[0])
                    :> Expression
                )
            | _ ->
                ValueNone

    let private defaultExpressionTranslators : ExpressionTranslator list =
        [ quotationTranslator; functionalOperatorTranslator; optionTranslator; collectionContainsTranslator ]

    // Guards against accidental translator cycles where translation rules keep rewriting the same node.
    // 16 is intentionally conservative for deeply nested combinations (pipes/compositions/options) while
    // still preventing runaway recursion from cyclic translator configurations.
    // When the limit is reached, translation stops and the current expression is returned as-is.
    let private maxTranslationDepth = 16

    let private gate = obj ()
    let mutable private customExpressionTranslators : ExpressionTranslator list = []

    type private TranslationVisitor (translators : ExpressionTranslator list) =
        inherit ExpressionVisitor ()

        override this.Visit expression =
            let visited = base.Visit expression

            let rec apply expressionToTranslate depth =
                if depth >= maxTranslationDepth || isNull expressionToTranslate then
                    expressionToTranslate
                else
                    let translated =
                        translators
                        |> Seq.tryPick (fun translator ->
                            // Translators are expected to return a new expression instance when they apply.
                            // Returning the original instance signals "no translation" for that pass.
                            match translator expressionToTranslate with
                            | ValueSome result when not (obj.ReferenceEquals (result, expressionToTranslate)) -> Some result
                            | _ -> None)
                    match translated with
                    | Some result -> apply (this.Visit result) (depth + 1)
                    | None -> expressionToTranslate

            apply visited 0

    let registerExpressionTranslator (translator : ExpressionTranslator) =
        lock gate (fun () -> customExpressionTranslators <- translator :: customExpressionTranslators)

    let clearCustomExpressionTranslators () = lock gate (fun () -> customExpressionTranslators <- [])

    let translateExpression (expression : Expression) =
        match expression with
        | null -> raise (ArgumentNullException (nameof expression))
        | expression ->
            let translators =
                lock gate (fun () -> defaultExpressionTranslators @ List.rev customExpressionTranslators)
            let visitor = TranslationVisitor translators
            visitor.Visit expression

    let translateQueryable<'T> (query : IQueryable<'T>) =
        match query with
        | null -> raise (ArgumentNullException (nameof query))
        | query ->
            let translatedExpression = translateExpression query.Expression
            if obj.ReferenceEquals (translatedExpression, query.Expression) then
                query
            else
                query.Provider.CreateQuery<'T> (translatedExpression)

[<AutoOpen>]
module QueryableExtensions =

    type IQueryable<'T> with

        /// <summary>
        /// Translates F#-specific LINQ constructs in this query to Cosmos-compatible expression forms.
        /// </summary>
        member query.TranslateForCosmosLinq () : IQueryable<'T> = LinqTranslation.translateQueryable query

        /// <summary>
        /// Translates F#-specific LINQ constructs and creates Cosmos DB <see cref="FeedIterator{T}" />.
        /// </summary>
        member query.ToTranslatedFeedIterator () : FeedIterator<'T> = query.TranslateForCosmosLinq().ToFeedIterator()

        member inline query.AsAsyncEnumerable<'T> ([<Optional; EnumeratorCancellation>] cancellationToken : CancellationToken) =
            query.ToTranslatedFeedIterator().AsAsyncEnumerable<'T> (cancellationToken)
