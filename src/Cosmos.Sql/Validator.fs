/// <summary>
/// Validation of a <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/> against the rules of the query language that the
/// grammar cannot express, so that nothing invalid reaches the printer unnoticed.
/// <para>
/// The rules checked are those the first translator slice needs:
/// </para>
/// <list type="bullet">
/// <item><description><c>SELECT *</c> only with exactly one collection in the <c>FROM</c> clause;</description></item>
/// <item><description><c>TOP</c> never in a subquery;</description></item>
/// <item><description>
/// no parameter in a <c>GROUP BY</c> key or in an <c>ORDER BY</c> item other than the arguments of a function call
/// allowed there, and parameter names of the form <c>@name</c>;
/// </description></item>
/// <item><description>
/// <c>ORDER BY</c> items only property paths or calls of functions flagged
/// <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.AllowedInOrderBy"/>; <c>ORDER BY RANK</c> items only calls of
/// functions flagged <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.AllowedInOrderByRank"/>; functions flagged
/// <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.OnlyInOrderByRank"/> nowhere else;
/// </description></item>
/// <item><description>identifiers of the form <c>[A-Za-z_][A-Za-z_0-9]*</c> that are not reserved words;</description></item>
/// <item><description><c>IN</c> lists and select lists with at least one element;</description></item>
/// <item><description>
/// built-in functions known to the catalog and called with an accepted number of arguments;
/// </description></item>
/// <item><description>
/// finite number literals, integer literals within -2^53..2^53 unless
/// <see cref="P:FSharp.Azure.Cosmos.Sql.ValidationOptions.AllowLossyInt64"/> is set, and non-negative
/// <c>TOP</c>, <c>OFFSET</c> and <c>LIMIT</c> counts.
/// </description></item>
/// </list>
/// <para>
/// Some rules hold by construction of the syntax tree: <c>OFFSET</c> and <c>LIMIT</c> are one field, counts are
/// integer literals or parameters, object keys are strings, and plain and rank <c>ORDER BY</c> items cannot be mixed.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharp.Azure.Cosmos.Sql.SqlQuery

open System
open System.Collections.Immutable
open System.Globalization

/// The largest magnitude a JSON number holds exactly as an integer, 2^53.
let private maxExactInteger = 9007199254740992L

/// A fragment of the query for messages.
let private fragment (expression : ScalarExpression) = Printer.printScalar PropertyStyle.Brackets expression

/// Whether a parameter occurs anywhere in an expression, its subqueries included.
let rec private containsParameter (expression : ScalarExpression) =
    let any (items : EquatableArray<ScalarExpression>) = items.Items |> Seq.exists containsParameter

    match expression with
    | ScalarExpression.ParameterRef _ -> true
    | ScalarExpression.Literal _
    | ScalarExpression.PropertyRef _ -> false
    | ScalarExpression.MemberIndexer (left, right)
    | ScalarExpression.Binary (_, left, right)
    | ScalarExpression.Coalesce (left, right) -> containsParameter left || containsParameter right
    | ScalarExpression.Unary (_, operand) -> containsParameter operand
    | ScalarExpression.Conditional (first, second, third)
    | ScalarExpression.Between (first, _, second, third) ->
        containsParameter first
        || containsParameter second
        || containsParameter third
    | ScalarExpression.Like (value, pattern, _, _) -> containsParameter value || containsParameter pattern
    | ScalarExpression.In (needle, _, haystack) -> containsParameter needle || any haystack
    | ScalarExpression.FunctionCall (_, items)
    | ScalarExpression.ArrayCreate items -> any items
    | ScalarExpression.ObjectCreate properties ->
        properties.Items
        |> Seq.exists (fun struct (_, value) -> containsParameter value)
    | ScalarExpression.Exists query
    | ScalarExpression.Array query
    | ScalarExpression.All query
    | ScalarExpression.First query
    | ScalarExpression.Last query
    | ScalarExpression.Subquery query -> queryContainsParameter query

