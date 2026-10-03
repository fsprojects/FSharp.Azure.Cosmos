/// <summary>
/// The pass behind the inline print mode: it replaces every parameter of a query with the literal its encoder gives.
/// <para>
/// The encoder contract is one function that gives, for a <see cref="T:FSharp.Azure.Cosmos.Sql.ParameterName"/>, the
/// literal that replaces it, if the parameter has one. The translator builds it from the encoders of its parameter slots;
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Inline.ofJsonElement(System.Text.Json.JsonElement)"/> turns a value that a
/// serializer has written as JSON into such a literal.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Sql.Inline

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Globalization
open System.Text.Json

let private forAll (predicate : 'T -> bool) (items : EquatableArray<'T>) =
    let mutable result = true
    let mutable index = 0

    while result && index < items.Length do
        result <- predicate items[index]
        index <- index + 1

    result

/// <summary>
/// Whether <paramref name="expression"/> is a constant that can replace a parameter: a literal, or an array or object
/// literal whose elements are constants.
/// </summary>
/// <param name="expression">The expression to check.</param>
let rec isConstant (expression : ScalarExpression) =
    match expression with
    | ScalarExpression.Literal _ -> true
    | ScalarExpression.ArrayCreate items -> items |> forAll isConstant
    | ScalarExpression.ObjectCreate properties ->
        properties
        |> forAll (fun struct (_, value) -> isConstant value)
    | _ -> false

