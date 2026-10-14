/// <summary>
/// Generators of the Cosmos DB SQL syntax tree of <see cref="N:FSharp.Azure.Cosmos.Sql"/>.
/// <para>
/// <see cref="P:FSharp.Azure.Cosmos.Tests.Models.SqlGenerators.sqlQueryGen"/> and
/// <see cref="P:FSharp.Azure.Cosmos.Tests.Models.SqlGenerators.scalarExpressionGen"/> build trees that are valid by
/// construction: every rule of
/// <see cref="M:FSharp.Azure.Cosmos.Sql.SqlQueryModule.validate(FSharp.Azure.Cosmos.Sql.SqlQuery)"/> is built into
/// them, and nothing is generated and then thrown away. The generators with <c>arbitrary</c> in their name build any
/// tree, valid or not, for the properties that must hold for every tree.
/// </para>
/// <para>
/// A tree halves the size it gives to its children, so its depth is bounded by the logarithm of the size, and a list
/// inside it has at most three items: a failing case stays small enough to read.
/// </para>
/// </summary>
module FSharp.Azure.Cosmos.Tests.Models.SqlGenerators

open System
open System.Collections.Immutable
open Hedgehog
open Hedgehog.FSharp

open FSharp.Azure.Cosmos.Sql

// The most items of a list inside a tree: arguments, array items, object properties, select items, sort keys
[<Literal>]
let private MaxItems = 3

[<Literal>]
let private MaxNameLength = 8

[<Literal>]
let private MaxTextAtoms = 8

// 2^53, the largest integer that a JSON number holds exactly and that the validator accepts as a literal
[<Literal>]
let private MaxExactInteger = 9007199254740992L

// The magnitude up to which finiteNumberGen draws numbers around zero, inclusive
[<Literal>]
let private MaxNumber = 1e15

/// What a generator may build.
[<Struct>]
type private Scope = {
    /// Whether the tree keeps to every rule of the validator.
    Valid : bool
    /// Whether the tree may hold parameters; the keys of a GROUP BY clause may not.
    Parameters : bool
}

/// A segment of a property path, before it is linked to the segments in front of it.
[<RequireQualifiedAccess; Struct>]
type private PathSegment =
    | Name of name : Identifier
    | Index of index : int64
    | Key of key : string

let private validScope = { Valid = true; Parameters = true }

let private arbitraryScope = { Valid = false; Parameters = true }

// Text that exercises the escaping of the printer: the quote, the backslash, the slash, the named escapes, other
// control characters, a space, letters outside ASCII and a character outside the Basic Multilingual Plane. No lone
// surrogate, which JSON text cannot carry
let private textAtoms =
    seq {
        "\""
        "\\"
        "/"
        "\b"
        "\f"
        "\n"
        "\r"
        "\t"
        "\u0001"
        "\u001F"
        " "
        "'"
        "@"
        "é"
        "ж"
        "中"
        "😀"
    }
    |> ImmutableArray.CreateRange

let private binaryOperators =
    seq {
        BinaryOperator.Add
        BinaryOperator.And
        BinaryOperator.BitwiseAnd
        BinaryOperator.BitwiseOr
        BinaryOperator.BitwiseXor
        BinaryOperator.Divide
        BinaryOperator.Equal
        BinaryOperator.GreaterThan
        BinaryOperator.GreaterThanOrEqual
        BinaryOperator.LessThan
        BinaryOperator.LessThanOrEqual
        BinaryOperator.Modulo
        BinaryOperator.Multiply
        BinaryOperator.NotEqual
        BinaryOperator.Or
        BinaryOperator.StringConcat
        BinaryOperator.Subtract
    }
    |> ImmutableArray.CreateRange

let private unaryOperators =
    seq {
        UnaryOperator.BitwiseNot
        UnaryOperator.Not
        UnaryOperator.Minus
        UnaryOperator.Plus
    }
    |> ImmutableArray.CreateRange

