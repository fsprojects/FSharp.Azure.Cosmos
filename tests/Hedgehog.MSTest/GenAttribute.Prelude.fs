// Copied from hedgehogqa/fsharp-hedgehog, src/Hedgehog.NUnit/GenAttribute.Prelude.fs at the Hedgehog 2.0.4 release commit
// a46977278db9a60542e3df3fe0fcd74b90f38ee3:
// https://github.com/hedgehogqa/fsharp-hedgehog/blob/a46977278db9a60542e3df3fe0fcd74b90f38ee3/src/Hedgehog.NUnit/GenAttribute.Prelude.fs
// Copyright (c) 2016 Jacob Stanley, Nikos Baxevanis. Licensed under the Apache License, Version 2.0
// (http://www.apache.org/licenses/LICENSE-2.0); see THIRD-PARTY-NOTICES.md at the root of this repository.
// Changes: the namespace Hedgehog.NUnit is renamed to Hedgehog.MSTest; the documentation of the attributes is
// rewritten; NonZeroInt skips a 0 at either end of its range, where upstream generates from an empty part, and rejects
// the range from 0 to 0; Odd and Even stay inside their range, where upstream leaves an even upper or an odd lower
// bound by one, and reject a range without such a value; DateOnlyAttribute and TimeOnlyAttribute are added.
// The file is formatted with Fantomas, under the settings of this repository.

namespace Hedgehog.MSTest

open System
open Hedgehog
open Hedgehog.FSharp

[<AutoOpen>]
module private RangeHelpers =

    [<Literal>]
    let private LargeRangeThreshold = 1000L
    [<Literal>]
    let private MediumRangeThreshold = 100L

    /// Choose between constant, linear, and exponential range based on the range size.
    /// - For ranges > 1000: use exponential to ensure boundary values are tested
    /// - For ranges > 100: use linear for balanced shrinking
    /// - For ranges <= 100: use constant (no shrinking needed for small ranges)
    let inline chooseRangeInt32 (min : int) (max : int) : Range<int> =
        let rangeSize = int64 max - int64 min
        let origin = if min <= 0 && 0 <= max then 0 else min
        match rangeSize with
        | size when size > LargeRangeThreshold -> Range.exponentialFrom origin min max
        | size when size > MediumRangeThreshold -> Range.linearFrom origin min max
        | _ -> Range.constantFrom origin min max

    /// The range narrowed to its first and last odd or even value, so that moving a generated value onto that parity
    /// cannot leave the range. A range without such a value is rejected.
    let parityRangeInt32 (odd : bool) (min : int) (max : int) : Range<int> =
        let isOffParity (value : int) = (value % 2 <> 0) <> odd
        // int64, because the neighbour of Int32.MinValue or Int32.MaxValue would overflow
        let first = if isOffParity min then int64 min + 1L else int64 min
        let last = if isOffParity max then int64 max - 1L else int64 max

        if first > last then
            let parity = if odd then "odd" else "even"
            invalidArg (nameof min) $"The range from %d{min} to %d{max} holds no %s{parity} value."

        chooseRangeInt32 (int first) (int last)

