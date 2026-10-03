// The syntax tree of the Azure Cosmos DB NoSQL query language. One union case or record per non-terminal of the SDK
// grammar sql.g4, named after the SDK's SqlObjects classes, so SDK baselines, parser baselines and the port of the SDK's
// offline engine line up with it one to one. Derived from Azure/azure-cosmos-dotnet-v3 3.62.0 (MIT), see
// THIRD-PARTY-NOTICES.md.
namespace FSharp.Azure.Cosmos.Sql

/// <summary>
/// An identifier of the query language: a collection alias, an input name, a select-item alias or a path segment
/// written with a dot, stored without any quoting.
/// <para>
/// Mirrors <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlIdentifier.cs">SqlIdentifier</a>.
/// The validator checks every identifier against the pattern <c>[A-Za-z_][A-Za-z_0-9]*</c> and the reserved words in
/// <see cref="P:FSharp.Azure.Cosmos.Sql.Keywords.reserved"/>.
/// </para>
/// </summary>
[<Struct>]
type Identifier =
    /// An identifier with the given text.
    | Identifier of string

    /// The text of the identifier.
    member this.Value =
        let (Identifier value) = this
        value

/// <summary>
/// The name of a query parameter, including its leading <c>@</c>, as the SDK's
/// <a href="https://learn.microsoft.com/dotnet/api/microsoft.azure.cosmos.querydefinition.withparameter">QueryDefinition.WithParameter</a>
/// expects it.
/// <para>
/// Mirrors <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlParameter.cs">SqlParameter</a>.
/// The validator checks every name against the pattern <c>@[A-Za-z_][A-Za-z_0-9]*</c>.
/// </para>
/// </summary>
[<Struct>]
type ParameterName =
    /// <summary>
    /// A parameter name with the given text, including the leading <c>@</c>.
    /// </summary>
    | ParameterName of string

    /// <summary>
    /// The text of the name, including the leading <c>@</c>.
    /// </summary>
    member this.Value =
        let (ParameterName value) = this
        value

/// <summary>
/// A literal value of the <c>literal</c> rule of the grammar.
/// <para>
/// Numbers are split the way the SDK's printer writes them: integers are written in full and double-precision numbers
/// in the round-trip format, so a double-precision number without a fractional part is written without one, as the
/// SDK does. Decimals are never literals; the translator passes them as parameters.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
type Literal =
    /// <summary>
    /// The literal <c>true</c> or <c>false</c>.
    /// </summary>
    | Boolean of bool
    /// <summary>The literal <c>null</c>.</summary>
    | Null
    /// <summary>The literal <c>undefined</c>.</summary>
    | Undefined
    /// <summary>
    /// An integer. The validator rejects a value outside -2^53..2^53, which a JSON number cannot hold exactly, unless
    /// <see cref="P:FSharp.Azure.Cosmos.Sql.ValidationOptions.AllowLossyInt64"/> is set; parameters are never range checked.
    /// </summary>
    | Int of int64
    /// <summary>
    /// A double-precision number. The validator rejects <see cref="F:System.Double.NaN"/> and the infinities, which have
    /// no literal form.
    /// </summary>
    | Float of float
    /// A string, written in double quotes with the escaping of the SDK's printer.
    | String of string

/// <summary>
/// The binary operators of the grammar: the seventeen kinds of the SDK's
/// <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlBinaryScalarOperatorKind.cs">SqlBinaryScalarOperatorKind</a>
/// except <c>??</c>, which <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/> represents with a case of its own.
/// <para>
/// The grammar has no shift operators; shifts are the functions <c>INTBITLEFTSHIFT</c> and <c>INTBITRIGHTSHIFT</c>.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
type BinaryOperator =
    /// <summary><c>+</c></summary>
    | Add
    /// <summary><c>AND</c></summary>
    | And
    /// <summary><c>&amp;</c></summary>
    | BitwiseAnd
    /// <summary><c>|</c></summary>
    | BitwiseOr
    /// <summary><c>^</c></summary>
    | BitwiseXor
    /// <summary><c>/</c></summary>
    | Divide
    /// <summary><c>=</c></summary>
    | Equal
    /// <summary><c>&gt;</c></summary>
    | GreaterThan
    /// <summary><c>&gt;=</c></summary>
    | GreaterThanOrEqual
    /// <summary><c>&lt;</c></summary>
    | LessThan
    /// <summary><c>&lt;=</c></summary>
    | LessThanOrEqual
    /// <summary><c>%</c></summary>
    | Modulo
    /// <summary><c>*</c></summary>
    | Multiply
    /// <summary><c>!=</c></summary>
    | NotEqual
    /// <summary><c>OR</c></summary>
    | Or
    /// <summary><c>||</c></summary>
    | StringConcat
    /// <summary><c>-</c></summary>
    | Subtract