// Numbers at the edges of a double, which a range around zero hardly ever reaches
let private edgeNumbers =
    seq {
        Double.MaxValue
        Double.MinValue
        Double.Epsilon
        -0.0
        0.1
    }
    |> ImmutableArray.CreateRange

let private notFiniteNumbers =
    seq {
        Double.NaN
        Double.PositiveInfinity
        Double.NegativeInfinity
    }
    |> ImmutableArray.CreateRange

/// Whether the grammar can call the function by its name: LEFT and RIGHT are the only keywords it accepts as names.
let private isCallable (spec : FunctionSpec) =
    spec.Status.IsSupported
    && (not (Keywords.isReserved spec.Name)
        || String.Equals (spec.Name, "LEFT", StringComparison.OrdinalIgnoreCase)
        || String.Equals (spec.Name, "RIGHT", StringComparison.OrdinalIgnoreCase))

/// The functions a valid tree may call anywhere.
let private scalarFunctions =
    Catalog.all
    |> Seq.filter (fun spec -> isCallable spec && not spec.OnlyInOrderByRank)
    |> ImmutableArray.CreateRange

/// The functions whose call may be an item of an ORDER BY clause.
let private orderByFunctions =
    scalarFunctions
    |> Seq.filter _.AllowedInOrderBy
    |> ImmutableArray.CreateRange

/// The scoring functions, whose call is an item of an ORDER BY RANK clause.
let private rankFunctions =
    Catalog.all
    |> Seq.filter (fun spec -> isCallable spec && spec.AllowedInOrderByRank)
    |> ImmutableArray.CreateRange

/// A name that passes the identifier rule and is no reserved word. A reserved word gets an underscore instead of
/// being drawn again, so the generator needs no filter.
let private safeNameGen : Gen<string> = gen {
    let! first = Gen.frequency [ 9, Gen.alpha; 1, Gen.constant '_' ]

    let! rest =
        Gen.frequency [ 9, Gen.alphaNum; 1, Gen.constant '_' ]
        |> Gen.string (Range.linear 0 (MaxNameLength - 1))

    let name = $"%c{first}%s{rest}"

    return if Keywords.isReserved name then $"%s{name}_" else name
}

/// <summary>
/// Generates the text of a string literal, of a property name or of an object key: letters and digits mixed with the
/// characters the printer has to escape and with characters outside ASCII. The text can be empty.
/// </summary>
let textGen : Gen<string> =
    Gen.frequency [ 3, Gen.alphaNum |> Gen.map string; 1, Gen.item textAtoms ]
    |> Gen.array (Range.linear 0 MaxTextAtoms)
    |> Gen.map (fun atoms -> String.Concat (atoms : string array))

/// <summary>
/// Generates a name that may or may not be written as a bare identifier: names that pass
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isSafeIdentifier(System.String)"/>, reserved words in any letter case,
/// and the texts of <see cref="P:FSharp.Azure.Cosmos.Tests.Models.SqlGenerators.textGen"/>.
/// </summary>
let nameGen : Gen<string> =
    let reservedWord = gen {
        let! word = Gen.item Keywords.reserved
        let! lower = Gen.bool

        return if lower then word.ToLowerInvariant () else word
    }

    Gen.frequency [ 4, safeNameGen; 1, reservedWord; 2, textGen ]

/// <summary>
/// Generates an <see cref="T:FSharp.Azure.Cosmos.Sql.Identifier"/> that the validator accepts: it matches the identifier
/// rule and is no reserved word.
/// </summary>
let identifierGen : Gen<Identifier> = safeNameGen |> Gen.map Identifier

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.ParameterName"/> that the validator accepts: <c>@</c> and a name
/// that matches the identifier rule.
/// </summary>
let parameterNameGen : Gen<ParameterName> =
    safeNameGen
    |> Gen.map (fun name -> ParameterName $"@%s{name}")

