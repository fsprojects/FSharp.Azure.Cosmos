/// <summary>
/// Functions that say, inside a quotation, how a value gets into the query.
/// </summary>
/// <remarks>
/// <para>
/// A query is its text and the values of its parameters. A value that a quotation writes as a literal, such as
/// <c>18</c> in <c>p.Age &gt; 18</c>, is a part of the text. A value that the quotation takes from the code around it
/// or computes from such values, such as <c>limit</c> in <c>p.Age &gt; limit</c>, is a parameter: the text stays the
/// same when the value changes.
/// </para>
/// <para>
/// The functions here say the opposite for one value. Outside a quotation each of them returns its argument.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Quotations.Query

/// <summary>
/// Makes <paramref name="value"/> a part of the text of the query, although the quotation does not write it as a
/// literal: <c>p.Kind = Query.constant kind</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every different value gives a query with a text of its own, which is translated on its own. So this is for a value
/// that takes few different values, never for a value that a user of the application enters.
/// </para>
/// <para>
/// The value has to be known before the query runs: it cannot depend on the document. And it has to be a value that a
/// query can hold as a literal: a boolean, a character, a string, an integer or a floating-point number of a built-in
/// type, a value of an enumeration or the <see langword="null"/> of a reference type. A value of any other type can
/// only be a parameter: a decimal, a date, and a nullable or an option, with a value or without one.
/// </para>
/// </remarks>
/// <param name="value">The value, which the quotation takes from the code around it or computes from such values.</param>
/// <returns><paramref name="value"/>.</returns>
let constant (value : 'T) : 'T = value

/// <summary>
/// Makes <paramref name="value"/> a parameter of the query, although the quotation writes it as a literal:
/// <c>p.Age &gt; Query.parameter 18</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two queries that differ only in such a value have the same text, so the second one is not translated again.
/// </para>
/// <para>
/// The value has to be known before the query runs: it cannot depend on the document.
/// </para>
/// </remarks>
/// <param name="value">The value.</param>
/// <returns><paramref name="value"/>.</returns>
let parameter (value : 'T) : 'T = value
