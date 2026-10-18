/// <summary>
/// The parser of the Azure Cosmos DB .NET SDK as an oracle for the printer: <see cref="parse"/> reads query text with
/// the lexer and the parser of <c>tests/Cosmos.Sql.Grammar.Generated</c>, the SDK's own generated sources, and converts
/// the parse tree into a <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/>.
/// <para>
/// The conversion follows the one of the SDK (<c>CstToAstVisitor</c> next to the grammar), with what the syntax tree of
/// this repository asks for:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A property access with a dot, <c>c.name</c>, becomes the same node as <c>c["name"]</c>: the tree has one form for
/// both.
/// </description></item>
/// <item><description>
/// A string literal is decoded completely, with <c>\n</c>, <c>\t</c> and <c>\uXXXX</c>; the SDK's conversion decodes
/// only <c>\"</c>, <c>\\</c> and <c>\/</c>.
/// </description></item>
/// <item><description>
/// A number is read with the invariant culture: an integer in the range of <see cref="T:System.Int64"/> becomes an
/// integer literal, any other number a double-precision one.
/// </description></item>
/// </list>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Sql.Grammar.SdkGrammar

open System
open System.Globalization
open System.IO
open System.Text
open Antlr4.Runtime
open Antlr4.Runtime.Tree

open FSharp.Azure.Cosmos.Sql

/// Text that the grammar accepts and the syntax tree of this repository cannot hold, such as a fraction after TOP.
exception private Unrepresentable of reason : string

/// Keeps the first syntax error of the lexer and of the parser, which would otherwise be written to the console.
[<Sealed>]
type private FirstError () =

    let mutable first : string voption = ValueNone

    /// The first error, with its line and column, if there was one.
    member _.First = first

    /// Records an error unless one is recorded already.
    member _.Record (line : int, column : int, message : string) =
        if first.IsNone then
            first <- ValueSome $"line %d{line}:%d{column} %s{message}"

    interface IAntlrErrorListener<int> with

        /// <inheritdoc />
        member this.SyntaxError
            (_ : TextWriter, _ : IRecognizer, _ : int, line : int, column : int, message : string, _ : RecognitionException)
            =
            this.Record (line, column, message)

    interface IAntlrErrorListener<IToken> with

        /// <inheritdoc />
        member this.SyntaxError
            (_ : TextWriter, _ : IRecognizer, _ : IToken, line : int, column : int, message : string, _ : RecognitionException)
            =
            this.Record (line, column, message)

let private isPresent (node : ITerminalNode | null) =
    match node with
    | null -> false
    | _ -> true