/// Generates a number that is finite: mostly around zero, sometimes at an edge of the range of a double.
let finiteNumberGen : Gen<float> =
    Gen.frequency [ 8, Gen.double (Range.linearFrom 0.0 -MaxNumber MaxNumber); 1, Gen.item edgeNumbers ]

/// <summary>
/// Generates an integer from -2^53 to 2^53, the integers that a JSON number holds exactly and that the validator
/// accepts as a literal.
/// </summary>
let exactIntegerGen : Gen<int64> = Gen.int64 (Range.linearFrom 0L -MaxExactInteger MaxExactInteger)

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.Literal"/> that the validator accepts: <c>null</c>,
/// <c>undefined</c>, a Boolean, an integer that a JSON number holds exactly, a finite number or a string.
/// </summary>
let literalGen : Gen<Literal> =
    Gen.choice [
        Gen.item [ Literal.Null; Literal.Undefined; Literal.Boolean true; Literal.Boolean false ]
        exactIntegerGen |> Gen.map Literal.Int
        finiteNumberGen |> Gen.map Literal.Float
        textGen |> Gen.map Literal.String
    ]

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.BinaryOperator"/>.
/// </summary>
let binaryOperatorGen : Gen<BinaryOperator> = Gen.item binaryOperators

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.UnaryOperator"/>.
/// </summary>
let unaryOperatorGen : Gen<UnaryOperator> = Gen.item unaryOperators

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.SortOrder"/>.
/// </summary>
let sortOrderGen : Gen<SortOrder> = Gen.item [ SortOrder.Ascending; SortOrder.Descending ]

/// A literal of any value: also an integer that a JSON number cannot hold and a number that is not finite.
let private arbitraryLiteralGen : Gen<Literal> =
    Gen.choice [
        literalGen
        Gen.int64 (Range.linearBounded ()) |> Gen.map Literal.Int
        Gen.item notFiniteNumbers |> Gen.map Literal.Float
    ]

let private identifierWith (scope : Scope) : Gen<Identifier> =
    if scope.Valid then
        identifierGen
    else
        nameGen |> Gen.map Identifier

let private parameterWith (scope : Scope) : Gen<ParameterName> =
    if scope.Valid then
        parameterNameGen
    else
        Gen.choice [ parameterNameGen; nameGen |> Gen.map ParameterName ]

let private literalWith (scope : Scope) : Gen<Literal> = if scope.Valid then literalGen else arbitraryLiteralGen

/// A TOP, OFFSET or LIMIT count.
let private countWith (scope : Scope) : Gen<SpecValue> =
    let lowest = if scope.Valid then 0L else -10L

    let literal =
        Gen.int64 (Range.linearFrom 0L lowest 1000L)
        |> Gen.map SpecValue.Literal

    if scope.Parameters then
        Gen.choice [ literal; parameterWith scope |> Gen.map SpecValue.Parameter ]
    else
        literal

let private appendSegment (parent : PathExpression voption) (segment : PathSegment) : PathExpression voption =
    match segment with
    | PathSegment.Name name -> ValueSome (PathExpression.Identifier (parent, name))
    | PathSegment.Index index -> ValueSome (PathExpression.Number (parent, index))
    | PathSegment.Key key -> ValueSome (PathExpression.String (parent, key))

/// The path of a FROM clause after the input name: no segment at all, or up to three of them.
let private pathWith (scope : Scope) : Gen<PathExpression voption> =
    Gen.choice [
        identifierWith scope |> Gen.map PathSegment.Name
        Gen.int64 (Range.linear 0L 9L) |> Gen.map PathSegment.Index
        textGen |> Gen.map PathSegment.Key
    ]
    |> Gen.array (Range.linear 0 MaxItems)
    |> Gen.map (Array.fold appendSegment ValueNone)

let private indexWith (target : ScalarExpression) (index : Literal) : ScalarExpression =
    ScalarExpression.MemberIndexer (target, ScalarExpression.Literal index)