/// <summary>
/// The unary operators of the grammar, the four kinds of the SDK's
/// <a href="https://github.com/Azure/azure-cosmos-dotnet-v3/blob/3.62.0/Microsoft.Azure.Cosmos/src/SqlObjects/SqlUnaryScalarOperatorKind.cs">SqlUnaryScalarOperatorKind</a>.
/// </summary>
[<RequireQualifiedAccess>]
type UnaryOperator =
    /// <summary><c>~</c></summary>
    | BitwiseNot
    /// <summary><c>NOT</c></summary>
    | Not
    /// <summary><c>-</c></summary>
    | Minus
    /// <summary><c>+</c></summary>
    | Plus

/// <summary>
/// The sort order of an <c>ORDER BY</c> item, the <c>sort_order</c> rule of the grammar.
/// </summary>
[<RequireQualifiedAccess>]
type SortOrder =
    /// <summary><c>ASC</c></summary>
    | Ascending
    /// <summary><c>DESC</c></summary>
    | Descending

/// <summary>
/// The function that a function call in a <see cref="T:FSharp.Azure.Cosmos.Sql.ScalarExpression"/> calls.
/// </summary>
[<RequireQualifiedAccess>]
type FunctionRef =
    /// <summary>
    /// A built-in function, looked up case-insensitively in <see cref="P:FSharp.Azure.Cosmos.Sql.Catalog.byName"/>.
    /// </summary>
    | BuiltIn of name : string
    /// <summary>
    /// A user-defined function, written with the <c>udf.</c> prefix.
    /// </summary>
    | Udf of name : string

    /// <summary>
    /// The name of the function, without the <c>udf.</c> prefix.
    /// </summary>
    member this.Name =
        match this with
        | BuiltIn name
        | Udf name -> name

/// <summary>
/// The count of <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c>, which the grammar allows as an integer literal or a
/// parameter only (<c>top_spec</c>, <c>offset_count</c>, <c>limit_count</c>).
/// </summary>
[<RequireQualifiedAccess>]
type SpecValue =
    /// An integer literal; the validator rejects a negative one.
    | Literal of int64
    /// A parameter.
    | Parameter of ParameterName

/// <summary>
/// The path after the input name of an input-path collection, such as <c>.tags</c> in <c>FROM t IN c.tags</c>; the
/// <c>path_expression</c> rule of the grammar. Each segment holds the path before it.
/// </summary>
[<RequireQualifiedAccess>]
type PathExpression =
    /// <summary>
    /// A segment written with a dot, <c>parent.name</c>.
    /// </summary>
    | Identifier of parent : PathExpression voption * name : Identifier
    /// <summary>
    /// An array index, <c>parent[index]</c>.
    /// </summary>
    | Number of parent : PathExpression voption * index : int64
    /// <summary>
    /// A property name in brackets, <c>parent["name"]</c>, which is how the translator always writes it.
    /// </summary>
    | String of parent : PathExpression voption * name : string