// The text between the quotes of a string literal, single or double, with every escape of the grammar decoded
let private stringOf (node : ITerminalNode) : string =
    let text = node.GetText ()
    let builder = StringBuilder text.Length
    let closing = text.Length - 1
    let mutable index = 1

    while index < closing do
        let character = text[index]

        if character <> '\\' then
            builder.Append character |> ignore
            index <- index + 1
        else
            match text[index + 1] with
            | 'b' -> builder.Append '\b' |> ignore
            | 'f' -> builder.Append '\f' |> ignore
            | 'n' -> builder.Append '\n' |> ignore
            | 'r' -> builder.Append '\r' |> ignore
            | 't' -> builder.Append '\t' |> ignore
            | 'u' ->
                let codeUnit =
                    Int32.Parse (text.AsSpan (index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                builder.Append (char codeUnit) |> ignore
                index <- index + 4
            // A quote, a backslash or a solidus stands for itself
            | escaped -> builder.Append escaped |> ignore

            index <- index + 2

    builder.ToString ()

let private numberOf (node : ITerminalNode) : Literal =
    let text = node.GetText ()
    let mutable integer = 0L

    // The negative zero stays a double: as an integer it would lose its sign, which the printer writes
    if
        not (text.Equals ("-0", StringComparison.Ordinal))
        && Int64.TryParse (text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, &integer)
    then
        Literal.Int integer
    else
        Literal.Float (Double.Parse (text, NumberStyles.Float, CultureInfo.InvariantCulture))

// The count of TOP, OFFSET or LIMIT: the grammar gives it as a number or as a parameter
let private countOf (number : ITerminalNode | null) (parameter : ITerminalNode | null) : SpecValue =
    match number with
    | null ->
        match parameter with
        | null -> raise (Unrepresentable "A count is neither a number nor a parameter.")
        | parameter -> SpecValue.Parameter (ParameterName (parameter.GetText ()))
    | number ->
        match numberOf number with
        | Literal.Int count -> SpecValue.Literal count
        | _ -> raise (Unrepresentable $"The count %s{number.GetText ()} is not an integer.")

let private identifierOf (context : sqlParser.IdentifierContext) = Identifier (context.GetText ())

let private optionalIdentifierOf (context : sqlParser.IdentifierContext | null) : Identifier voption =
    match context with
    | null -> ValueNone
    | context -> ValueSome (identifierOf context)

let private binaryOperatorOf (text : string) : BinaryOperator =
    match text.ToUpperInvariant () with
    | "+" -> BinaryOperator.Add
    | "AND" -> BinaryOperator.And
    | "&" -> BinaryOperator.BitwiseAnd
    | "|" -> BinaryOperator.BitwiseOr
    | "^" -> BinaryOperator.BitwiseXor
    | "/" -> BinaryOperator.Divide
    | "=" -> BinaryOperator.Equal
    | ">" -> BinaryOperator.GreaterThan
    | ">=" -> BinaryOperator.GreaterThanOrEqual
    | "<" -> BinaryOperator.LessThan
    | "<=" -> BinaryOperator.LessThanOrEqual
    | "%" -> BinaryOperator.Modulo
    | "*" -> BinaryOperator.Multiply
    | "!=" -> BinaryOperator.NotEqual
    | "OR" -> BinaryOperator.Or
    | "||" -> BinaryOperator.StringConcat
    | "-" -> BinaryOperator.Subtract
    | _ -> raise (Unrepresentable $"The binary operator %s{text} is unknown.")

let private unaryOperatorOf (text : string) : UnaryOperator =
    match text.ToUpperInvariant () with
    | "~" -> UnaryOperator.BitwiseNot
    | "-" -> UnaryOperator.Minus
    | "NOT" -> UnaryOperator.Not
    | "+" -> UnaryOperator.Plus
    | _ -> raise (Unrepresentable $"The unary operator %s{text} is unknown.")

let private sortOrderOf (context : sqlParser.Sort_orderContext | null) : SortOrder voption =
    match context with
    | null -> ValueNone
    | context when isPresent (context.K_DESC ()) -> ValueSome SortOrder.Descending
    | _ -> ValueSome SortOrder.Ascending

let private literalOf (context : sqlParser.LiteralContext) : Literal =
    if isPresent (context.STRING_LITERAL ()) then
        Literal.String (stringOf (context.STRING_LITERAL ()))
    elif isPresent (context.NUMERIC_LITERAL ()) then
        numberOf (context.NUMERIC_LITERAL ())
    elif isPresent (context.K_TRUE ()) then
        Literal.Boolean true
    elif isPresent (context.K_FALSE ()) then
        Literal.Boolean false
    elif isPresent (context.K_NULL ()) then
        Literal.Null
    else
        Literal.Undefined

let rec private pathOf (context : sqlParser.Path_expressionContext) : PathExpression voption =
    match context with
    | :? sqlParser.IdentifierPathExpressionContext as path ->
        ValueSome (PathExpression.Identifier (pathOf (path.path_expression ()), identifierOf (path.identifier ())))
    | :? sqlParser.NumberPathExpressionContext as path ->
        match numberOf (path.NUMERIC_LITERAL ()) with
        | Literal.Int index -> ValueSome (PathExpression.Number (pathOf (path.path_expression ()), index))
        | _ -> raise (Unrepresentable $"The path index %s{path.NUMERIC_LITERAL().GetText()} is not an integer.")
    | :? sqlParser.StringPathExpressionContext as path ->
        ValueSome (PathExpression.String (pathOf (path.path_expression ()), stringOf (path.STRING_LITERAL ())))
    // No path at all, or the empty alternative that ends every path
    | _ -> ValueNone

let rec private queryOf (context : sqlParser.Sql_queryContext) : SqlQuery =
    let select = context.select_clause ()
    let selection = select.selection ()

    let selectSpec =
        match selection.select_value_spec (), selection.select_list_spec () with
        | null, null -> SelectSpec.Star
        | null, list ->
            list.select_item ()
            |> Array.map (fun item -> {
                Expression = scalarOf (item.scalar_expression ())
                Alias = optionalIdentifierOf (item.identifier ())
            })
            |> EquatableArray.unsafeOfArray
            |> SelectSpec.List
        | value, _ -> SelectSpec.Value (scalarOf (value.scalar_expression ()))

    let top =
        match select.top_spec () with
        | null -> ValueNone
        | top -> ValueSome (countOf (top.NUMERIC_LITERAL ()) (top.PARAMETER ()))

    let from =
        match context.from_clause () with
        | null -> ValueNone
        | from -> ValueSome (collectionExpressionOf (from.collection_expression ()))

    let where =
        match context.where_clause () with
        | null -> ValueNone
        | where -> ValueSome (scalarOf (where.scalar_expression ()))

    let groupBy =
        match context.group_by_clause () with
        | null -> EquatableArray.empty
        | groupBy -> scalarListOf (groupBy.scalar_expression_list ())

    let struct (orderBy, rank) =
        match context.order_by_clause () with
        | null -> struct (EquatableArray.empty, false)
        | orderBy when isPresent (orderBy.K_RANK ()) ->
            let items =
                orderBy.score_expression_order_by_items().score_expression_order_by_item()
                |> Array.map (fun item -> {
                    Expression = functionCallOf (item.function_call_scalar_expression ())
                    Order = sortOrderOf (item.sort_order ())
                })

            struct (EquatableArray.unsafeOfArray items, true)
        | orderBy ->
            let items =
                orderBy.order_by_items().order_by_item()
                |> Array.map (fun item -> {
                    Expression = scalarOf (item.scalar_expression ())
                    Order = sortOrderOf (item.sort_order ())
                })

            struct (EquatableArray.unsafeOfArray items, false)

    let offsetLimit =
        match context.offset_limit_clause () with
        | null -> ValueNone
        | clause ->
            let offset = clause.offset_count ()
            let limit = clause.limit_count ()

            ValueSome (
                struct (countOf (offset.NUMERIC_LITERAL ()) (offset.PARAMETER ()),
                        countOf (limit.NUMERIC_LITERAL ()) (limit.PARAMETER ()))
            )

    {
        Select = selectSpec
        Distinct = isPresent (select.K_DISTINCT ())
        Top = top
        From = from
        Where = where
        GroupBy = groupBy
        OrderBy = orderBy
        OrderByRank = rank
        OffsetLimit = offsetLimit
    }

and private collectionExpressionOf (context : sqlParser.Collection_expressionContext) : CollectionExpression =
    match context with
    | :? sqlParser.AliasedCollectionExpressionContext as aliased ->
        CollectionExpression.Aliased (collectionOf (aliased.collection ()), optionalIdentifierOf (aliased.identifier ()))
    | :? sqlParser.ArrayIteratorCollectionExpressionContext as iterator ->
        CollectionExpression.ArrayIterator (identifierOf (iterator.identifier ()), collectionOf (iterator.collection ()))
    | :? sqlParser.JoinCollectionExpressionContext as join ->
        CollectionExpression.Join (
            collectionExpressionOf (join.collection_expression 0),
            collectionExpressionOf (join.collection_expression 1)
        )
    | other -> raise (Unrepresentable $"The collection expression %s{other.GetText ()} is unknown.")

and private collectionOf (context : sqlParser.CollectionContext) : Collection =
    match context with
    | :? sqlParser.InputPathCollectionContext as input ->
        Collection.InputPath (identifierOf (input.identifier ()), pathOf (input.path_expression ()))
    | :? sqlParser.SubqueryCollectionContext as subquery -> Collection.Subquery (queryOf (subquery.sql_query ()))
    | other -> raise (Unrepresentable $"The collection %s{other.GetText ()} is unknown.")

and private scalarListOf (context : sqlParser.Scalar_expression_listContext | null) : EquatableArray<ScalarExpression> =
    match context with
    | null -> EquatableArray.empty
    | context ->
        context.scalar_expression ()
        |> Array.map scalarOf
        |> EquatableArray.unsafeOfArray

and private scalarOf (context : sqlParser.Scalar_expressionContext) : ScalarExpression =
    match context with
    | :? sqlParser.ConditionalScalarExpressionContext as conditional ->
        ScalarExpression.Conditional (
            scalarOf (conditional.scalar_expression 0),
            scalarOf (conditional.scalar_expression 1),
            scalarOf (conditional.scalar_expression 2)
        )
    | :? sqlParser.CoalesceScalarExpressionContext as coalesce ->
        ScalarExpression.Coalesce (scalarOf (coalesce.scalar_expression 0), scalarOf (coalesce.scalar_expression 1))
    | :? sqlParser.BetweenScalarExpressionContext as between ->
        ScalarExpression.Between (
            binaryOf (between.binary_scalar_expression 0),
            isPresent (between.K_NOT ()),
            binaryOf (between.binary_scalar_expression 1),
            binaryOf (between.binary_scalar_expression 2)
        )
    | :? sqlParser.LogicalScalarExpressionContext as logical -> logicalOf (logical.logical_scalar_expression ())
    | other -> raise (Unrepresentable $"The scalar expression %s{other.GetText ()} is unknown.")

and private logicalOf (context : sqlParser.Logical_scalar_expressionContext) : ScalarExpression =
    match context.binary_scalar_expression (), context.in_scalar_expression (), context.like_scalar_expression () with
    | null, null, null ->
        // logical operator logical
        ScalarExpression.Binary (
            binaryOperatorOf (context.GetChild(1).GetText()),
            logicalOf (context.logical_scalar_expression 0),
            logicalOf (context.logical_scalar_expression 1)
        )
    | null, null, like ->
        let escape =
            match like.escape_expression () with
            | null -> ValueNone
            | escape -> ValueSome (stringOf (escape.STRING_LITERAL ()))

        ScalarExpression.Like (
            binaryOf (like.binary_scalar_expression 0),
            binaryOf (like.binary_scalar_expression 1),
            isPresent (like.K_NOT ()),
            escape
        )
    | null, inList, _ ->
        ScalarExpression.In (
            binaryOf (inList.binary_scalar_expression ()),
            isPresent (inList.K_NOT ()),
            scalarListOf (inList.scalar_expression_list ())
        )
    | binary, _, _ -> binaryOf binary

and private binaryOf (context : sqlParser.Binary_scalar_expressionContext) : ScalarExpression =
    match context.unary_scalar_expression () with
    | null ->
        // binary operator binary
        ScalarExpression.Binary (
            binaryOperatorOf (context.GetChild(1).GetText()),
            binaryOf (context.binary_scalar_expression 0),
            binaryOf (context.binary_scalar_expression 1)
        )
    | unary -> unaryOf unary

and private unaryOf (context : sqlParser.Unary_scalar_expressionContext) : ScalarExpression =
    match context.primary_expression () with
    | null ->
        ScalarExpression.Unary (
            unaryOperatorOf (context.unary_operator().GetText()),
            unaryOf (context.unary_scalar_expression ())
        )
    | primary -> primaryOf primary

and private functionCallOf (context : sqlParser.Function_call_scalar_expressionContext) : ScalarExpression =
    let name =
        match context.identifier () with
        // LEFT and RIGHT are keywords of the grammar and functions all the same
        | null when isPresent (context.K_LEFT ()) -> context.K_LEFT().GetText()
        | null -> context.K_RIGHT().GetText()
        | identifier -> identifier.GetText ()

    let func =
        if isPresent (context.K_UDF ()) then
            FunctionRef.Udf name
        else
            FunctionRef.BuiltIn name

    ScalarExpression.FunctionCall (func, scalarListOf (context.scalar_expression_list ()))

and private primaryOf (context : sqlParser.Primary_expressionContext) : ScalarExpression =
    match context with
    | :? sqlParser.PropertyRefScalarExpressionBaseContext as reference ->
        ScalarExpression.PropertyRef (identifierOf (reference.identifier ()))
    | :? sqlParser.ParameterRefScalarExpressionContext as parameter ->
        ScalarExpression.ParameterRef (ParameterName (parameter.PARAMETER().GetText()))
    | :? sqlParser.LiteralScalarExpressionContext as literal -> ScalarExpression.Literal (literalOf (literal.literal ()))
    | :? sqlParser.ArrayCreateScalarExpressionContext as array ->
        ScalarExpression.ArrayCreate (scalarListOf (array.scalar_expression_list ()))
    | :? sqlParser.ObjectCreateScalarExpressionContext as object ->
        match object.object_property_list () with
        | null -> ScalarExpression.ObjectCreate EquatableArray.empty
        | properties ->
            properties.object_property ()
            |> Array.map (fun property ->
                struct (stringOf (property.STRING_LITERAL ()), scalarOf (property.scalar_expression ()))
            )
            |> EquatableArray.unsafeOfArray
            |> ScalarExpression.ObjectCreate
    | :? sqlParser.ParenthesizedScalarExperessionContext as parenthesized -> scalarOf (parenthesized.scalar_expression ())
    | :? sqlParser.SubqueryScalarExpressionContext as subquery -> ScalarExpression.Subquery (queryOf (subquery.sql_query ()))
    | :? sqlParser.PropertyRefScalarExpressionRecursiveContext as reference ->
        // c.name is the node of c["name"]
        ScalarExpression.MemberIndexer (
            primaryOf (reference.primary_expression ()),
            ScalarExpression.Literal (Literal.String (reference.identifier().GetText()))
        )
    | :? sqlParser.MemberIndexerScalarExpressionContext as indexer ->
        ScalarExpression.MemberIndexer (primaryOf (indexer.primary_expression ()), scalarOf (indexer.scalar_expression ()))
    | :? sqlParser.ExistsScalarExpressionContext as exists -> ScalarExpression.Exists (queryOf (exists.sql_query ()))
    | :? sqlParser.ArrayScalarExpressionContext as array -> ScalarExpression.Array (queryOf (array.sql_query ()))
    | :? sqlParser.AllScalarExpressionContext as all -> ScalarExpression.All (queryOf (all.sql_query ()))
    | :? sqlParser.FirstScalarExpressionContext as first -> ScalarExpression.First (queryOf (first.sql_query ()))
    | :? sqlParser.LastScalarExpressionContext as last -> ScalarExpression.Last (queryOf (last.sql_query ()))
    | :? sqlParser.FunctionCallScalarExpressionContext as call -> functionCallOf (call.function_call_scalar_expression ())
    | other -> raise (Unrepresentable $"The primary expression %s{other.GetText ()} is unknown.")

/// <summary>
/// Reads <paramref name="text"/> as a query with the SDK's parser.
/// </summary>
/// <param name="text">The query text.</param>
/// <returns>
/// The syntax tree of the query; or the first syntax error with its line and column, or what the text holds that the
/// syntax tree of this repository cannot.
/// </returns>
let parse (text : string) : Result<SqlQuery, string> =
    let errors = FirstError ()
    let lexer = sqlLexer (AntlrInputStream text)
    lexer.RemoveErrorListeners ()
    lexer.AddErrorListener errors
    let parser = sqlParser (CommonTokenStream lexer)
    parser.RemoveErrorListeners ()
    parser.AddErrorListener errors
    let program = parser.program ()

    match errors.First with
    | ValueSome error -> Error error
    | ValueNone ->
        try
            Ok (queryOf (program.sql_query ()))
        with Unrepresentable reason ->
            Error reason