/// <summary>
/// Generates a property path as a scalar expression: a property reference followed by up to three accesses by a
/// property name or by an index, which is what an <c>ORDER BY</c> item may be.
/// </summary>
let propertyPathGen : Gen<ScalarExpression> =
    let index =
        Gen.choice [ textGen |> Gen.map Literal.String; Gen.int64 (Range.linear 0L 9L) |> Gen.map Literal.Int ]

    Gen.map2
        (fun root indexes ->
            indexes
            |> Array.fold indexWith (ScalarExpression.PropertyRef root)
        )
        identifierGen
        (index |> Gen.array (Range.linear 0 MaxItems))

let private argumentCountGen (arity : Arity) : Gen<int> =
    match arity with
    | Arity.Exactly count -> Gen.constant count
    | Arity.Between (min, max) -> Gen.int32 (Range.linear min max)
    | Arity.AtLeast min -> Gen.int32 (Range.linear min (min + 2))
    | Arity.Unspecified -> Gen.int32 (Range.linear 0 MaxItems)

/// <summary>
/// A call of one of <paramref name="functions"/> with a number of arguments its arity accepts.
/// </summary>
let private builtInCallOf (functions : ImmutableArray<FunctionSpec>) (argument : Gen<ScalarExpression>) : Gen<ScalarExpression> =
    gen {
        let! spec = Gen.item functions
        let! count = argumentCountGen spec.Arity
        let! arguments = argument |> Gen.array (Range.singleton count)

        return ScalarExpression.FunctionCall (FunctionRef.BuiltIn spec.Name, EquatableArray.unsafeOfArray arguments)
    }

/// A call of any function, by any name and with any number of arguments.
let private arbitraryCallOf (argument : Gen<ScalarExpression>) : Gen<ScalarExpression> = gen {
    let! name = Gen.choice [ Gen.item Catalog.all |> Gen.map _.Name; nameGen ]
    let! func = Gen.item [ FunctionRef.BuiltIn name; FunctionRef.Udf name ]
    let! arguments = argument |> Gen.array (Range.linear 0 MaxItems)

    return ScalarExpression.FunctionCall (func, EquatableArray.unsafeOfArray arguments)
}

let private udfCallOf (argument : Gen<ScalarExpression>) : Gen<ScalarExpression> =
    Gen.map2
        (fun name arguments -> ScalarExpression.FunctionCall (FunctionRef.Udf name, EquatableArray.unsafeOfArray arguments))
        safeNameGen
        (argument |> Gen.array (Range.linear 0 MaxItems))

/// An expression without children.
let private leafWith (scope : Scope) : Gen<ScalarExpression> =
    let literal = literalWith scope |> Gen.map ScalarExpression.Literal

    let property = identifierWith scope |> Gen.map ScalarExpression.PropertyRef

    if scope.Parameters then
        Gen.choice [ literal; property; parameterWith scope |> Gen.map ScalarExpression.ParameterRef ]
    else
        Gen.choice [ literal; property ]

/// The forms a subquery takes inside a scalar expression.
let private subqueryForms : (SqlQuery -> ScalarExpression) list = [
    ScalarExpression.Exists
    ScalarExpression.Array
    ScalarExpression.All
    ScalarExpression.First
    ScalarExpression.Last
    ScalarExpression.Subquery
]