/// Rewrites a query or a scalar expression with the inline values of its parameters, collecting the errors.
type private Substitution (encode : ParameterName -> ScalarExpression voption) =
    let errors = ImmutableArray.CreateBuilder<ValidationError>()
    // A parameter used several times is reported once
    let reported = HashSet<string>(StringComparer.Ordinal)

    let fail code (name : ParameterName) message =
        if reported.Add name.Value then
            errors.Add { Code = code; Message = message }

    member _.Errors = errors.ToImmutable ()

    member this.Scalar (expression : ScalarExpression) : ScalarExpression =
        let scalar = this.Scalar
        let query = this.Query

        match expression with
        | ScalarExpression.ParameterRef name ->
            match encode name with
            | ValueSome value when isConstant value -> value
            | ValueSome _ ->
                fail
                    ValidationErrorCode.InlineValueNotConstant
                    name
                    ($"The inline value of the parameter %s{name.Value} is not a constant: "
                     + "a literal, or an array or object literal of constants.")

                expression
            | ValueNone ->
                fail ValidationErrorCode.MissingInlineValue name $"The parameter %s{name.Value} has no inline value."
                expression
        | ScalarExpression.Literal _
        | ScalarExpression.PropertyRef _ -> expression
        | ScalarExpression.MemberIndexer (target, index) -> ScalarExpression.MemberIndexer (scalar target, scalar index)
        | ScalarExpression.Binary (operator, left, right) -> ScalarExpression.Binary (operator, scalar left, scalar right)
        | ScalarExpression.Unary (operator, operand) -> ScalarExpression.Unary (operator, scalar operand)
        | ScalarExpression.Conditional (condition, whenTrue, whenFalse) ->
            ScalarExpression.Conditional (scalar condition, scalar whenTrue, scalar whenFalse)
        | ScalarExpression.Coalesce (left, right) -> ScalarExpression.Coalesce (scalar left, scalar right)
        | ScalarExpression.In (needle, negated, haystack) ->
            ScalarExpression.In (scalar needle, negated, haystack |> EquatableArray.map scalar)
        | ScalarExpression.Between (value, negated, low, high) ->
            ScalarExpression.Between (scalar value, negated, scalar low, scalar high)
        | ScalarExpression.Like (value, pattern, negated, escape) ->
            ScalarExpression.Like (scalar value, scalar pattern, negated, escape)
        | ScalarExpression.FunctionCall (func, arguments) ->
            ScalarExpression.FunctionCall (func, arguments |> EquatableArray.map scalar)
        | ScalarExpression.ArrayCreate items -> ScalarExpression.ArrayCreate (items |> EquatableArray.map scalar)
        | ScalarExpression.ObjectCreate properties ->
            properties
            |> EquatableArray.map (fun struct (key, value) -> struct (key, scalar value))
            |> ScalarExpression.ObjectCreate
        | ScalarExpression.Exists subquery -> ScalarExpression.Exists (query subquery)
        | ScalarExpression.Array subquery -> ScalarExpression.Array (query subquery)
        | ScalarExpression.All subquery -> ScalarExpression.All (query subquery)
        | ScalarExpression.First subquery -> ScalarExpression.First (query subquery)
        | ScalarExpression.Last subquery -> ScalarExpression.Last (query subquery)
        | ScalarExpression.Subquery subquery -> ScalarExpression.Subquery (query subquery)

    member this.Count (count : SpecValue) : SpecValue =
        match count with
        | SpecValue.Literal _ -> count
        | SpecValue.Parameter name ->
            match encode name with
            | ValueSome (ScalarExpression.Literal (Literal.Int value)) -> SpecValue.Literal value
            | ValueSome _ ->
                fail
                    ValidationErrorCode.InlineCountNotInteger
                    name
                    $"The inline value of the TOP, OFFSET or LIMIT parameter %s{name.Value} is not an integer literal."

                count
            | ValueNone ->
                fail ValidationErrorCode.MissingInlineValue name $"The parameter %s{name.Value} has no inline value."
                count

    member this.Collection (collection : Collection) : Collection =
        match collection with
        | Collection.InputPath _ -> collection
        | Collection.Subquery subquery -> Collection.Subquery (this.Query subquery)

    member this.CollectionExpression (expression : CollectionExpression) : CollectionExpression =
        match expression with
        | CollectionExpression.Aliased (collection, alias) -> CollectionExpression.Aliased (this.Collection collection, alias)
        | CollectionExpression.ArrayIterator (alias, collection) ->
            CollectionExpression.ArrayIterator (alias, this.Collection collection)
        | CollectionExpression.Join (left, right) ->
            CollectionExpression.Join (this.CollectionExpression left, this.CollectionExpression right)

    member this.Query (query : SqlQuery) : SqlQuery =
        let scalar = this.Scalar

        let select =
            match query.Select with
            | SelectSpec.Star -> SelectSpec.Star
            | SelectSpec.Value expression -> SelectSpec.Value (scalar expression)
            | SelectSpec.List items ->
                items
                |> EquatableArray.map (fun item -> { item with Expression = scalar item.Expression })
                |> SelectSpec.List

        {
            Select = select
            Distinct = query.Distinct
            Top = query.Top |> ValueOption.map this.Count
            From = query.From |> ValueOption.map this.CollectionExpression
            Where = query.Where |> ValueOption.map scalar
            GroupBy = query.GroupBy |> EquatableArray.map scalar
            OrderBy =
                query.OrderBy
                |> EquatableArray.map (fun item -> { item with Expression = scalar item.Expression })
            OrderByRank = query.OrderByRank
            OffsetLimit =
                query.OffsetLimit
                |> ValueOption.map (fun struct (offset, limit) -> struct (this.Count offset, this.Count limit))
        }

let private result (substitution : Substitution) value =
    let errors = substitution.Errors
    if errors.IsEmpty then Ok value else Error errors

/// <summary>
/// Replaces every parameter of <paramref name="query"/> with the literal <paramref name="encode"/> gives for it, in
/// every clause and subquery.
/// </summary>
/// <param name="encode">
/// Gives the literal that replaces a parameter. It must be a constant (see
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Inline.isConstant(FSharp.Azure.Cosmos.Sql.ScalarExpression)"/>), and an
/// integer literal for a <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c> count.
/// </param>
/// <param name="query">The query whose parameters are replaced.</param>
/// <returns>
/// The query without parameters, or one error per parameter that has no literal or an unusable one.
/// </returns>
let substituteParameters
    (encode : ParameterName -> ScalarExpression voption)
    (query : SqlQuery)
    : Result<SqlQuery, ImmutableArray<ValidationError>> =
    let substitution = Substitution encode
    let substituted = substitution.Query query
    result substitution substituted

