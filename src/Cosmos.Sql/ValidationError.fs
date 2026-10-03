namespace FSharp.Azure.Cosmos.Sql

/// <summary>
/// The rule a <see cref="T:FSharp.Azure.Cosmos.Sql.ValidationError"/> reports, such as a parameter that inline printing
/// cannot replace.
/// </summary>
[<RequireQualifiedAccess>]
type ValidationErrorCode =
    /// A parameter for which inline printing has no literal.
    | MissingInlineValue
    /// A parameter whose inline value is not a constant: a literal, or an array or object literal of constants.
    | InlineValueNotConstant
    /// <summary>
    /// A <c>TOP</c>, <c>OFFSET</c> or <c>LIMIT</c> parameter whose inline value is not an integer literal.
    /// </summary>
    | InlineCountNotInteger

/// A rule a query breaks, with an English message that names the offending part.
[<Struct>]
type ValidationError = {
    /// The rule that is broken.
    Code : ValidationErrorCode
    /// What is wrong, in English.
    Message : string
}
