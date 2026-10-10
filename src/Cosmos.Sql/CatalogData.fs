// The catalog data file: one row per built-in function of the query language.
//
// Seeded with every name of SqlFunctionCallScalarExpression.Names of Azure/azure-cosmos-dotnet-v3 3.62.0 (MIT, see
// THIRD-PARTY-NOTICES.md) except the internal names, which start with an underscore, plus the names Microsoft Learn
// documents that the SDK does not list (the INTBIT* spellings, NOW, AGO, NUMBERBIN, REGEXREPLACE, REGEXREPLACEALL,
// ST_AREA and the GETCURRENT*STATIC functions). Rows the translator does not need yet are Unsupported with a reason;
// a row becomes Supported once its arity, return kind and placement rules are filled in from its Learn page. The
// remarks of the Learn pages give the index usage; a property the page does not state stays Unspecified.
module internal FSharp.Azure.Cosmos.Sql.CatalogData

open System.Collections.Immutable

/// The Microsoft Learn page of a documented function: its name in lower case with underscores as hyphens.
let private docUrl (name : string) =
    ValueSome $"""https://learn.microsoft.com/cosmos-db/query/%s{name.ToLowerInvariant().Replace('_', '-')}"""

/// A row whose arity, return kind and placement rules are specified.
let private supported category name arity returnKind = {
    Name = name
    Category = category
    Arity = arity
    ReturnKind = returnKind
    UndefinedRule = UndefinedRule.Unspecified
    IndexUsage = IndexUsage.Unspecified
    Availability = Availability.Service
    Deterministic = true
    AllowedInOrderBy = false
    AllowedInOrderByRank = false
    OnlyInOrderByRank = false
    DocUrl = docUrl name
    Status = FunctionStatus.Supported
}

let private notSpecified =
    "Not specified yet; the translator pack that uses the function fills in its arity, return kind and placement rules."

/// A documented function that the catalog does not specify yet.
let private documented category name = {
    supported category name Arity.Unspecified ReturnKind.Unspecified with
        Status = FunctionStatus.Unsupported notSpecified
}

/// A name the SDK lists that Microsoft Learn does not document.
let private undocumented name reason = {
    documented FunctionCategory.Undocumented name with
        Availability = Availability.None
        DocUrl = ValueNone
        Status = FunctionStatus.Unsupported reason
}

let private undocumentedName =
    "Not documented on Microsoft Learn; the SDK lists the name without a specification."

let private typedValueFunction =
    "An undocumented typed-value function of the C_ family, not part of the documented NoSQL query language."

/// A keyword of the grammar that the SDK also lists as a function name; the grammar cannot parse it as a call.
let private keyword name form =
    undocumented name $"A keyword of the grammar, so it cannot be called as a function; the query language writes %s{form}."

/// An SDK spelling of an integer bitwise function that Learn documents under another name.
let private sdkSpelling name documentedName =
    undocumented name $"The SDK's spelling of %s{documentedName}; the documented name is %s{documentedName}."