let rec private scalarWith (scope : Scope) : Gen<ScalarExpression> =
    Gen.sized (fun size ->
        if size <= 1 then
            leafWith scope
        else
            let child = scalarWith scope |> Gen.resize (size / 2)

            let children (smallest : int) =
                child
                |> Gen.array (Range.linear smallest MaxItems)
                |> Gen.map EquatableArray.unsafeOfArray

            // An IN list without elements is an error of the validator
            let haystack = children (if scope.Valid then 1 else 0)

            let property = Gen.map2 (fun key value -> struct (key, value)) textGen child

            let call =
                if scope.Valid then
                    Gen.choice [ builtInCallOf scalarFunctions child; udfCallOf child ]
                else
                    arbitraryCallOf child

            let subquery = queryWith scope true |> Gen.resize (size / 4)

            Gen.frequency [
                4, leafWith scope
                2, Gen.map2 (fun target index -> ScalarExpression.MemberIndexer (target, index)) child child
                2,
                Gen.map3
                    (fun operator left right -> ScalarExpression.Binary (operator, left, right))
                    binaryOperatorGen
                    child
                    child
                1, Gen.map2 (fun operator operand -> ScalarExpression.Unary (operator, operand)) unaryOperatorGen child
                1,
                Gen.map3
                    (fun condition whenTrue whenFalse -> ScalarExpression.Conditional (condition, whenTrue, whenFalse))
                    child
                    child
                    child
                1, Gen.map2 (fun left right -> ScalarExpression.Coalesce (left, right)) child child
                1, Gen.map3 (fun needle negated items -> ScalarExpression.In (needle, negated, items)) child Gen.bool haystack
                1,
                gen {
                    let! value = child
                    let! negated = Gen.bool
                    let! low = child
                    let! high = child

                    return ScalarExpression.Between (value, negated, low, high)
                }
                1,
                gen {
                    let! value = child
                    let! pattern = child
                    let! negated = Gen.bool
                    let! escape = Gen.voptionBalanced textGen

                    return ScalarExpression.Like (value, pattern, negated, escape)
                }
                2, call
                1, children 0 |> Gen.map ScalarExpression.ArrayCreate
                1,
                property
                |> Gen.array (Range.linear 0 MaxItems)
                |> Gen.map (fun properties -> ScalarExpression.ObjectCreate (EquatableArray.unsafeOfArray properties))
                1, Gen.map2 (fun form query -> form query) (Gen.item subqueryForms) subquery
            ]
    )

and private collectionWith (scope : Scope) : Gen<Collection> =
    Gen.sized (fun size ->
        let inputPath =
            Gen.map2 (fun input path -> Collection.InputPath (input, path)) (identifierWith scope) (pathWith scope)

        if size <= 1 then
            inputPath
        else
            Gen.frequency [
                4, inputPath
                1,
                queryWith scope true
                |> Gen.resize (size / 4)
                |> Gen.map Collection.Subquery
            ]
    )

and private collectionExpressionWith (scope : Scope) : Gen<CollectionExpression> =
    Gen.sized (fun size ->
        let aliased =
            Gen.map2
                (fun collection alias -> CollectionExpression.Aliased (collection, alias))
                (collectionWith scope)
                (Gen.voptionBalanced (identifierWith scope))

        let iterator =
            Gen.map2
                (fun alias collection -> CollectionExpression.ArrayIterator (alias, collection))
                (identifierWith scope)
                (collectionWith scope)

        if size <= 1 then
            Gen.choice [ aliased; iterator ]
        else
            let part = collectionExpressionWith scope |> Gen.resize (size / 2)

            Gen.frequency [
                3, aliased
                3, iterator
                1, Gen.map2 (fun left right -> CollectionExpression.Join (left, right)) part part
            ]
    )