/// <summary>
/// Generates an <see cref="T:System.Int32"/> from <paramref name="min"/> to <paramref name="max"/>, both included.
/// </summary>
/// <remarks>
/// The range is exponential when it spans more than 1000 values, linear when it spans more than 100 and constant
/// otherwise; it shrinks towards 0 when it contains 0 and towards <paramref name="min"/> otherwise.
/// </remarks>
type IntAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    (min : int, max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates an <see cref="T:System.Int32"/> from <see cref="F:System.Int32.MinValue"/> to
    /// <see cref="F:System.Int32.MaxValue"/>.
    /// </summary>
    new () = IntAttribute (Int32.MinValue, Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator = Gen.int32 (chooseRangeInt32 min max)

/// <summary>
/// Generates an odd <see cref="T:System.Int32"/> from <paramref name="min"/> to <paramref name="max"/>, both included.
/// </summary>
/// <remarks>
/// The range is narrowed to its first and last odd value and then chosen as for
/// <see cref="T:Hedgehog.MSTest.IntAttribute"/>; a range without an odd value is rejected. Upstream sets the lowest bit of
/// any value of the range, which exceeds an even <paramref name="max"/> by one.
/// </remarks>
type OddAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    (min : int, max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates an odd <see cref="T:System.Int32"/> from the whole range of <see cref="T:System.Int32"/>.
    /// </summary>
    new () = OddAttribute (Int32.MinValue, Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator =
        let range = parityRangeInt32 true min max

        gen {
            let! n = Gen.int32 range
            return n ||| 1
        }

/// <summary>
/// Generates an even <see cref="T:System.Int32"/> from <paramref name="min"/> to <paramref name="max"/>, both included.
/// </summary>
/// <remarks>
/// The range is narrowed to its first and last even value and then chosen as for
/// <see cref="T:Hedgehog.MSTest.IntAttribute"/>; a range without an even value is rejected. Upstream clears the lowest
/// bit of any value of the range, which falls below an odd <paramref name="min"/> by one.
/// </remarks>
type EvenAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    (min : int, max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates an even <see cref="T:System.Int32"/> from the whole range of <see cref="T:System.Int32"/>.
    /// </summary>
    new () = EvenAttribute (Int32.MinValue, Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator =
        let range = parityRangeInt32 false min max

        gen {
            let! n = Gen.int32 range
            return n &&& ~~~1
        }

/// <summary>
/// Generates a positive <see cref="T:System.Int32"/> from 1 to <paramref name="max"/>.
/// </summary>
/// <remarks>
/// The range is chosen as for <see cref="T:Hedgehog.MSTest.IntAttribute"/>.
/// </remarks>
type PositiveIntAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="max">The largest value.</param>
    (max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates a positive <see cref="T:System.Int32"/> from 1 to <see cref="F:System.Int32.MaxValue"/>.
    /// </summary>
    new () = PositiveIntAttribute (Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator = Gen.int32 (chooseRangeInt32 1 max)

/// <summary>
/// Generates a non-negative <see cref="T:System.Int32"/> from 0 to <paramref name="max"/>.
/// </summary>
/// <remarks>
/// The range is chosen as for <see cref="T:Hedgehog.MSTest.IntAttribute"/>.
/// </remarks>
type NonNegativeIntAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="max">The largest value.</param>
    (max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates a non-negative <see cref="T:System.Int32"/> from 0 to <see cref="F:System.Int32.MaxValue"/>.
    /// </summary>
    new () = NonNegativeIntAttribute (Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator = Gen.int32 (chooseRangeInt32 0 max)

/// <summary>
/// Generates a non-zero <see cref="T:System.Int32"/> from <paramref name="min"/> to <paramref name="max"/>.
/// </summary>
/// <remarks>
/// A 0 at either end of the range is skipped. A range with 0 inside is split into its negative and its positive part,
/// and each generated value comes from one of them; each part is chosen as for
/// <see cref="T:Hedgehog.MSTest.IntAttribute"/>. The range from 0 to 0 holds no non-zero value and is rejected.
/// </remarks>
type NonZeroIntAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    (min : int, max : int)
    =
    inherit GenAttribute<int> ()
    /// <summary>
    /// Generates a non-zero <see cref="T:System.Int32"/> from <see cref="F:System.Int32.MinValue"/> + 1 to
    /// <see cref="F:System.Int32.MaxValue"/>.
    /// </summary>
    new () = NonZeroIntAttribute (Int32.MinValue + 1, Int32.MaxValue)
    /// <inheritdoc />
    override _.Generator =
        match min, max with
        | 0, 0 -> invalidArg (nameof min) "The range from 0 to 0 holds no non-zero value."
        | _, m when m < 0 -> Gen.int32 (chooseRangeInt32 min max) // Range entirely negative
        | n, _ when n > 0 -> Gen.int32 (chooseRangeInt32 min max) // Range entirely positive
        | 0, m -> Gen.int32 (chooseRangeInt32 1 m) // 0 is the lowest value, skip it
        | n, 0 -> Gen.int32 (chooseRangeInt32 n -1) // 0 is the highest value, skip it
        | n, m -> // 0 lies inside the range, split it
            Gen.choice [| Gen.int32 (chooseRangeInt32 n -1); Gen.int32 (chooseRangeInt32 1 m) |]

/// <summary>
/// Generates a <see cref="T:System.String"/> that is a valid identifier, through
/// <see cref="M:Hedgehog.FSharp.GenConvenience.Gen.identifier(System.Int32)"/>.
/// </summary>
type IdentifierAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="maxLen">The largest length.</param>
    (maxLen : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates an identifier of up to 25 characters.
    /// </summary>
    new () = IdentifierAttribute (25)
    /// <inheritdoc />
    override _.Generator = Gen.identifier maxLen

/// <summary>
/// Generates a <see cref="T:System.String"/> that is a Latin name, through
/// <see cref="M:Hedgehog.FSharp.GenConvenience.Gen.latinName(System.Int32)"/>.
/// </summary>
type LatinNameAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="maxLength">The largest length.</param>
    (maxLength : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates a Latin name of up to 20 characters.
    /// </summary>
    new () = LatinNameAttribute (20)
    /// <inheritdoc />
    override _.Generator = Gen.latinName maxLength

/// <summary>
/// Generates a <see cref="T:System.String"/> in snake case, such as <c>snake_case</c>, through
/// <see cref="M:Hedgehog.FSharp.GenConvenience.Gen.snakeCase(Hedgehog.Range{System.Int32},Hedgehog.Range{System.Int32})"/>.
/// </summary>
type SnakeCaseAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="maxWordLength">The largest length of one word.</param>
    /// <param name="maxWordsCount">The largest number of words.</param>
    (maxWordLength : int, maxWordsCount : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates up to 5 words of up to 5 characters each in snake case.
    /// </summary>
    new () = SnakeCaseAttribute (5, 5)
    /// <inheritdoc />
    override _.Generator = Gen.snakeCase (Range.constant 1 maxWordLength) (Range.constant 1 maxWordsCount)

/// <summary>
/// Generates a <see cref="T:System.String"/> in kebab case, such as <c>kebab-case</c>, through
/// <see cref="M:Hedgehog.FSharp.GenConvenience.Gen.kebabCase(Hedgehog.Range{System.Int32},Hedgehog.Range{System.Int32})"/>.
/// </summary>
type KebabCaseAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="maxWordLength">The largest length of one word.</param>
    /// <param name="maxWordsCount">The largest number of words.</param>
    (maxWordLength : int, maxWordsCount : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates up to 5 words of up to 5 characters each in kebab case.
    /// </summary>
    new () = KebabCaseAttribute (5, 5)
    /// <inheritdoc />
    override _.Generator = Gen.kebabCase (Range.constant 1 maxWordLength) (Range.constant 1 maxWordsCount)

/// <summary>
/// Generates a <see cref="T:System.String"/> that is a valid domain name, through
/// <see cref="P:Hedgehog.FSharp.GenUri.Gen.domainName"/>.
/// </summary>
type DomainNameAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    ()
    =
    inherit GenAttribute<string> ()
    /// <inheritdoc />
    override _.Generator = Gen.domainName

/// <summary>
/// Generates a <see cref="T:System.String"/> that is a valid email address, through
/// <see cref="P:Hedgehog.FSharp.GenUri.Gen.email"/>.
/// </summary>
type EmailAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    ()
    =
    inherit GenAttribute<string> ()
    /// <inheritdoc />
    override _.Generator = Gen.email

/// <summary>
/// Generates a <see cref="T:System.DateTime"/> of the given <paramref name="kind"/> from <paramref name="from"/> to
/// <paramref name="from"/> plus <paramref name="duration"/>.
/// </summary>
type DateTimeAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="kind">The <see cref="P:System.DateTime.Kind"/> of every value.</param>
    /// <param name="from">The earliest value.</param>
    /// <param name="duration">How much later than <paramref name="from"/> the latest value is.</param>
    (kind : DateTimeKind, from : DateTime, duration : TimeSpan)
    =
    inherit GenAttribute<DateTime> ()
    /// <summary>
    /// Generates a <see cref="T:System.DateTime"/> of kind <see cref="F:System.DateTimeKind.Utc"/> within the 3650 days
    /// from 2000-01-01.
    /// </summary>
    new () = DateTimeAttribute (DateTimeKind.Utc, DateTime (2000, 1, 1), TimeSpan.FromDays (3650))
    /// <summary>
    /// Generates a <see cref="T:System.DateTime"/> of kind <see cref="F:System.DateTimeKind.Utc"/> from
    /// <paramref name="from"/> to <paramref name="from"/> plus <paramref name="duration"/>.
    /// </summary>
    /// <param name="from">The earliest value.</param>
    /// <param name="duration">How much later than <paramref name="from"/> the latest value is.</param>
    new (from, duration) = DateTimeAttribute (DateTimeKind.Utc, from, duration)
    /// <summary>
    /// Generates a <see cref="T:System.DateTime"/> of the given <paramref name="kind"/> within the 3650 days from
    /// 2000-01-01.
    /// </summary>
    /// <param name="kind">The <see cref="P:System.DateTime.Kind"/> of every value.</param>
    new (kind) = DateTimeAttribute (kind, DateTime (2000, 1, 1), TimeSpan.FromDays (3650))
    /// <inheritdoc />
    override _.Generator = Gen.dateTime (Range.constant from (from + duration)) (Gen.constant kind)

/// <summary>
/// Generates a <see cref="T:System.DateTimeOffset"/> from <paramref name="from"/> to <paramref name="from"/> plus
/// <paramref name="duration"/>.
/// </summary>
type DateTimeOffsetAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="from">The earliest value.</param>
    /// <param name="duration">How much later than <paramref name="from"/> the latest value is.</param>
    (from : DateTimeOffset, duration : TimeSpan)
    =
    inherit GenAttribute<DateTimeOffset> ()
    /// <summary>
    /// Generates a <see cref="T:System.DateTimeOffset"/> with offset zero within the 3650 days from 2000-01-01.
    /// </summary>
    new () = DateTimeOffsetAttribute (DateTimeOffset (2000, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays (3650))
    /// <summary>
    /// Generates a <see cref="T:System.DateTimeOffset"/> within the 3650 days from <paramref name="from"/>.
    /// </summary>
    /// <param name="from">The earliest value.</param>
    new (from) = DateTimeOffsetAttribute (from, TimeSpan.FromDays (3650))
    /// <inheritdoc />
    override _.Generator = Gen.dateTimeOffset (Range.constant from (from + duration))

/// <summary>
/// Generates a <see cref="T:System.DateOnly"/> from <paramref name="min"/> to <paramref name="max"/>, both included.
/// </summary>
/// <remarks>
/// The range is constant: it does not grow with the size of a case, and a value shrinks towards
/// <paramref name="min"/>. Upstream's adapters have no such attribute, and Hedgehog 2.0.4 cannot auto-generate a
/// <see cref="T:System.DateOnly"/>, so a parameter of this type needs this attribute or a generator of its own.
/// </remarks>
type DateOnlyAttribute
    /// <summary>
    /// Creates the attribute from two dates. An attribute argument cannot be a <see cref="T:System.DateOnly"/>, so this
    /// constructor serves attributes that derive from this one.
    /// </summary>
    /// <param name="min">The earliest value.</param>
    /// <param name="max">The latest value.</param>
    (min : DateOnly, max : DateOnly)
    =
    inherit GenAttribute<DateOnly> ()
    /// <summary>
    /// Generates a <see cref="T:System.DateOnly"/> within the 3650 days from 2000-01-01, the dates of the default range
    /// of <see cref="T:Hedgehog.MSTest.DateTimeAttribute"/>.
    /// </summary>
    new () = DateOnlyAttribute (DateOnly (2000, 1, 1), DateOnly(2000, 1, 1).AddDays(3650))
    /// <summary>
    /// Generates a <see cref="T:System.DateOnly"/> from the first date to the second, both included.
    /// </summary>
    /// <param name="minYear">The year of the earliest value.</param>
    /// <param name="minMonth">The month of the earliest value.</param>
    /// <param name="minDay">The day of the earliest value.</param>
    /// <param name="maxYear">The year of the latest value.</param>
    /// <param name="maxMonth">The month of the latest value.</param>
    /// <param name="maxDay">The day of the latest value.</param>
    new (minYear : int, minMonth : int, minDay : int, maxYear : int, maxMonth : int, maxDay : int)
        =
        DateOnlyAttribute (DateOnly (minYear, minMonth, minDay), DateOnly (maxYear, maxMonth, maxDay))
    /// <inheritdoc />
    override _.Generator =
        Gen.int32 (Range.constant min.DayNumber max.DayNumber)
        |> Gen.map DateOnly.FromDayNumber

/// <summary>
/// Generates a <see cref="T:System.TimeOnly"/> from <paramref name="min"/> to <paramref name="max"/>, both included.
/// </summary>
/// <remarks>
/// A value has the full precision of <see cref="T:System.TimeOnly"/>, 100 nanoseconds. The range is constant: it does
/// not grow with the size of a case, and a value shrinks towards <paramref name="min"/>. Upstream's adapters have no
/// such attribute, and Hedgehog 2.0.4 cannot auto-generate a <see cref="T:System.TimeOnly"/>, so a parameter of this
/// type needs this attribute or a generator of its own.
/// </remarks>
type TimeOnlyAttribute
    /// <summary>
    /// Creates the attribute from two times of day. An attribute argument cannot be a <see cref="T:System.TimeOnly"/>,
    /// so this constructor serves attributes that derive from this one.
    /// </summary>
    /// <param name="min">The earliest value.</param>
    /// <param name="max">The latest value.</param>
    (min : TimeOnly, max : TimeOnly)
    =
    inherit GenAttribute<TimeOnly> ()
    /// <summary>
    /// Generates any <see cref="T:System.TimeOnly"/>, from <see cref="P:System.TimeOnly.MinValue"/> to
    /// <see cref="P:System.TimeOnly.MaxValue"/>.
    /// </summary>
    new () = TimeOnlyAttribute (TimeOnly.MinValue, TimeOnly.MaxValue)
    /// <summary>
    /// Generates a <see cref="T:System.TimeOnly"/> from the first time of day to the second, both included.
    /// </summary>
    /// <param name="minHour">The hour of the earliest value.</param>
    /// <param name="minMinute">The minute of the earliest value.</param>
    /// <param name="maxHour">The hour of the latest value.</param>
    /// <param name="maxMinute">The minute of the latest value.</param>
    new (minHour : int, minMinute : int, maxHour : int, maxMinute : int)
        =
        TimeOnlyAttribute (TimeOnly (minHour, minMinute), TimeOnly (maxHour, maxMinute))
    /// <inheritdoc />
    override _.Generator =
        Gen.int64 (Range.constant min.Ticks max.Ticks)
        |> Gen.map TimeOnly

/// <summary>
/// Generates a <see cref="T:System.String"/> of ASCII letters and digits, from <paramref name="minLength"/> to
/// <paramref name="maxLength"/> characters long.
/// </summary>
type AlphaNumStringAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="minLength">The smallest length.</param>
    /// <param name="maxLength">The largest length.</param>
    (minLength : int, maxLength : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates a <see cref="T:System.String"/> of ASCII letters and digits, up to 256 characters long.
    /// </summary>
    new () = AlphaNumStringAttribute (0, 256)
    /// <summary>
    /// Generates a <see cref="T:System.String"/> of ASCII letters and digits, from <paramref name="minLength"/> to 256
    /// characters long.
    /// </summary>
    /// <param name="minLength">The smallest length.</param>
    new (minLength) = AlphaNumStringAttribute (minLength, 256)
    /// <inheritdoc />
    override _.Generator = Gen.string (Range.constant minLength maxLength) Gen.alphaNum

/// <summary>
/// Generates a <see cref="T:System.String"/> of Unicode characters, from <paramref name="minLength"/> to
/// <paramref name="maxLength"/> characters long.
/// </summary>
type UnicodeStringAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="minLength">The smallest length.</param>
    /// <param name="maxLength">The largest length.</param>
    (minLength : int, maxLength : int)
    =
    inherit GenAttribute<string> ()
    /// <summary>
    /// Generates a <see cref="T:System.String"/> of Unicode characters, up to 256 characters long.
    /// </summary>
    new () = UnicodeStringAttribute (0, 256)
    /// <summary>
    /// Generates a <see cref="T:System.String"/> of Unicode characters, from <paramref name="minLength"/> to 256
    /// characters long.
    /// </summary>
    /// <param name="minLength">The smallest length.</param>
    new (minLength) = UnicodeStringAttribute (minLength, 256)
    /// <inheritdoc />
    override _.Generator = Gen.string (Range.constant minLength maxLength) Gen.unicode

/// <summary>
/// Generates an IPv4 <see cref="T:System.Net.IPAddress"/>, through <see cref="P:Hedgehog.FSharp.GenUri.Gen.ipv4Address"/>.
/// </summary>
type Ipv4AddressAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    ()
    =
    inherit GenAttribute<System.Net.IPAddress> ()
    /// <inheritdoc />
    override _.Generator = Gen.ipv4Address

/// <summary>
/// Generates an IPv6 <see cref="T:System.Net.IPAddress"/>, through <see cref="P:Hedgehog.FSharp.GenUri.Gen.ipv6Address"/>.
/// </summary>
type Ipv6AddressAttribute
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    ()
    =
    inherit GenAttribute<System.Net.IPAddress> ()
    /// <inheritdoc />
    override _.Generator = Gen.ipv6Address
