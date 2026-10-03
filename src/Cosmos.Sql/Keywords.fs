/// The reserved words of the query language and the identifier rule, used by the validator to check aliases and by the
/// printer to decide when a property name can be written with a dot.
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Sql.Keywords

open System
open System.Collections.Frozen

/// <summary>
/// The keywords of the SDK grammar <c>sql.g4</c> (its <c>K_</c> tokens), compared case-insensitively.
/// <para>
/// The grammar matches most keywords case-insensitively; <c>true</c>, <c>false</c>, <c>null</c>, <c>undefined</c> and
/// <c>udf</c> only in lower case, and it accepts <c>ALL</c>, <c>FIRST</c> and <c>LAST</c> as identifiers. The set
/// treats all of them as reserved in every spelling, so an identifier that passes
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isSafeIdentifier(System.String)"/> is never mistaken for a keyword.
/// </para>
/// </summary>
let reserved : FrozenSet<string> =
    [|
        "ALL"
        "AND"
        "ARRAY"
        "AS"
        "ASC"
        "BETWEEN"
        "BY"
        "DESC"
        "DISTINCT"
        "ESCAPE"
        "EXISTS"
        "FALSE"
        "FIRST"
        "FROM"
        "GROUP"
        "IN"
        "JOIN"
        "LAST"
        "LEFT"
        "LIKE"
        "LIMIT"
        "NOT"
        "NULL"
        "OFFSET"
        "OR"
        "ORDER"
        "RANK"
        "RIGHT"
        "SELECT"
        "TOP"
        "TRUE"
        "UDF"
        "UNDEFINED"
        "VALUE"
        "WHERE"
    |]
    |> _.ToFrozenSet(StringComparer.OrdinalIgnoreCase)

/// <summary>
/// Whether <paramref name="text"/> is a reserved word of the query language, in any letter case.
/// </summary>
/// <param name="text">The text to check.</param>
let isReserved (text : string) = reserved.Contains text

/// Whether the characters match the identifier rule; a span, so a parameter name is checked without its '@' and without
/// copying it.
let internal isValidIdentifierSpan (span : ReadOnlySpan<char>) =
    let isStart (character : char) = Char.IsAsciiLetter character || character = '_'

    if span.IsEmpty || not (isStart span[0]) then
        false
    else
        let mutable valid = true
        let mutable index = 1

        while valid && index < span.Length do
            valid <- isStart span[index] || Char.IsAsciiDigit span[index]
            index <- index + 1

        valid

/// <summary>
/// Whether <paramref name="text"/> matches the identifier rule of the grammar, <c>[A-Za-z_][A-Za-z_0-9]*</c>, with
/// ASCII letters only. The empty alternative of the grammar's <c>LEX_IDENTIFIER</c> token is not accepted.
/// </summary>
/// <param name="text">The text to check.</param>
let isValidIdentifier (text : string) = isValidIdentifierSpan (text.AsSpan ())

/// <summary>
/// Whether <paramref name="text"/> can be written as a bare identifier: it matches
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isValidIdentifier(System.String)"/> and is not
/// <see cref="M:FSharp.Azure.Cosmos.Sql.Keywords.isReserved(System.String)"/>.
/// </summary>
/// <param name="text">The text to check.</param>
let isSafeIdentifier (text : string) = isValidIdentifier text && not (isReserved text)