/// The ORDER BY clause and whether it is an ORDER BY RANK clause.
and private orderByWith
    (scope : Scope)
    (isSubquery : bool)
    (scalar : Gen<ScalarExpression>)
    : Gen<struct (EquatableArray<OrderByItem> * bool)> =
    let itemsOf (smallest : int) (largest : int) (expression : Gen<ScalarExpression>) =
        Gen.map2
            (fun expression order -> { Expression = expression; Order = order })
            expression
            (Gen.voptionBalanced sortOrderGen)
        |> Gen.array (Range.linear smallest largest)
        |> Gen.map EquatableArray.unsafeOfArray

    let none = Gen.constant (struct (EquatableArray.empty<OrderByItem>, false))

    if not scope.Valid then
        Gen.map2 (fun items rank -> struct (items, rank)) (itemsOf 0 MaxItems scalar) Gen.bool
    elif isSubquery then
        // The validator accepts ORDER BY in a subquery; the emulator rejects it, so a valid tree has none there
        none
    else
        // An item is a property path or a call of a function allowed there
        let key =
            if orderByFunctions.IsEmpty then
                propertyPathGen
            else
                Gen.frequency [ 4, propertyPathGen; 1, builtInCallOf orderByFunctions scalar ]

        let plain =
            itemsOf 1 MaxItems key
            |> Gen.map (fun items -> struct (items, false))

        // A scoring function takes ordinary arguments. RRF fuses two or three calls of the other scoring functions and
        // may end with the weights, one number for each of them, as Microsoft Learn describes it
        let isFusion (spec : FunctionSpec) = String.Equals (spec.Name, "RRF", StringComparison.Ordinal)

        let scoring =
            rankFunctions
            |> Seq.filter (not << isFusion)
            |> ImmutableArray.CreateRange

        if scoring.IsEmpty then
            Gen.choice [ none; plain ]
        else
            let scoringCall = builtInCallOf scoring scalar

            let weights (count : int) =
                Gen.int64 (Range.linear 1L 10L)
                |> Gen.map (Literal.Int >> ScalarExpression.Literal)
                |> Gen.array (Range.singleton count)
                |> Gen.map (EquatableArray.unsafeOfArray >> ScalarExpression.ArrayCreate)

            let fusion = gen {
                let! calls = scoringCall |> Gen.array (Range.linear 2 3)
                let! ending = Gen.voptionBalanced (weights calls.Length)

                return
                    ScalarExpression.FunctionCall (
                        FunctionRef.BuiltIn "RRF",
                        EquatableArray.unsafeOfArray [|
                            yield! calls

                            match ending with
                            | ValueSome array -> yield array
                            | ValueNone -> ()
                        |]
                    )
            }

            let item =
                if rankFunctions |> Seq.exists isFusion then
                    Gen.frequency [ 3, scoringCall; 2, fusion ]
                else
                    scoringCall

            let rank =
                itemsOf 1 2 item
                |> Gen.map (fun items -> struct (items, true))

            Gen.frequency [ 3, none; 3, plain; 1, rank ]

and private queryWith (scope : Scope) (isSubquery : bool) : Gen<SqlQuery> =
    Gen.sized (fun size ->
        let scalar = scalarWith scope |> Gen.resize (size / 2)

        // A GROUP BY key holds no parameter, at any depth
        let key =
            scalarWith (
                if scope.Valid then
                    { scope with Parameters = false }
                else
                    scope
            )
            |> Gen.resize (size / 4)

        let selectItem =
            Gen.map2
                (fun expression alias -> { SelectItem.Expression = expression; Alias = alias })
                scalar
                (Gen.voptionBalanced (identifierWith scope))

        let value = scalar |> Gen.map SelectSpec.Value

        // A select list without items is an error of the validator
        let list =
            selectItem
            |> Gen.array (Range.linear (if scope.Valid then 1 else 0) MaxItems)
            |> Gen.map (fun items -> SelectSpec.List (EquatableArray.unsafeOfArray items))

        let star = Gen.constant SelectSpec.Star

        gen {
            let! from =
                collectionExpressionWith scope
                |> Gen.resize (size / 2)
                |> Gen.voptionBalanced

            let! select =
                match from with
                // SELECT * needs exactly one collection
                | ValueSome (CollectionExpression.Aliased _)
                | ValueSome (CollectionExpression.ArrayIterator _) -> Gen.choice [ star; value; list ]
                | _ when not scope.Valid -> Gen.choice [ star; value; list ]
                | _ -> Gen.choice [ value; list ]

            let! distinct = Gen.bool

            let! top =
                // TOP in a subquery is an error of the validator
                if scope.Valid && isSubquery then
                    Gen.constant ValueNone
                else
                    Gen.voptionBalanced (countWith scope)

            let! where = Gen.voptionBalanced scalar

            let! groupBy =
                key
                |> Gen.array (Range.linear 0 2)
                |> Gen.map EquatableArray.unsafeOfArray

            let! struct (orderBy, orderByRank) = orderByWith scope isSubquery scalar

            let! offsetLimit =
                // The validator accepts OFFSET LIMIT in a subquery; the emulator rejects it, so a valid tree has none there
                if scope.Valid && isSubquery then
                    Gen.constant ValueNone
                else
                    Gen.map2 (fun offset limit -> struct (offset, limit)) (countWith scope) (countWith scope)
                    |> Gen.voptionBalanced

            return {
                Select = select
                Distinct = distinct
                Top = top
                From = from
                Where = where
                GroupBy = groupBy
                OrderBy = orderBy
                OrderByRank = orderByRank
                OffsetLimit = offsetLimit
            }
        }
    )