/// Whether a parameter occurs anywhere in a query, its subqueries included.
and private queryContainsParameter (query : SqlQuery) =
    let countIsParameter (count : SpecValue) = count.IsParameter

    let rec collectionContainsParameter (expression : CollectionExpression) =
        let inCollection (collection : Collection) =
            match collection with
            | Collection.InputPath _ -> false
            | Collection.Subquery subquery -> queryContainsParameter subquery

        match expression with
        | CollectionExpression.Aliased (collection, _)
        | CollectionExpression.ArrayIterator (_, collection) -> inCollection collection
        | CollectionExpression.Join (left, right) ->
            collectionContainsParameter left
            || collectionContainsParameter right

    (query.Top |> ValueOption.exists countIsParameter)
    || (
        match query.Select with
        | SelectSpec.Star -> false
        | SelectSpec.Value expression -> containsParameter expression
        | SelectSpec.List items ->
            items.Items
            |> Seq.exists (fun item -> containsParameter item.Expression)
    )
    || (query.From |> ValueOption.exists collectionContainsParameter)
    || (query.Where |> ValueOption.exists containsParameter)
    || (query.GroupBy.Items |> Seq.exists containsParameter)
    || (query.OrderBy.Items
        |> Seq.exists (fun item -> containsParameter item.Expression))
    || (query.OffsetLimit
        |> ValueOption.exists (fun struct (offset, limit) -> countIsParameter offset || countIsParameter limit))

/// Whether an expression is a property path: an alias followed by member accesses with literal names or indexes.
let rec private isPropertyPath (expression : ScalarExpression) =
    match expression with
    | ScalarExpression.PropertyRef _ -> true
    | ScalarExpression.MemberIndexer (target, ScalarExpression.Literal (Literal.String _ | Literal.Int _)) ->
        isPropertyPath target
    | _ -> false

/// The catalog entry of a call of a built-in function, if the expression is one.
let private builtInCall (expression : ScalarExpression) =
    match expression with
    | ScalarExpression.FunctionCall (FunctionRef.BuiltIn name, _) -> Catalog.tryFind name
    | _ -> ValueNone

