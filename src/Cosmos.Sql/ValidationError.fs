namespace FSharp.Azure.Cosmos.Sql

/// <summary>
/// The rule a <see cref="T:FSharp.Azure.Cosmos.Sql.ValidationError"/> reports: a rule of the query language that the
/// grammar cannot express, or a parameter that inline printing cannot replace.
/// </summary>
[<RequireQualifiedAccess>]
type ValidationErrorCode =
    /// <summary>
    /// <c>SELECT *</c> without exactly one collection in the <c>FROM</c> clause.
    /// </summary>
    | SelectStarWithoutSingleAlias
    /// <summary>
    /// <c>TOP</c> in a subquery, which the service rejects.
    /// </summary>
    | TopInSubquery
    /// <summary>
    /// A parameter in an <c>ORDER BY</c> item that is not a call of a function allowed there: a parameter cannot choose
    /// the sort key. The arguments of an allowed call, such as the query vector of <c>VectorDistance</c>, may be
    /// parameters.
    /// </summary>
    | ParameterInOrderBy
    /// <summary>
    /// A parameter in a <c>GROUP BY</c> key, which the service rejects.
    /// </summary>
    | ParameterInGroupBy
    /// <summary>
    /// An <c>ORDER BY</c> item that is neither a property path nor a call of a function allowed in <c>ORDER BY</c>.
    /// </summary>
    | OrderByItemNotPath
    /// <summary>
    /// An <c>ORDER BY RANK</c> item that is not a call of a scoring function.
    /// </summary>
    | RankItemNotScoringFunction
    /// <summary>
    /// A function that may appear only in <c>ORDER BY RANK</c> used anywhere else.
    /// </summary>
    | ScoringFunctionOutsideRank
    /// <summary>
    /// An identifier that does not match <c>[A-Za-z_][A-Za-z_0-9]*</c>.
    /// </summary>
    | InvalidIdentifier
    /// An identifier that is a reserved word of the query language.
    | ReservedIdentifier
    /// <summary>
    /// A parameter name that does not match <c>@[A-Za-z_][A-Za-z_0-9]*</c>.
    /// </summary>
    | InvalidParameterName
    /// <summary>
    /// An <c>IN</c> list without elements.
    /// </summary>
    | EmptyInList
    /// A select list without items.
    | EmptySelectList
    /// An integer literal outside -2^53..2^53, which a JSON number cannot hold exactly.
    | LossyInteger
    /// A number literal that is not a finite number.
    | NonFiniteNumber
    /// <summary>
    /// A negative <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c> count.
    /// </summary>
    | NegativeCount
    /// A call of a built-in function the catalog does not know.
    | UnknownFunction
    /// A call of a built-in function with a number of arguments the catalog does not accept.
    | FunctionArity
    /// A function name that is not a valid identifier, or a keyword the grammar cannot call.
    | InvalidFunctionName
    /// A parameter for which inline printing has no literal.
    | MissingInlineValue
    /// A parameter whose inline value is not a constant: a literal, or an array or object literal of constants.
    | InlineValueNotConstant
    /// <summary>
    /// A <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c> parameter whose inline value is not an integer literal.
    /// </summary>
    | InlineCountNotInteger

/// A rule a query breaks, with an English message that quotes the offending fragment.
[<Struct>]
type ValidationError = {
    /// The rule that is broken.
    Code : ValidationErrorCode
    /// What is wrong, in English.
    Message : string
}

/// The settings of validation.
type ValidationOptions = {
    /// Whether integer literals outside -2^53..2^53 are accepted. Such a literal loses precision as a JSON number, so it
    /// is rejected unless the translator's semantics explicitly allow lossy 64-bit integers.
    AllowLossyInt64 : bool
} with

    static let ``default`` = { AllowLossyInt64 = false }

    /// The default settings: lossy integer literals are rejected.
    static member Default = ``default``
