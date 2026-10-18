namespace FSharp.Azure.Cosmos.Quotations

open System.Collections.Immutable
open FSharp.Quotations
open FSharp.Quotations.DerivedPatterns
open FSharp.Quotations.ExprShape
open FSharp.Quotations.Patterns

open FSharp.Azure.Cosmos

/// <summary>
/// The place in a source file where a part of a quotation was written. The compiler records it only when it is run with
/// <c>--quotations-debug</c>, so a <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationError"/> has one only for
/// code compiled that way.
/// </summary>
[<Struct>]
type SourceRange = {
    /// The path of the source file, as the compiler recorded it.
    File : string
    /// The line the part starts at; the first line of a file is 1.
    StartLine : int
    /// The column the part starts at; the first column of a line is 0.
    StartColumn : int
    /// The line the part ends at.
    EndLine : int
    /// The column the part ends at.
    EndColumn : int
}

/// <summary>
/// The reason a <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationError"/> reports.
/// </summary>
[<RequireQualifiedAccess>]
type TranslationErrorCode =
    /// A construct of F# that a query has no counterpart for, such as a loop, an assignment or a recursive function.
    | Unsupported
    /// <summary>
    /// A function that exists only as a compiled value is applied to a value of the document. A compiled function
    /// cannot be read, so it cannot become a part of the query; a quotation or a function marked with
    /// <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> can.
    /// </summary>
    | CapturedFunctionValue
    /// <summary>
    /// The quotation cannot be brought into its simplest form: a function marked with
    /// <see cref="T:Microsoft.FSharp.Core.ReflectedDefinitionAttribute"/> calls itself, or the simplification did not
    /// finish within its limit of steps.
    /// </summary>
    | NormalizationDiverged

/// <summary>
/// Why a quotation cannot be translated into a query, with the part of the quotation that is the reason.
/// </summary>
/// <remarks>
/// The type has no equality, because <see cref="P:FSharp.Azure.Cosmos.Quotations.TranslationError.Hints"/> is an
/// <see cref="T:System.Collections.Immutable.ImmutableArray`1"/>, which is equal to another one only when both wrap
/// the same array. Compare the <see cref="P:FSharp.Azure.Cosmos.Quotations.TranslationError.Code"/>.
/// </remarks>
[<Struct; NoEquality; NoComparison>]
type TranslationError = {
    /// The reason, for code that handles the error.
    Code : TranslationErrorCode
    /// What is wrong, in English.
    Message : string
    /// The part of the quotation that is the reason, as text; empty when the error is about the quotation as a whole.
    Fragment : string
    /// <summary>
    /// Where the part was written, when the compiler recorded it: see
    /// <see cref="T:FSharp.Azure.Cosmos.Quotations.SourceRange"/>.
    /// </summary>
    Location : SourceRange voption
    /// What to write instead, in English; empty when there is no general advice.
    Hints : ImmutableArray<string>
}

/// <summary>
/// Reads the <see cref="T:FSharp.Azure.Cosmos.Quotations.SourceRange"/> that the compiler attached to a part of a
/// quotation.
/// </summary>
[<RequireQualifiedAccess>]
module internal SourceRange =

    /// <summary>
    /// The range in one custom attribute of a quotation node. The compiler writes it as the pair of the name
    /// <c>DebugRange</c> and the tuple of the file, the start line and column and the end line and column.
    /// </summary>
    [<return : Struct>]
    let private (|DebugRange|_|) (attribute : Expr) : SourceRange voption =
        match attribute with
        | NewTuple [ String "DebugRange"
                     NewTuple [ String file; Int32 startLine; Int32 startColumn; Int32 endLine; Int32 endColumn ] ] ->
            ValueSome {
                File = file
                StartLine = startLine
                StartColumn = startColumn
                EndLine = endLine
                EndColumn = endColumn
            }
        | _ -> ValueNone

    /// <summary>
    /// The range of <paramref name="expr"/>, or of the first part inside it that has one: a node that a rewrite built
    /// has none of its own, but the parts it was built from keep theirs.
    /// </summary>
    let rec tryOfQuotation (expr : Expr) : SourceRange voption =
        let own =
            expr.CustomAttributes
            |> List.tryPick (fun attribute ->
                match attribute with
                | DebugRange range -> ValueSome range
                | _ -> ValueNone
            )

        match own with
        | ValueSome _ -> own
        | ValueNone ->
            match expr with
            | ShapeVar _ -> ValueNone
            | ShapeLambda (_, body) -> tryOfQuotation body
            | ShapeCombination (_, parts) -> parts |> List.tryPick tryOfQuotation

/// <summary>
/// Builds a <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationError"/> about a part of a quotation.
/// </summary>
[<RequireQualifiedAccess>]
module internal TranslationError =

    /// <summary>
    /// The error with <paramref name="code"/>, <paramref name="message"/> and <paramref name="hints"/> about
    /// <paramref name="fragment"/>, which gives the error its text and, when the compiler recorded one, its place in
    /// the source.
    /// </summary>
    let ofQuotation
        (code : TranslationErrorCode)
        (message : string)
        (hints : ImmutableArray<string>)
        (fragment : Expr)
        : TranslationError = {
        Code = code
        Message = message
        Fragment = string fragment
        Location = SourceRange.tryOfQuotation fragment
        Hints = hints
    }

    /// <summary>
    /// The error with <paramref name="code"/> and <paramref name="message"/> about a quotation as a whole: it names no
    /// part of it.
    /// </summary>
    let ofWhole (code : TranslationErrorCode) (message : string) : TranslationError = {
        Code = code
        Message = message
        Fragment = ""
        Location = ValueNone
        Hints = ImmutableArray.Empty
    }