/// Walks a query and collects the errors.
type private Validator (options : ValidationOptions) =
    let errors = ImmutableArray.CreateBuilder<ValidationError>()

    let fail code message = errors.Add { Code = code; Message = message }

    /// The errors found so far, in the order the walk met them.
    member _.Errors = errors.ToImmutable ()

    /// <summary>
    /// Checks that <paramref name="identifier"/> has the form of an identifier and is not a reserved word.
    /// </summary>
    /// <param name="role">What the identifier is in the query, for the message.</param>
    /// <param name="identifier">The identifier to check.</param>
    member _.Identifier (role : string) (identifier : Identifier) =
        let text = identifier.Value

        if not (Keywords.isValidIdentifier text) then
            fail
                ValidationErrorCode.InvalidIdentifier
                $"The %s{role} '%s{text}' is not an identifier of the form [A-Za-z_][A-Za-z_0-9]*."
        elif Keywords.isReserved text then
            fail ValidationErrorCode.ReservedIdentifier $"The %s{role} '%s{text}' is a reserved word of the query language."

    /// <summary>
    /// Checks that <paramref name="name"/> is <c>@</c> followed by an identifier.
    /// </summary>
    /// <param name="name">The parameter name to check.</param>
    member _.Parameter (name : ParameterName) =
        let text = name.Value

        if
            text.Length < 2
            || text[0] <> '@'
            || not (Keywords.isValidIdentifierSpan (text.AsSpan 1))
        then
            fail
                ValidationErrorCode.InvalidParameterName
                $"The parameter name '%s{text}' is not of the form @name with a name matching [A-Za-z_][A-Za-z_0-9]*."

    /// <summary>
    /// Checks a <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c> count: a literal must not be negative, and a parameter must
    /// have a valid name.
    /// </summary>
    /// <param name="count">The count to check.</param>
    member this.Count (count : SpecValue) =
        match count with
        | SpecValue.Literal value when value < 0L ->
            fail ValidationErrorCode.NegativeCount $"The TOP, OFFSET or LIMIT count %d{value} is negative."
        | SpecValue.Literal _ -> ()
        | SpecValue.Parameter name -> this.Parameter name

    /// <summary>
    /// Checks that <paramref name="literal"/> can be written exactly: an integer within -2^53..2^53, unless
    /// <see cref="P:FSharp.Azure.Cosmos.Sql.ValidationOptions.AllowLossyInt64"/> is set, and a finite number.
    /// </summary>
    /// <param name="literal">The literal to check.</param>
    member _.Literal (literal : Literal) =
        match literal with
        | Literal.Int value when
            not options.AllowLossyInt64
            && (value > maxExactInteger || value < -maxExactInteger)
            ->
            fail
                ValidationErrorCode.LossyInteger
                ($"The integer literal %d{value} is outside -2^53..2^53, which a JSON number cannot hold exactly; "
                 + "pass it as a parameter.")
        | Literal.Float value when not (Double.IsFinite value) ->
            fail
                ValidationErrorCode.NonFiniteNumber
                $"The number literal %s{value.ToString (CultureInfo.InvariantCulture)} is not a finite number."
        | _ -> ()

    /// <summary>
    /// Checks a function call: the name of a user-defined function, and for a built-in function that the catalog knows
    /// it, that it can be called, that it accepts <paramref name="count"/> arguments and that it may appear here.
    /// </summary>
    /// <param name="inRank">Whether the call is an item of an <c>ORDER BY RANK</c> clause or part of one.</param>
    /// <param name="func">The function that is called.</param>
    /// <param name="count">The number of arguments of the call.</param>
    member _.Function (inRank : bool) (func : FunctionRef) (count : int) =
        match func with
        | FunctionRef.Udf name ->
            if not (Keywords.isSafeIdentifier name) then
                fail
                    ValidationErrorCode.InvalidFunctionName
                    $"The user-defined function name '%s{name}' is not an identifier or is a reserved word."
        | FunctionRef.BuiltIn name ->
            match Catalog.tryFind name with
            | ValueNone ->
                fail
                    ValidationErrorCode.UnknownFunction
                    $"The query language has no built-in function '%s{name}'; call a user-defined function as udf.%s{name}."
            | ValueSome spec ->
                // LEFT and RIGHT are keywords that the grammar accepts as function names; the other keywords the SDK
                // lists as names (ALL, ARRAY, LIKE) are expressions of their own
                if
                    Keywords.isReserved name
                    && not (String.Equals (name, "LEFT", StringComparison.OrdinalIgnoreCase))
                    && not (String.Equals (name, "RIGHT", StringComparison.OrdinalIgnoreCase))
                then
                    fail
                        ValidationErrorCode.InvalidFunctionName
                        $"'%s{name}' is a keyword of the query language and cannot be called as a function."
                elif not (spec.Arity.Accepts count) then
                    fail
                        ValidationErrorCode.FunctionArity
                        ($"The number of arguments of the built-in function %s{spec.Name} must be %O{spec.Arity}, "
                         + $"but the call has %d{count}.")

                if spec.OnlyInOrderByRank && not inRank then
                    fail
                        ValidationErrorCode.ScoringFunctionOutsideRank
                        $"The scoring function %s{spec.Name} may appear only in an ORDER BY RANK clause."

    /// <summary>
    /// Validates an expression; <paramref name="inRank"/> allows the functions that may appear only in
    /// <c>ORDER BY RANK</c>.
    /// </summary>
    /// <param name="inRank">Whether the expression is an item of an <c>ORDER BY RANK</c> clause or part of one.</param>
    /// <param name="expression">The expression to validate.</param>
    member this.Scalar (inRank : bool) (expression : ScalarExpression) : unit =
        let scalar = this.Scalar inRank

        match expression with
        | ScalarExpression.Literal literal -> this.Literal literal
        | ScalarExpression.ParameterRef name -> this.Parameter name
        | ScalarExpression.PropertyRef identifier -> this.Identifier "property reference" identifier
        | ScalarExpression.MemberIndexer (left, right)
        | ScalarExpression.Binary (_, left, right)
        | ScalarExpression.Coalesce (left, right) ->
            scalar left
            scalar right
        | ScalarExpression.Unary (_, operand) -> scalar operand
        | ScalarExpression.Conditional (first, second, third)
        | ScalarExpression.Between (first, _, second, third) ->
            scalar first
            scalar second
            scalar third
        | ScalarExpression.Like (value, pattern, _, _) ->
            scalar value
            scalar pattern
        | ScalarExpression.In (needle, _, haystack) ->
            if haystack.IsEmpty then
                fail ValidationErrorCode.EmptyInList $"The IN list of {fragment needle} has no elements."

            scalar needle

            for item in haystack.Items do
                scalar item
        | ScalarExpression.FunctionCall (func, arguments) ->
            this.Function inRank func arguments.Length

            for argument in arguments.Items do
                scalar argument
        | ScalarExpression.ArrayCreate items ->
            for item in items.Items do
                scalar item
        | ScalarExpression.ObjectCreate properties ->
            for struct (_, value) in properties.Items do
                scalar value
        | ScalarExpression.Exists query
        | ScalarExpression.Array query
        | ScalarExpression.All query
        | ScalarExpression.First query
        | ScalarExpression.Last query
        | ScalarExpression.Subquery query -> this.Query true query

    /// <summary>
    /// Checks the identifiers of <paramref name="path"/> and of the path before it.
    /// </summary>
    /// <param name="path">The last segment of the path.</param>
    member this.Path (path : PathExpression) =
        let parent (parent : PathExpression voption) =
            match parent with
            | ValueSome parent -> this.Path parent
            | ValueNone -> ()

        match path with
        | PathExpression.Identifier (previous, name) ->
            parent previous
            this.Identifier "path segment" name
        | PathExpression.Number (previous, _)
        | PathExpression.String (previous, _) -> parent previous

    /// <summary>
    /// Checks a collection: the input name and the path of an input path, or the subquery.
    /// </summary>
    /// <param name="collection">The collection to check.</param>
    member this.Collection (collection : Collection) =
        match collection with
        | Collection.InputPath (input, path) ->
            this.Identifier "input name" input
            path |> ValueOption.iter this.Path
        | Collection.Subquery query -> this.Query true query

    /// <summary>
    /// Checks a source of the <c>FROM</c> clause: its collections and their aliases.
    /// </summary>
    /// <param name="expression">The source to check.</param>
    member this.CollectionExpression (expression : CollectionExpression) =
        match expression with
        | CollectionExpression.Aliased (collection, alias) ->
            this.Collection collection
            alias
            |> ValueOption.iter (this.Identifier "collection alias")
        | CollectionExpression.ArrayIterator (alias, collection) ->
            this.Identifier "collection alias" alias
            this.Collection collection
        | CollectionExpression.Join (left, right) ->
            this.CollectionExpression left
            this.CollectionExpression right

    /// <summary>
    /// Checks an item of an <c>ORDER BY</c> clause: what it may sort by, that no parameter chooses the sort key, and
    /// the expression itself.
    /// </summary>
    /// <param name="rank">Whether the clause is <c>ORDER BY RANK</c>.</param>
    /// <param name="item">The item to check.</param>
    member this.OrderByItem (rank : bool) (item : OrderByItem) =
        let expression = item.Expression

        // The arguments of an allowed call may be parameters: Microsoft Learn passes the query vector of a vector search
        // as one (ORDER BY VectorDistance(c.embedding, @embedding)), and so does the SDK's query plan baseline
        let allowedCall =
            match builtInCall expression with
            | ValueSome spec ->
                if rank then
                    spec.AllowedInOrderByRank
                else
                    spec.AllowedInOrderBy
            | ValueNone -> false

        if rank then
            if not allowedCall then
                fail
                    ValidationErrorCode.RankItemNotScoringFunction
                    $"The ORDER BY RANK item {fragment expression} is not a call of a scoring function."
        elif not allowedCall && not (isPropertyPath expression) then
            fail
                ValidationErrorCode.OrderByItemNotPath
                ($"The ORDER BY item {fragment expression} is neither a property path nor a call of a function "
                 + "allowed in ORDER BY.")

        if not allowedCall && containsParameter expression then
            fail
                ValidationErrorCode.ParameterInOrderBy
                $"The ORDER BY item {fragment expression} contains a parameter, which cannot choose the sort key."

        this.Scalar rank expression

    /// <summary>
    /// Checks a query clause by clause, in the order the query text shows them, and its subqueries.
    /// </summary>
    /// <param name="inSubquery">Whether the query is a subquery, where <c>TOP</c> is not allowed.</param>
    /// <param name="query">The query to check.</param>
    member this.Query (inSubquery : bool) (query : SqlQuery) : unit =
        match query.Top with
        | ValueSome top ->
            if inSubquery then
                fail
                    ValidationErrorCode.TopInSubquery
                    "A subquery has a TOP clause; the service accepts TOP only in the outermost query."

            this.Count top
        | ValueNone -> ()

        match query.Select with
        | SelectSpec.Star ->
            match query.From with
            | ValueSome (CollectionExpression.Aliased _)
            | ValueSome (CollectionExpression.ArrayIterator _) -> ()
            | ValueSome (CollectionExpression.Join _)
            | ValueNone ->
                fail
                    ValidationErrorCode.SelectStarWithoutSingleAlias
                    "SELECT * needs exactly one collection in the FROM clause; project the collections explicitly."
        | SelectSpec.Value expression -> this.Scalar false expression
        | SelectSpec.List items ->
            if items.IsEmpty then
                fail ValidationErrorCode.EmptySelectList "The select list has no items."

            for item in items.Items do
                this.Scalar false item.Expression
                item.Alias
                |> ValueOption.iter (this.Identifier "select alias")

        query.From |> ValueOption.iter this.CollectionExpression
        query.Where |> ValueOption.iter (this.Scalar false)

        for key in query.GroupBy.Items do
            if containsParameter key then
                fail
                    ValidationErrorCode.ParameterInGroupBy
                    $"The GROUP BY key {fragment key} contains a parameter; the service accepts none there."

            this.Scalar false key

        for item in query.OrderBy.Items do
            this.OrderByItem query.OrderByRank item

        match query.OffsetLimit with
        | ValueSome (struct (offset, limit)) ->
            this.Count offset
            this.Count limit
        | ValueNone -> ()

/// <summary>
/// Checks <paramref name="query"/> and its subqueries against the rules the grammar cannot express, with
/// <paramref name="options"/>.
/// </summary>
/// <param name="options">The settings of validation.</param>
/// <param name="query">The query to check.</param>
/// <returns>The broken rules, in the order the query text would show them; empty for a valid query.</returns>
let validateWith (options : ValidationOptions) (query : SqlQuery) : ImmutableArray<ValidationError> =
    let validator = Validator options
    validator.Query false query
    validator.Errors

/// <summary>
/// Checks <paramref name="query"/> and its subqueries against the rules the grammar cannot express, with
/// <see cref="P:FSharp.Azure.Cosmos.Sql.ValidationOptions.Default"/>.
/// </summary>
/// <param name="query">The query to check.</param>
/// <returns>The broken rules, in the order the query text would show them; empty for a valid query.</returns>
let validate (query : SqlQuery) : ImmutableArray<ValidationError> = validateWith ValidationOptions.Default query