/// <summary>
/// A scalar expression, the <c>scalar_expression</c> rule of the grammar with every alternative of
/// <c>primary_expression</c>.
/// <para>
/// There is no case for raw SQL text: text that has to become part of a query is parsed into these cases. A member
/// access <c>c.name</c> is represented as a member indexer with a string literal index, which the printer writes as
/// <c>c["name"]</c>, so reserved words such as <c>value</c> and arbitrary property names need no special treatment.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
type ScalarExpression =
    /// A literal value.
    | Literal of Literal
    /// A reference to a query parameter.
    | ParameterRef of ParameterName
    /// <summary>
    /// A bare identifier, such as the collection alias <c>c</c>.
    /// </summary>
    | PropertyRef of Identifier
    /// <summary>
    /// A member or element access, <c>target[index]</c>: <c>c["name"]</c> with a string literal index, <c>arr[0]</c>
    /// with an integer one.
    /// </summary>
    | MemberIndexer of target : ScalarExpression * index : ScalarExpression
    /// A binary operation, written in parentheses.
    | Binary of operator : BinaryOperator * left : ScalarExpression * right : ScalarExpression
    /// A unary operation, written in parentheses.
    | Unary of operator : UnaryOperator * operand : ScalarExpression
    /// <summary>
    /// The ternary operator <c>(condition ? whenTrue : whenFalse)</c>.
    /// </summary>
    | Conditional of condition : ScalarExpression * whenTrue : ScalarExpression * whenFalse : ScalarExpression
    /// <summary>
    /// The operator <c>(left ?? right)</c>, which yields <paramref name="right"/> when <paramref name="left"/> is
    /// undefined.
    /// </summary>
    | Coalesce of left : ScalarExpression * right : ScalarExpression
    /// <summary>
    /// <c>(needle IN (haystack))</c> or, when <paramref name="negated"/>, <c>(needle NOT IN (haystack))</c>. The grammar
    /// and the validator require at least one element in <paramref name="haystack"/>.
    /// </summary>
    | In of needle : ScalarExpression * negated : bool * haystack : EquatableArray<ScalarExpression>
    /// <summary>
    /// <c>(value BETWEEN low AND high)</c> or, when <paramref name="negated"/>, <c>(value NOT BETWEEN low AND high)</c>.
    /// </summary>
    | Between of value : ScalarExpression * negated : bool * low : ScalarExpression * high : ScalarExpression
    /// <summary>
    /// <c>(value LIKE pattern)</c> or, when <paramref name="negated"/>, <c>(value NOT LIKE pattern)</c>, optionally
    /// followed by <c>ESCAPE "escape"</c>.
    /// </summary>
    | Like of value : ScalarExpression * pattern : ScalarExpression * negated : bool * escape : string voption
    /// <summary>
    /// A call of a built-in or user-defined function. <see cref="M:FSharp.Azure.Cosmos.Sql.Catalog.call(System.String,System.Collections.Immutable.ImmutableArray{FSharp.Azure.Cosmos.Sql.ScalarExpression})"/>
    /// builds calls of built-in functions after checking their arity.
    /// </summary>
    | FunctionCall of func : FunctionRef * arguments : EquatableArray<ScalarExpression>
    /// <summary>An array literal, <c>[a, b]</c>.</summary>
    | ArrayCreate of items : EquatableArray<ScalarExpression>
    /// <summary>
    /// An object literal, <c>{"a": x, "b": y}</c>. Every key is a string, so the grammar rule that object keys are
    /// string literals holds by construction.
    /// </summary>
    | ObjectCreate of properties : EquatableArray<struct (string * ScalarExpression)>
    /// <summary><c>EXISTS(subquery)</c>.</summary>
    | Exists of SqlQuery
    /// <summary><c>ARRAY(subquery)</c>.</summary>
    | Array of SqlQuery
    /// <summary><c>ALL(subquery)</c>.</summary>
    | All of SqlQuery
    /// <summary><c>FIRST(subquery)</c>.</summary>
    | First of SqlQuery
    /// <summary><c>LAST(subquery)</c>.</summary>
    | Last of SqlQuery
    /// <summary>
    /// A scalar subquery, <c>(subquery)</c>.
    /// </summary>
    | Subquery of SqlQuery

