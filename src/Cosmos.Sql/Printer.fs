// The printing rules follow SqlObjectTextSerializer of Azure/azure-cosmos-dotnet-v3 3.62.0 (MIT, see
// THIRD-PARTY-NOTICES.md) without pretty printing, so the output matches the SDK's baselines once whitespace is
// normalized. The whitespace itself differs in two places, both SDK artefacts: the SDK writes "NOT  LIKE" with two
// spaces and a space after the GROUP BY clause, which doubles the space before ORDER BY or trails the text; this printer
// writes single spaces only.
namespace FSharp.Azure.Cosmos.Sql

open System
open System.Collections.Immutable
open System.Globalization
open System.Text

/// How the printer writes an access to a property with a string name.
[<RequireQualifiedAccess>]
type PropertyStyle =
    /// <summary>
    /// Always with brackets, <c>c["name"]</c>, as the SDK's LINQ provider does: safe for reserved words such as
    /// <c>value</c> and for any property name. The default.
    /// </summary>
    | Brackets
    /// <summary>
    /// With a dot, <c>c.name</c>, when the name passes
    /// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isSafeIdentifier(System.String)"/>, and with brackets otherwise.
    /// </summary>
    | DotWhenSafe

/// <summary>
/// Writes the syntax tree as query text, deterministically and independently of the current culture.
/// <para>
/// The rules are those of the SDK's printer: aliases are written bare; every binary, unary, conditional, coalesce,
/// <c>IN</c>, <c>BETWEEN</c> and <c>LIKE</c> expression is written in parentheses, so the text never depends on operator
/// precedence; strings are written in double quotes with <c>\"</c>, <c>\\</c>, <c>\b</c>, <c>\f</c>, <c>\n</c>,
/// <c>\r</c>, <c>\t</c> and <c>\uXXXX</c> for the other control characters, while <c>/</c> is written as is like the
/// SDK does; integers are written in full, other numbers in the round-trip format of the invariant culture;
/// <c>true</c>, <c>false</c>, <c>null</c> and <c>undefined</c> in lower case; user-defined functions with the
/// <c>udf.</c> prefix.
/// </para>
/// <para>
/// Printing is total: every tree is written, valid or not. There are two modes: the parameterized mode of
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Printer.print(FSharp.Azure.Cosmos.Sql.PropertyStyle,FSharp.Azure.Cosmos.Sql.SqlQuery)"/>
/// writes parameters as <c>@name</c>, and the inline mode of
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Printer.printInline(FSharp.Azure.Cosmos.Sql.PropertyStyle,Microsoft.FSharp.Core.FSharpFunc{FSharp.Azure.Cosmos.Sql.ParameterName,Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Sql.ScalarExpression}},FSharp.Azure.Cosmos.Sql.SqlQuery)"/>
/// writes their values as literals, for APIs that take query text without parameters, such as the filter predicate of
/// a patch, and for diagnostics.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module Printer =

    let private hexDigit (value : int) =
        if value < 10 then
            char (int '0' + value)
        else
            char (int 'A' + value - 10)

    /// Writes a string literal with the escaping of the SDK's printer.
    let private writeString (builder : StringBuilder) (text : string) =
        builder.Append '"' |> ignore

        for character in text do
            match character with
            | '\\' -> builder.Append "\\\\" |> ignore
            | '"' -> builder.Append "\\\"" |> ignore
            | '\b' -> builder.Append "\\b" |> ignore
            | '\f' -> builder.Append "\\f" |> ignore
            | '\n' -> builder.Append "\\n" |> ignore
            | '\r' -> builder.Append "\\r" |> ignore
            | '\t' -> builder.Append "\\t" |> ignore
            | control when control < ' ' ->
                let code = int control

                builder
                    .Append("\\u")
                    .Append(hexDigit ((code >>> 12) &&& 0xF))
                    .Append(hexDigit ((code >>> 8) &&& 0xF))
                    .Append(hexDigit ((code >>> 4) &&& 0xF))
                    .Append(hexDigit (code &&& 0xF))
                |> ignore
            | other -> builder.Append other |> ignore

        builder.Append '"' |> ignore

    let private writeInteger (builder : StringBuilder) (value : int64) =
        builder.Append (value.ToString (CultureInfo.InvariantCulture))
        |> ignore

    let private writeLiteral (builder : StringBuilder) (literal : Literal) =
        match literal with
        | Literal.Boolean true -> builder.Append "true" |> ignore
        | Literal.Boolean false -> builder.Append "false" |> ignore
        | Literal.Null -> builder.Append "null" |> ignore
        | Literal.Undefined -> builder.Append "undefined" |> ignore
        | Literal.Int value -> writeInteger builder value
        | Literal.Float value ->
            builder.Append (value.ToString ("R", CultureInfo.InvariantCulture))
            |> ignore
        | Literal.String value -> writeString builder value

    /// <summary>
    /// Writes a property name after its target: <c>.name</c> or <c>["name"]</c>, depending on the style.
    /// </summary>
    let private writePropertyName (style : PropertyStyle) (builder : StringBuilder) (name : string) =
        match style with
        | PropertyStyle.DotWhenSafe when Keywords.isSafeIdentifier name -> builder.Append('.').Append name |> ignore
        | PropertyStyle.DotWhenSafe
        | PropertyStyle.Brackets ->
            builder.Append '[' |> ignore
            writeString builder name
            builder.Append ']' |> ignore

    let private binaryOperator (operator : BinaryOperator) =
        match operator with
        | BinaryOperator.Add -> "+"
        | BinaryOperator.And -> "AND"
        | BinaryOperator.BitwiseAnd -> "&"
        | BinaryOperator.BitwiseOr -> "|"
        | BinaryOperator.BitwiseXor -> "^"
        | BinaryOperator.Divide -> "/"
        | BinaryOperator.Equal -> "="
        | BinaryOperator.GreaterThan -> ">"
        | BinaryOperator.GreaterThanOrEqual -> ">="
        | BinaryOperator.LessThan -> "<"
        | BinaryOperator.LessThanOrEqual -> "<="
        | BinaryOperator.Modulo -> "%"
        | BinaryOperator.Multiply -> "*"
        | BinaryOperator.NotEqual -> "!="
        | BinaryOperator.Or -> "OR"
        | BinaryOperator.StringConcat -> "||"
        | BinaryOperator.Subtract -> "-"

    let private unaryOperator (operator : UnaryOperator) =
        match operator with
        | UnaryOperator.BitwiseNot -> "~"
        | UnaryOperator.Not -> "NOT"
        | UnaryOperator.Minus -> "-"
        | UnaryOperator.Plus -> "+"

    let private writeCount (builder : StringBuilder) (count : SpecValue) =
        match count with
        | SpecValue.Literal value -> writeInteger builder value
        | SpecValue.Parameter name -> builder.Append name.Value |> ignore

    /// Writes the items with the separator between them.
    let private writeSeparated (builder : StringBuilder) (separator : string) (write : 'T -> unit) (items : EquatableArray<'T>) =
        for index in 0 .. items.Length - 1 do
            if index > 0 then
                builder.Append separator |> ignore

            write items[index]

    let rec private writePath (style : PropertyStyle) (builder : StringBuilder) (path : PathExpression) =
        let writeParent parent =
            match parent with
            | ValueSome parent -> writePath style builder parent
            | ValueNone -> ()

        match path with
        | PathExpression.Identifier (parent, name) ->
            writeParent parent
            builder.Append('.').Append name.Value |> ignore
        | PathExpression.Number (parent, index) ->
            writeParent parent
            builder.Append '[' |> ignore
            writeInteger builder index
            builder.Append ']' |> ignore
        | PathExpression.String (parent, name) ->
            writeParent parent
            writePropertyName style builder name

    let rec private writeScalar (style : PropertyStyle) (builder : StringBuilder) (expression : ScalarExpression) =
        let scalar = writeScalar style builder
        let append (text : string) = builder.Append text |> ignore

        let subquery (keyword : string) query =
            append keyword
            append "("
            writeQuery style builder query
            append ")"

        match expression with
        | ScalarExpression.Literal literal -> writeLiteral builder literal
        | ScalarExpression.ParameterRef name -> append name.Value
        | ScalarExpression.PropertyRef identifier -> append identifier.Value
        | ScalarExpression.MemberIndexer (target, ScalarExpression.Literal (Literal.String name)) ->
            scalar target
            writePropertyName style builder name
        | ScalarExpression.MemberIndexer (target, index) ->
            scalar target
            append "["
            scalar index
            append "]"
        | ScalarExpression.Binary (operator, left, right) ->
            append "("
            scalar left
            append " "
            append (binaryOperator operator)
            append " "
            scalar right
            append ")"
        | ScalarExpression.Unary (operator, operand) ->
            append "("
            append (unaryOperator operator)
            append " "
            scalar operand
            append ")"
        | ScalarExpression.Conditional (condition, whenTrue, whenFalse) ->
            append "("
            scalar condition
            append " ? "
            scalar whenTrue
            append " : "
            scalar whenFalse
            append ")"
        | ScalarExpression.Coalesce (left, right) ->
            append "("
            scalar left
            append " ?? "
            scalar right
            append ")"
        | ScalarExpression.In (needle, negated, haystack) ->
            append "("
            scalar needle
            append (if negated then " NOT IN (" else " IN (")
            haystack |> writeSeparated builder ", " scalar
            append "))"
        | ScalarExpression.Between (value, negated, low, high) ->
            append "("
            scalar value
            append (if negated then " NOT BETWEEN " else " BETWEEN ")
            scalar low
            append " AND "
            scalar high
            append ")"
        | ScalarExpression.Like (value, pattern, negated, escape) ->
            append "("
            scalar value
            append (if negated then " NOT LIKE " else " LIKE ")
            scalar pattern

            match escape with
            | ValueSome escape ->
                append " ESCAPE "
                writeString builder escape
            | ValueNone -> ()

            append ")"
        | ScalarExpression.FunctionCall (func, arguments) ->
            match func with
            | FunctionRef.BuiltIn name -> append name
            | FunctionRef.Udf name ->
                append "udf."
                append name

            append "("
            arguments |> writeSeparated builder ", " scalar
            append ")"
        | ScalarExpression.ArrayCreate items ->
            append "["
            items |> writeSeparated builder ", " scalar
            append "]"
        | ScalarExpression.ObjectCreate properties ->
            append "{"

            properties
            |> writeSeparated
                builder
                ", "
                (fun struct (name, value) ->
                    // The SDK writes property names without escaping them; this printer escapes them like every
                    // other string, which yields the same text for every name that does not need escaping
                    writeString builder name
                    append ": "
                    scalar value
                )

            append "}"
        | ScalarExpression.Exists query -> subquery "EXISTS" query
        | ScalarExpression.Array query -> subquery "ARRAY" query
        | ScalarExpression.All query -> subquery "ALL" query
        | ScalarExpression.First query -> subquery "FIRST" query
        | ScalarExpression.Last query -> subquery "LAST" query
        | ScalarExpression.Subquery query -> subquery "" query

    and private writeCollection (style : PropertyStyle) (builder : StringBuilder) (collection : Collection) =
        match collection with
        | Collection.InputPath (input, path) ->
            builder.Append input.Value |> ignore

            match path with
            | ValueSome path -> writePath style builder path
            | ValueNone -> ()
        | Collection.Subquery query ->
            builder.Append '(' |> ignore
            writeQuery style builder query
            builder.Append ')' |> ignore

    and private writeCollectionExpression (style : PropertyStyle) (builder : StringBuilder) (expression : CollectionExpression) =
        match expression with
        | CollectionExpression.Aliased (collection, alias) ->
            writeCollection style builder collection

            match alias with
            | ValueSome alias -> builder.Append(" AS ").Append alias.Value |> ignore
            | ValueNone -> ()
        | CollectionExpression.ArrayIterator (alias, collection) ->
            builder.Append(alias.Value).Append " IN " |> ignore
            writeCollection style builder collection
        | CollectionExpression.Join (left, right) ->
            writeCollectionExpression style builder left
            builder.Append " JOIN " |> ignore
            writeCollectionExpression style builder right

    and private writeQuery (style : PropertyStyle) (builder : StringBuilder) (query : SqlQuery) =
        let scalar = writeScalar style builder
        let append (text : string) = builder.Append text |> ignore

        append "SELECT "

        if query.Distinct then
            append "DISTINCT "

        match query.Top with
        | ValueSome top ->
            append "TOP "
            writeCount builder top
            append " "
        | ValueNone -> ()

        match query.Select with
        | SelectSpec.Star -> append "*"
        | SelectSpec.Value expression ->
            append "VALUE "
            scalar expression
        | SelectSpec.List items ->
            items
            |> writeSeparated
                builder
                ", "
                (fun item ->
                    scalar item.Expression

                    match item.Alias with
                    | ValueSome alias -> builder.Append(" AS ").Append alias.Value |> ignore
                    | ValueNone -> ()
                )

        match query.From with
        | ValueSome from ->
            append " FROM "
            writeCollectionExpression style builder from
        | ValueNone -> ()

        match query.Where with
        | ValueSome predicate ->
            append " WHERE "
            scalar predicate
        | ValueNone -> ()

        if not query.GroupBy.IsEmpty then
            append " GROUP BY "
            query.GroupBy |> writeSeparated builder ", " scalar

        if not query.OrderBy.IsEmpty then
            append (
                if query.OrderByRank then
                    " ORDER BY RANK "
                else
                    " ORDER BY "
            )

            query.OrderBy
            |> writeSeparated
                builder
                ", "
                (fun item ->
                    scalar item.Expression

                    match item.Order with
                    | ValueSome SortOrder.Ascending -> append " ASC"
                    | ValueSome SortOrder.Descending -> append " DESC"
                    | ValueNone -> ()
                )

        match query.OffsetLimit with
        | ValueSome (struct (offset, limit)) ->
            append " OFFSET "
            writeCount builder offset
            append " LIMIT "
            writeCount builder limit
        | ValueNone -> ()

    let private render (write : StringBuilder -> unit) =
        let builder = StringBuilder ()
        write builder
        builder.ToString ()

    /// <summary>
    /// Writes <paramref name="query"/> as query text, with parameters as <c>@name</c>.
    /// </summary>
    /// <param name="style">How to write accesses to properties with string names.</param>
    /// <param name="query">The query to write.</param>
    let print (style : PropertyStyle) (query : SqlQuery) : string = render (fun builder -> writeQuery style builder query)

    /// <summary>
    /// Writes <paramref name="expression"/> as query text, with parameters as <c>@name</c>.
    /// </summary>
    /// <param name="style">How to write accesses to properties with string names.</param>
    /// <param name="expression">The expression to write.</param>
    let printScalar (style : PropertyStyle) (expression : ScalarExpression) : string =
        render (fun builder -> writeScalar style builder expression)

    /// <summary>
    /// Writes <paramref name="predicate"/> as the filter text <c>FROM alias WHERE predicate</c> that the SDK's
    /// <a href="https://learn.microsoft.com/dotnet/api/microsoft.azure.cosmos.patchitemrequestoptions.filterpredicate">PatchItemRequestOptions.FilterPredicate</a>
    /// expects, with parameters as <c>@name</c>.
    /// </summary>
    /// <param name="style">How to write accesses to properties with string names.</param>
    /// <param name="alias">The alias the predicate refers to the item by.</param>
    /// <param name="predicate">The condition.</param>
    let printPredicate (style : PropertyStyle) (alias : Identifier) (predicate : ScalarExpression) : string =
        render (fun builder ->
            builder.Append("FROM ").Append(alias.Value).Append " WHERE "
            |> ignore
            writeScalar style builder predicate
        )

    /// <summary>
    /// Writes <paramref name="query"/> as query text with every parameter replaced by the literal
    /// <paramref name="encode"/> gives for it, through
    /// <see cref="M:FSharp.Azure.Cosmos.Sql.Inline.substituteParameters(Microsoft.FSharp.Core.FSharpFunc{FSharp.Azure.Cosmos.Sql.ParameterName,Microsoft.FSharp.Core.FSharpValueOption{FSharp.Azure.Cosmos.Sql.ScalarExpression}},FSharp.Azure.Cosmos.Sql.SqlQuery)"/>.
    /// </summary>
    /// <param name="style">How to write accesses to properties with string names.</param>
    /// <param name="encode">Gives the literal that replaces a parameter.</param>
    /// <param name="query">The query to write.</param>
    /// <returns>The text, or one error per parameter that has no literal or an unusable one.</returns>
    let printInline
        (style : PropertyStyle)
        (encode : ParameterName -> ScalarExpression voption)
        (query : SqlQuery)
        : Result<string, ImmutableArray<ValidationError>> =
        query
        |> Inline.substituteParameters encode
        |> Result.map (print style)

    /// <summary>
    /// Writes <paramref name="predicate"/> as the filter text <c>FROM alias WHERE predicate</c>, like
    /// <see cref="M:FSharp.Azure.Cosmos.Sql.Printer.printPredicate(FSharp.Azure.Cosmos.Sql.PropertyStyle,FSharp.Azure.Cosmos.Sql.Identifier,FSharp.Azure.Cosmos.Sql.ScalarExpression)"/>,
    /// with every parameter replaced by the literal <paramref name="encode"/> gives for it: the form the filter
    /// predicate of a patch needs, because it takes no parameters.
    /// </summary>
    /// <param name="style">How to write accesses to properties with string names.</param>
    /// <param name="encode">Gives the literal that replaces a parameter.</param>
    /// <param name="alias">The alias the predicate refers to the item by.</param>
    /// <param name="predicate">The condition.</param>
    /// <returns>The text, or one error per parameter that has no literal or an unusable one.</returns>
    let printPredicateInline
        (style : PropertyStyle)
        (encode : ParameterName -> ScalarExpression voption)
        (alias : Identifier)
        (predicate : ScalarExpression)
        : Result<string, ImmutableArray<ValidationError>> =
        predicate
        |> Inline.substituteScalarParameters encode
        |> Result.map (printPredicate style alias)