/// <summary>
/// Replaces every parameter of <paramref name="expression"/> with the literal <paramref name="encode"/> gives for it,
/// like <see cref="M:FSharp.Azure.Cosmos.Sql.Inline.substituteParameters(Microsoft.FSharp.Core.FSharpFunc{FSharp.Azure.Cosmos.Sql.ParameterName,Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Sql.ScalarExpression}},FSharp.Azure.Cosmos.Sql.SqlQuery)"/>
/// does for a whole query.
/// </summary>
/// <param name="encode">Gives the literal that replaces a parameter; it must be a constant.</param>
/// <param name="expression">The expression whose parameters are replaced.</param>
/// <returns>
/// The expression without parameters, or one error per parameter that has no literal or an unusable one.
/// </returns>
let substituteScalarParameters
    (encode : ParameterName -> ScalarExpression voption)
    (expression : ScalarExpression)
    : Result<ScalarExpression, ImmutableArray<ValidationError>> =
    let substitution = Substitution encode
    let substituted = substitution.Scalar expression
    result substitution substituted

/// <summary>
/// Converts a JSON value, such as one that <see cref="M:System.Text.Json.JsonSerializer.SerializeToElement(System.Object,System.Type,System.Text.Json.JsonSerializerOptions)"/>
/// wrote with the serializer options of the client, into the constant that writes the same JSON.
/// <para>
/// An integral number in the range of <see cref="T:System.Int64"/> becomes an integer
/// <see cref="T:FSharp.Azure.Cosmos.Sql.Literal"/>, any other number a double-precision one; object properties keep
/// their order. The default <see cref="T:System.Text.Json.JsonElement"/>, which holds no value, becomes the literal
/// <c>undefined</c>.
/// </para>
/// </summary>
/// <param name="element">The JSON value.</param>
let rec ofJsonElement (element : JsonElement) : ScalarExpression =
    match element.ValueKind with
    | JsonValueKind.Object ->
        let builder = ImmutableArray.CreateBuilder<struct (string * ScalarExpression)>()

        for property in element.EnumerateObject () do
            builder.Add (struct (property.Name, ofJsonElement property.Value))

        ScalarExpression.ObjectCreate (EquatableArray (builder.ToImmutable ()))
    | JsonValueKind.Array ->
        let builder = ImmutableArray.CreateBuilder<ScalarExpression>(element.GetArrayLength ())

        for item in element.EnumerateArray () do
            builder.Add (ofJsonElement item)

        ScalarExpression.ArrayCreate (EquatableArray (builder.MoveToImmutable ()))
    | JsonValueKind.String -> ScalarExpression.Literal (Literal.String (element.GetString () |> nonNull))
    | JsonValueKind.Number ->
        let mutable integer = 0L

        if element.TryGetInt64 (&integer) then
            ScalarExpression.Literal (Literal.Int integer)
        else
            // Double.Parse rather than GetDouble, which throws for a number beyond the range of a double; the
            // resulting infinity is rejected by the validator
            ScalarExpression.Literal (
                Literal.Float (Double.Parse (element.GetRawText (), NumberStyles.Float, CultureInfo.InvariantCulture))
            )
    | JsonValueKind.True -> ScalarExpression.Literal (Literal.Boolean true)
    | JsonValueKind.False -> ScalarExpression.Literal (Literal.Boolean false)
    | JsonValueKind.Null -> ScalarExpression.Literal Literal.Null
    | _ -> ScalarExpression.Literal Literal.Undefined