/// <summary>
/// A query, the <c>sql_query</c> rule of the grammar, with the select clause flattened into it. The printer writes the
/// clauses in the order of the grammar:
/// <c>SELECT [DISTINCT] [TOP n] spec FROM … WHERE … GROUP BY … ORDER BY [RANK] … OFFSET n LIMIT m</c>.
/// </summary>
and SqlQuery = {
    /// What the query projects.
    Select : SelectSpec
    /// <summary>
    /// Whether the projection is <c>SELECT DISTINCT</c>.
    /// </summary>
    Distinct : bool
    /// <summary>
    /// The count of <c>TOP</c>, if any.
    /// </summary>
    Top : SpecValue voption
    /// <summary>
    /// The <c>FROM</c> clause, if any.
    /// </summary>
    From : CollectionExpression voption
    /// <summary>
    /// The <c>WHERE</c> predicate, if any.
    /// </summary>
    Where : ScalarExpression voption
    /// <summary>
    /// The <c>GROUP BY</c> keys; empty for no <c>GROUP BY</c> clause.
    /// </summary>
    GroupBy : EquatableArray<ScalarExpression>
    /// <summary>
    /// The <c>ORDER BY</c> items; empty for no <c>ORDER BY</c> clause.
    /// </summary>
    OrderBy : EquatableArray<OrderByItem>
    /// <summary>
    /// Whether the <c>ORDER BY</c> clause is <c>ORDER BY RANK</c>, which applies to all of its items, so plain and rank
    /// items cannot be mixed.
    /// </summary>
    OrderByRank : bool
    /// <summary>
    /// The counts of <c>OFFSET</c> and <c>LIMIT</c>, which the grammar only allows together.
    /// </summary>
    OffsetLimit : struct (SpecValue * SpecValue) voption
}

/// <summary>
/// The projection of a query, the <c>selection</c> rule of the grammar.
/// </summary>
and [<RequireQualifiedAccess>] SelectSpec =
    /// <summary>
    /// <c>SELECT *</c>, valid with exactly one collection in the <c>FROM</c> clause.
    /// </summary>
    | Star
    /// <summary><c>SELECT VALUE expression</c>.</summary>
    | Value of ScalarExpression
    /// <summary>
    /// <c>SELECT a, b AS x</c>; at least one item.
    /// </summary>
    | List of EquatableArray<SelectItem>

/// <summary>
/// An item of a select list, <c>expression [AS alias]</c>.
/// </summary>
and SelectItem = {
    /// The projected expression.
    Expression : ScalarExpression
    /// The name of the projected property, if given.
    Alias : Identifier voption
}

/// <summary>
/// A source of the <c>FROM</c> clause, the <c>collection_expression</c> rule of the grammar.
/// </summary>
and [<RequireQualifiedAccess>] CollectionExpression =
    /// <summary>
    /// A collection with an optional alias, <c>c</c>, <c>c AS d</c> or <c>(subquery) AS x</c>.
    /// </summary>
    | Aliased of collection : Collection * alias : Identifier voption
    /// <summary>
    /// An iteration over an array, <c>t IN c.tags</c>.
    /// </summary>
    | ArrayIterator of alias : Identifier * collection : Collection
    /// <summary>
    /// A self join with an array of the same item, <c>left JOIN right</c>.
    /// </summary>
    | Join of left : CollectionExpression * right : CollectionExpression

/// <summary>
/// A collection, the <c>collection</c> rule of the grammar.
/// </summary>
and [<RequireQualifiedAccess>] Collection =
    /// <summary>
    /// An input name with an optional path, <c>c</c> or <c>c.tags</c>.
    /// </summary>
    | InputPath of input : Identifier * path : PathExpression voption
    /// <summary>A subquery, <c>(SELECT …)</c>.</summary>
    | Subquery of SqlQuery

/// <summary>
/// An item of an <c>ORDER BY</c> clause, <c>expression [ASC|DESC]</c>.
/// </summary>
and OrderByItem = {
    /// The sort key.
    Expression : ScalarExpression
    /// The sort order, if written; the service sorts ascending without one.
    Order : SortOrder voption
}