/// Every built-in function, grouped like the function index on Microsoft Learn.
let functions : ImmutableArray<FunctionSpec> =
    seq {
        // Mathematical functions
        documented FunctionCategory.Mathematical "ABS"
        documented FunctionCategory.Mathematical "ACOS"
        documented FunctionCategory.Mathematical "ASIN"
        documented FunctionCategory.Mathematical "ATAN"
        documented FunctionCategory.Mathematical "ATN2"
        documented FunctionCategory.Mathematical "CEILING"
        documented FunctionCategory.Mathematical "COS"
        documented FunctionCategory.Mathematical "COT"
        documented FunctionCategory.Mathematical "DEGREES"
        documented FunctionCategory.Mathematical "EXP"
        documented FunctionCategory.Mathematical "FLOOR"
        documented FunctionCategory.Mathematical "IntAdd"
        supported FunctionCategory.Mathematical "INTBITAND" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "INTBITLEFTSHIFT" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "INTBITNOT" (Arity.Exactly 1) ReturnKind.Number
        supported FunctionCategory.Mathematical "INTBITOR" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "INTBITRIGHTSHIFT" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "INTBITXOR" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "IntDiv" (Arity.Exactly 2) ReturnKind.Number
        supported FunctionCategory.Mathematical "IntMod" (Arity.Exactly 2) ReturnKind.Number
        documented FunctionCategory.Mathematical "IntMul"
        documented FunctionCategory.Mathematical "IntSub"
        documented FunctionCategory.Mathematical "LOG"
        documented FunctionCategory.Mathematical "LOG10"
        documented FunctionCategory.Mathematical "NUMBERBIN"
        documented FunctionCategory.Mathematical "PI"
        documented FunctionCategory.Mathematical "POWER"
        documented FunctionCategory.Mathematical "RADIANS"
        { documented FunctionCategory.Mathematical "RAND" with Deterministic = false }
        documented FunctionCategory.Mathematical "ROUND"
        documented FunctionCategory.Mathematical "SIGN"
        documented FunctionCategory.Mathematical "SIN"
        documented FunctionCategory.Mathematical "SQRT"
        documented FunctionCategory.Mathematical "SQUARE"
        documented FunctionCategory.Mathematical "TAN"
        documented FunctionCategory.Mathematical "TRUNC"

        // Array functions
        documented FunctionCategory.Array "ARRAY_AVG"
        documented FunctionCategory.Array "ARRAY_CONCAT"
        {
            supported FunctionCategory.Array "ARRAY_CONTAINS" (Arity.Between (2, 3)) ReturnKind.Boolean with
                IndexUsage = IndexUsage.RangeIndex
        }
        documented FunctionCategory.Array "ARRAY_CONTAINS_ALL"
        documented FunctionCategory.Array "ARRAY_CONTAINS_ANY"
        {
            supported FunctionCategory.Array "ARRAY_LENGTH" (Arity.Exactly 1) ReturnKind.Number with
                UndefinedRule = UndefinedRule.UndefinedOnInvalidArgument
                IndexUsage = IndexUsage.NoIndex
        }
        documented FunctionCategory.Array "ARRAY_MAX"
        documented FunctionCategory.Array "ARRAY_MEDIAN"
        documented FunctionCategory.Array "ARRAY_MIN"
        documented FunctionCategory.Array "ARRAY_SLICE"
        documented FunctionCategory.Array "ARRAY_SUM"
        documented FunctionCategory.Array "CHOOSE"
        documented FunctionCategory.Array "ObjectToArray"
        documented FunctionCategory.Array "SetDifference"
        documented FunctionCategory.Array "SetEqual"
        documented FunctionCategory.Array "SetIntersect"
        documented FunctionCategory.Array "SetUnion"

        // Aggregation functions
        {
            supported FunctionCategory.Aggregate "AVG" (Arity.Exactly 1) ReturnKind.Number with
                IndexUsage = IndexUsage.RangeIndex
        }
        supported FunctionCategory.Aggregate "COUNT" (Arity.Exactly 1) ReturnKind.Number
        supported FunctionCategory.Aggregate "MAX" (Arity.Exactly 1) ReturnKind.Any
        supported FunctionCategory.Aggregate "MIN" (Arity.Exactly 1) ReturnKind.Any
        {
            supported FunctionCategory.Aggregate "SUM" (Arity.Exactly 1) ReturnKind.Number with
                IndexUsage = IndexUsage.RangeIndex
        }

        // String functions
        {
            supported FunctionCategory.String "CONCAT" (Arity.AtLeast 2) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "CONTAINS" (Arity.Between (2, 3)) ReturnKind.Boolean with
                IndexUsage = IndexUsage.FullScan
        }
        supported FunctionCategory.String "ENDSWITH" (Arity.Between (2, 3)) ReturnKind.Boolean
        supported FunctionCategory.String "INDEX_OF" (Arity.Between (2, 3)) ReturnKind.Number
        documented FunctionCategory.String "LastIndexOf"
        documented FunctionCategory.String "LastSubstringAfter"
        documented FunctionCategory.String "LastSubstringBefore"
        {
            supported FunctionCategory.String "LEFT" (Arity.Exactly 2) ReturnKind.String with
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.String "LENGTH" (Arity.Exactly 1) ReturnKind.Number with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "LOWER" (Arity.Exactly 1) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "LTRIM" (Arity.Between (1, 2)) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        documented FunctionCategory.String "RegexExtract"
        documented FunctionCategory.String "RegexExtractAll"
        {
            supported FunctionCategory.String "RegexMatch" (Arity.Between (2, 3)) ReturnKind.Boolean with
                IndexUsage = IndexUsage.RangeIndex
        }
        documented FunctionCategory.String "REGEXREPLACE"
        documented FunctionCategory.String "REGEXREPLACEALL"
        {
            supported FunctionCategory.String "REPLACE" (Arity.Exactly 3) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        documented FunctionCategory.String "REPLICATE"
        documented FunctionCategory.String "REVERSE"
        {
            supported FunctionCategory.String "RIGHT" (Arity.Exactly 2) ReturnKind.String with
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.String "RTRIM" (Arity.Between (1, 2)) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "STARTSWITH" (Arity.Between (2, 3)) ReturnKind.Boolean with
                IndexUsage = IndexUsage.PreciseIndexScan
        }
        {
            supported FunctionCategory.String "StringEquals" (Arity.Between (2, 3)) ReturnKind.Boolean with
                IndexUsage = IndexUsage.IndexSeek
        }
        documented FunctionCategory.String "StringJoin"
        documented FunctionCategory.String "StringSplit"
        documented FunctionCategory.String "StringToArray"
        documented FunctionCategory.String "StringToBoolean"
        {
            supported FunctionCategory.String "SUBSTRING" (Arity.Exactly 3) ReturnKind.String with
                IndexUsage = IndexUsage.RangeIndex
        }
        documented FunctionCategory.String "SubstringAfter"
        documented FunctionCategory.String "SubstringBefore"
        {
            supported FunctionCategory.String "ToString" (Arity.Exactly 1) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "TRIM" (Arity.Between (1, 2)) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }
        {
            supported FunctionCategory.String "UPPER" (Arity.Exactly 1) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
        }

        // Date and time functions
        { documented FunctionCategory.DateTime "AGO" with Deterministic = false }
        {
            supported FunctionCategory.DateTime "DateTimeAdd" (Arity.Exactly 3) ReturnKind.String with
                UndefinedRule = UndefinedRule.UndefinedOnInvalidArgument
        }
        documented FunctionCategory.DateTime "DateTimeBin"
        supported FunctionCategory.DateTime "DateTimeDiff" (Arity.Exactly 3) ReturnKind.Number
        documented FunctionCategory.DateTime "DateTimeFormat"
        documented FunctionCategory.DateTime "DateTimeFromParts"
        {
            supported FunctionCategory.DateTime "DateTimePart" (Arity.Exactly 2) ReturnKind.Number with
                IndexUsage = IndexUsage.NoIndex
        }
        documented FunctionCategory.DateTime "DateTimeToTicks"
        documented FunctionCategory.DateTime "DateTimeToTimestamp"
        documented FunctionCategory.DateTime "DAY"
        {
            supported FunctionCategory.DateTime "GetCurrentDateTime" (Arity.Exactly 0) ReturnKind.String with
                IndexUsage = IndexUsage.NoIndex
                Deterministic = false
        }
        {
            documented FunctionCategory.DateTime "GETCURRENTDATETIMESTATIC" with
                Deterministic = false
        }
        {
            documented FunctionCategory.DateTime "GetCurrentTicks" with
                Deterministic = false
        }
        {
            documented FunctionCategory.DateTime "GETCURRENTTICKSSTATIC" with
                Deterministic = false
        }
        {
            documented FunctionCategory.DateTime "GetCurrentTimestamp" with
                Deterministic = false
        }
        {
            documented FunctionCategory.DateTime "GETCURRENTTIMESTAMPSTATIC" with
                Deterministic = false
        }
        documented FunctionCategory.DateTime "MONTH"
        { documented FunctionCategory.DateTime "NOW" with Deterministic = false }
        documented FunctionCategory.DateTime "TicksToDateTime"
        documented FunctionCategory.DateTime "TimestampToDateTime"
        documented FunctionCategory.DateTime "YEAR"

        // Item functions
        documented FunctionCategory.Item "DOCUMENTID"

        // Full text search functions
        documented FunctionCategory.FullTextSearch "FullTextContains"
        documented FunctionCategory.FullTextSearch "FullTextContainsAll"
        documented FunctionCategory.FullTextSearch "FullTextContainsAny"
        {
            supported FunctionCategory.FullTextSearch "FullTextScore" (Arity.AtLeast 2) ReturnKind.Number with
                AllowedInOrderByRank = true
                OnlyInOrderByRank = true
        }
        {
            supported FunctionCategory.FullTextSearch "RRF" (Arity.AtLeast 2) ReturnKind.Number with
                AllowedInOrderByRank = true
                OnlyInOrderByRank = true
        }

        // Conditional functions
        supported FunctionCategory.Conditional "IIF" (Arity.Exactly 3) ReturnKind.Any

        // Type checking functions
        {
            supported FunctionCategory.TypeChecking "IS_ARRAY" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_BOOL" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_DATETIME" (Arity.Exactly 1) ReturnKind.Boolean with
                IndexUsage = IndexUsage.FullScan
        }
        {
            supported FunctionCategory.TypeChecking "IS_DEFINED" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_FINITE_NUMBER" (Arity.Exactly 1) ReturnKind.Boolean with
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_INTEGER" (Arity.Exactly 1) ReturnKind.Boolean with
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_NULL" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_NUMBER" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_OBJECT" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_PRIMITIVE" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        {
            supported FunctionCategory.TypeChecking "IS_STRING" (Arity.Exactly 1) ReturnKind.Boolean with
                UndefinedRule = UndefinedRule.NeverUndefined
                IndexUsage = IndexUsage.RangeIndex
        }
        documented FunctionCategory.TypeChecking "StringToNull"
        documented FunctionCategory.TypeChecking "StringToNumber"
        documented FunctionCategory.TypeChecking "StringToObject"

        // Spatial functions; Learn lists VectorDistance among them
        documented FunctionCategory.Spatial "ST_AREA"
        documented FunctionCategory.Spatial "ST_DISTANCE"
        documented FunctionCategory.Spatial "ST_INTERSECTS"
        documented FunctionCategory.Spatial "ST_ISVALID"
        documented FunctionCategory.Spatial "ST_ISVALIDDETAILED"
        documented FunctionCategory.Spatial "ST_WITHIN"
        {
            supported FunctionCategory.Spatial "VectorDistance" (Arity.Between (2, 4)) ReturnKind.Number with
                AllowedInOrderBy = true
                AllowedInOrderByRank = true
        }

        // Names the SDK lists that Microsoft Learn does not document
        keyword "ALL" "ALL(subquery)"
        undocumented "ANY" undocumentedName
        keyword "ARRAY" "ARRAY(subquery)"
        undocumented "C_BINARY" typedValueFunction
        undocumented "C_FLOAT32" typedValueFunction
        undocumented "C_FLOAT64" typedValueFunction
        undocumented "C_GUID" typedValueFunction
        undocumented "C_INT16" typedValueFunction
        undocumented "C_INT32" typedValueFunction
        undocumented "C_INT64" typedValueFunction
        undocumented "C_INT8" typedValueFunction
        undocumented "C_LIST" typedValueFunction
        undocumented "C_LISTCONTAINS" typedValueFunction
        undocumented "C_MAP" typedValueFunction
        undocumented "C_MAPCONTAINS" typedValueFunction
        undocumented "C_MAPCONTAINSKEY" typedValueFunction
        undocumented "C_MAPCONTAINSVALUE" typedValueFunction
        undocumented "C_SET" typedValueFunction
        undocumented "C_SETCONTAINS" typedValueFunction
        undocumented "C_TUPLE" typedValueFunction
        undocumented "C_UDT" typedValueFunction
        undocumented "C_UINT32" typedValueFunction
        undocumented "ContainsAllCi" undocumentedName
        undocumented "ContainsAllCs" undocumentedName
        undocumented "ContainsAnyCi" undocumentedName
        undocumented "ContainsAnyCs" undocumentedName
        undocumented "HASH" undocumentedName
        sdkSpelling "IntBitwiseAnd" "INTBITAND"
        sdkSpelling "IntBitwiseLeftShift" "INTBITLEFTSHIFT"
        sdkSpelling "IntBitwiseNot" "INTBITNOT"
        sdkSpelling "IntBitwiseOr" "INTBITOR"
        sdkSpelling "IntBitwiseRightShift" "INTBITRIGHTSHIFT"
        sdkSpelling "IntBitwiseXor" "INTBITXOR"
        keyword "LIKE" "the LIKE operator, (value LIKE pattern)"
    }
    |> ImmutableArray.CreateRange
