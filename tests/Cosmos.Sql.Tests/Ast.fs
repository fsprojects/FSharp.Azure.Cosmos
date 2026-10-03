/// Terse builders of syntax trees for the tests.
module FSharp.Azure.Cosmos.Sql.Tests.Ast

open FSharp.Azure.Cosmos.Sql

/// A bare identifier, such as the collection alias.
let alias (name : string) = ScalarExpression.PropertyRef (Identifier name)

/// An integer literal.
let integer (value : int64) = ScalarExpression.Literal (Literal.Int value)

/// A double literal.
let number (value : float) = ScalarExpression.Literal (Literal.Float value)

/// A string literal.
let str (value : string) = ScalarExpression.Literal (Literal.String value)

/// A boolean literal.
let boolean (value : bool) = ScalarExpression.Literal (Literal.Boolean value)

/// The null literal.
let nul = ScalarExpression.Literal Literal.Null

/// The undefined literal.
let undefined = ScalarExpression.Literal Literal.Undefined

/// <summary>
/// A parameter reference; the name includes its <c>@</c>.
/// </summary>
let param (name : string) = ScalarExpression.ParameterRef (ParameterName name)

/// <summary>
/// A property access with a string name, <c>target["name"]</c>.
/// </summary>
let prop (name : string) (target : ScalarExpression) = ScalarExpression.MemberIndexer (target, str name)

/// <summary>
/// An element access with an integer index, <c>target[index]</c>.
/// </summary>
let at (index : int64) (target : ScalarExpression) = ScalarExpression.MemberIndexer (target, integer index)

/// A binary operation.
let binary operator left right = ScalarExpression.Binary (operator, left, right)

/// A call of a built-in function.
let call (name : string) (arguments : ScalarExpression list) =
    ScalarExpression.FunctionCall (FunctionRef.BuiltIn name, EquatableArray.ofSeq arguments)

/// A call of a user-defined function.
let udf (name : string) (arguments : ScalarExpression list) =
    ScalarExpression.FunctionCall (FunctionRef.Udf name, EquatableArray.ofSeq arguments)

/// An array literal.
let arrayOf (items : ScalarExpression list) = ScalarExpression.ArrayCreate (EquatableArray.ofSeq items)

/// An object literal, from its properties in order.
let objectOf (properties : struct (string * ScalarExpression) list) =
    ScalarExpression.ObjectCreate (EquatableArray.ofSeq properties)

/// <summary>
/// A <c>FROM</c> clause over an input name without an alias, <c>FROM name</c>.
/// </summary>
let from (name : string) =
    ValueSome (CollectionExpression.Aliased (Collection.InputPath (Identifier name, ValueNone), ValueNone))

/// A query without clauses that projects the given value; extend it with a copy-and-update expression.
let selectValue (expression : ScalarExpression) = {
    Select = SelectSpec.Value expression
    Distinct = false
    Top = ValueNone
    From = ValueNone
    Where = ValueNone
    GroupBy = EquatableArray.empty
    OrderBy = EquatableArray.empty
    OrderByRank = false
    OffsetLimit = ValueNone
}

/// <summary>
/// <c>SELECT VALUE root FROM root</c>, the shape the SDK's LINQ baselines start from.
/// </summary>
let selectRoot = { selectValue (alias "root") with From = from "root" }

/// <summary>
/// An <c>ORDER BY</c> item.
/// </summary>
let orderBy (order : SortOrder voption) (expression : ScalarExpression) = { Expression = expression; Order = order }
