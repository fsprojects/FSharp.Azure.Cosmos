namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open System
open System.Collections.Immutable
open FSharp.Quotations

open FSharp.Azure.Cosmos.Sql

/// The document the sample query is written against.
type Person = {
    Id : string
    Name : string
    Age : int
    City : string
    Nick : string voption
    Tags : ImmutableArray<string>
    Registered : DateTimeOffset
}

/// Query arguments that arrive together, the way a request handler holds them.
type PersonFilter = { MinAge : int; City : string }

/// <summary>
/// The query every benchmark measures, in the forms a translator passes through: the quotation a caller writes and the
/// <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/> it is translated into. Until the translator exists, the second form
/// is written by hand here.
/// </summary>
[<RequireQualifiedAccess>]
module SampleQuery =

    /// A predicate with what a typical one has: two captured arguments, a literal, an optional member and a collection.
    let predicate (minAge : int) (city : string) : Expr<Person -> bool> =
        <@
            fun p ->
                p.Age >= minAge
                && p.City = city
                && p.Nick.IsSome
                && p.Tags.Contains "vip"
        @>

    /// The first of the four conditions alone, to see how the cost of a quotation literal grows with its size.
    let shortPredicate (minAge : int) : Expr<Person -> bool> = <@ fun p -> p.Age >= minAge @>

    /// A member of a captured record: an expression that does not read the document, so its value is a parameter.
    let minAgeOf (filter : PersonFilter) : Expr<int> = <@ filter.MinAge @>

    /// Arithmetic on the clock: another expression that does not read the document.
    let thirtyDaysAgo () : Expr<DateTimeOffset> = <@ DateTimeOffset.UtcNow.AddDays -30.0 @>

    /// <summary>
    /// The names of the parameters of <see cref="syntaxTree"/>, in the order in which <see cref="predicate"/> holds
    /// their values.
    /// </summary>
    let parameterNames =
        seq {
            "@minAge"
            "@city"
            "@p0"
        }
        |> ImmutableArray.CreateRange

    /// <summary>
    /// The translation of <see cref="predicate"/>, built anew on every call:
    /// <c>SELECT VALUE p FROM p WHERE ((((p["Age"] >= @minAge) AND (p["City"] = @city)) AND
    /// (IS_DEFINED(p["Nick"]) AND (NOT IS_NULL(p["Nick"])))) AND ARRAY_CONTAINS(p["Tags"], @p0))</c>.
    /// </summary>
    let syntaxTree () : SqlQuery =
        let person = ScalarExpression.PropertyRef (Identifier "p")

        let field (name : string) =
            ScalarExpression.MemberIndexer (person, ScalarExpression.Literal (Literal.String name))

        let parameter (name : string) = ScalarExpression.ParameterRef (ParameterName name)

        let call (name : string) (arguments : ScalarExpression array) =
            ScalarExpression.FunctionCall (FunctionRef.BuiltIn name, EquatableArray.unsafeOfArray arguments)

        let both (left : ScalarExpression) (right : ScalarExpression) = ScalarExpression.Binary (BinaryOperator.And, left, right)

        let isAdult =
            ScalarExpression.Binary (BinaryOperator.GreaterThanOrEqual, field "Age", parameter "@minAge")

        let livesInCity = ScalarExpression.Binary (BinaryOperator.Equal, field "City", parameter "@city")

        let hasNick =
            both
                (call "IS_DEFINED" [| field "Nick" |])
                (ScalarExpression.Unary (UnaryOperator.Not, call "IS_NULL" [| field "Nick" |]))

        let isVip = call "ARRAY_CONTAINS" [| field "Tags"; parameter "@p0" |]

        {
            Select = SelectSpec.Value person
            Distinct = false
            Top = ValueNone
            From = ValueSome (CollectionExpression.Aliased (Collection.InputPath (Identifier "p", ValueNone), ValueNone))
            Where = ValueSome (both (both (both isAdult livesInCity) hasNick) isVip)
            GroupBy = EquatableArray.empty
            OrderBy = EquatableArray.empty
            OrderByRank = false
            OffsetLimit = ValueNone
        }

    /// <summary>
    /// The literal of every parameter of <see cref="syntaxTree"/>, for the print mode that writes the values into the
    /// query text.
    /// </summary>
    let literalOf (name : ParameterName) : ScalarExpression voption =
        match name.Value with
        | "@minAge" -> ValueSome (ScalarExpression.Literal (Literal.Int 18L))
        | "@city" -> ValueSome (ScalarExpression.Literal (Literal.String "Kyiv"))
        | "@p0" -> ValueSome (ScalarExpression.Literal (Literal.String "vip"))
        | _ -> ValueNone