/// A number that is finite and has a fraction, so that it is not written like an integer.
let private fractionGen : Gen<float> =
    Gen.int64 (Range.linearFrom 0L -1_000_000L 1_000_000L)
    |> Gen.map (fun whole -> float whole + 0.5)

let rec private constantWith (size : int) : Gen<ScalarExpression> =
    let literal =
        Gen.choice [
            Gen.item [ Literal.Null; Literal.Boolean true; Literal.Boolean false ]
            exactIntegerGen |> Gen.map Literal.Int
            fractionGen |> Gen.map Literal.Float
            textGen |> Gen.map Literal.String
        ]
        |> Gen.map ScalarExpression.Literal

    if size <= 1 then
        literal
    else
        let child = constantWith (size / 2)

        Gen.frequency [
            3, literal
            1,
            child
            |> Gen.array (Range.linear 0 MaxItems)
            |> Gen.map (fun items -> ScalarExpression.ArrayCreate (EquatableArray.unsafeOfArray items))
            1,
            Gen.map2 (fun key value -> struct (key, value)) textGen child
            |> Gen.array (Range.linear 0 MaxItems)
            |> Gen.map (fun properties -> ScalarExpression.ObjectCreate (EquatableArray.unsafeOfArray properties))
        ]

/// <summary>
/// Generates a constant that JSON can carry and that reads back as itself: a literal other than <c>undefined</c>, or an
/// array or object of such constants. Its numbers are integers or have a fraction, because a number without one is
/// written, and read back, as an integer.
/// </summary>
let constantGen : Gen<ScalarExpression> = Gen.sized constantWith

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/> that
/// <see cref="M:FSharp.Azure.Cosmos.Sql.SqlQueryModule.validate(FSharp.Azure.Cosmos.Sql.SqlQuery)"/> accepts wherever a
/// query takes any expression: in the select clause and in the <c>WHERE</c> clause. It may hold parameters and
/// subqueries.
/// </summary>
let scalarExpressionGen : Gen<ScalarExpression> = scalarWith validScope

/// <summary>
/// Generates a <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/> that
/// <see cref="M:FSharp.Azure.Cosmos.Sql.SqlQueryModule.validate(FSharp.Azure.Cosmos.Sql.SqlQuery)"/> accepts.
/// </summary>
let sqlQueryGen : Gen<SqlQuery> = queryWith validScope false

/// <summary>
/// Generates any <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/>, valid or not: names that are no
/// identifiers, numbers that are not finite, calls of unknown functions, empty <c>IN</c> lists.
/// </summary>
let arbitraryScalarExpressionGen : Gen<ScalarExpression> = scalarWith arbitraryScope

/// <summary>
/// Generates any <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/>, valid or not: also <c>TOP</c> in a subquery,
/// <c>SELECT *</c> without a collection, an empty select list, negative counts and sort keys of any form.
/// </summary>
let arbitrarySqlQueryGen : Gen<SqlQuery> = queryWith arbitraryScope false
