namespace FSharp.Azure.Cosmos.Quotations

open System
open System.Collections.Immutable
open System.Reflection
open System.Text
open FSharp.Quotations
open FSharp.Quotations.ExprShape
open FSharp.Quotations.Patterns
open FSharp.Reflection

/// <summary>
/// What a <see cref="T:FSharp.Azure.Cosmos.Quotations.ShapeToken"/> says about the node of a quotation it stands for.
/// </summary>
type internal ShapeTokenKind =
    /// A variable that the quotation binds. The number says how many bindings lie between the variable and its own.
    | Variable = 0
    /// A function written in the quotation. The subject is the type of its variable.
    | Lambda = 1
    /// A delegate written in the quotation. The subject is the type of the delegate, the number counts its variables.
    | NewDelegate = 2
    /// A value that is a part of the text of the query. The subject is its type, the detail the value.
    | Constant = 3
    /// <summary>
    /// A <see cref="T:FSharp.Azure.Cosmos.Quotations.Slot"/>. The number is its index, the subject its type and the
    /// detail the name of the captured variable, when it has one.
    /// </summary>
    | Slot = 4
    /// The value a type has before anything is assigned. The subject is the type.
    | DefaultValue = 5
    /// A union case that is built. The subject is the case.
    | NewUnionCase = 6
    /// A tuple that is built. The subject is the type of the tuple, which tells a struct tuple from a reference tuple.
    | NewTuple = 7
    /// A record that is built. The subject is the type of the record.
    | NewRecord = 8
    /// An array that is built. The subject is the type of its items, the number counts them.
    | NewArray = 9
    /// A value seen as a value of another type. The subject is that type.
    | Coerce = 10
    /// A choice between two branches.
    | IfThenElse = 11
    /// A call of a method. The subject is the method, with its type arguments.
    | Call = 12
    /// A reading of a property. The subject is the property.
    | PropertyGet = 13
    /// A reading of a field. The subject is the field.
    | FieldGet = 14
    /// A call of a constructor. The subject is the constructor.
    | NewObject = 15
    /// A reading of an item of a tuple. The number is the index of the item.
    | TupleGet = 16
    /// A test for a union case. The subject is the case.
    | UnionCaseTest = 17
    /// A test for a type. The subject is the type.
    | TypeTest = 18
    /// An application of a function value to an argument.
    | Application = 19

/// <summary>
/// One node of a quotation, as a <see cref="T:FSharp.Azure.Cosmos.Quotations.ShapeKey"/> keeps it: the kind of the
/// node and what tells it from another node of that kind. The nodes under it follow it in the key.
/// </summary>
/// <remarks>
/// Two tokens are equal when they say the same about a node. A member, a type and a union case are compared the way
/// reflection compares them, a constant by its value. A floating-point constant is compared bit by bit, because
/// <c>0.0</c> and <c>-0.0</c> are equal as numbers and are written differently in the text of a query.
/// </remarks>
[<Struct; CustomEquality; NoComparison>]
type internal ShapeToken = {
    /// What the token says about its node.
    Kind : ShapeTokenKind
    /// The number that the kind gives a meaning to; 0 for a kind that has none.
    Number : int
    /// <summary>
    /// The member, type or union case that the kind gives a meaning to; <see langword="null"/> for a kind that
    /// has none.
    /// </summary>
    Subject : objnull
    /// <summary>
    /// The value of a constant or the name of a slot; <see langword="null"/> for a kind that has neither, for the
    /// constant <see langword="null"/> and for a slot without a name.
    /// </summary>
    Detail : objnull
} with

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are the same token.
    /// </summary>
    static member private AreSame (first : ShapeToken, second : ShapeToken) : bool =
        let sameDetail =
            match first.Detail, second.Detail with
            | (:? float as first), (:? float as second) ->
                BitConverter.DoubleToInt64Bits first = BitConverter.DoubleToInt64Bits second
            | (:? float32 as first), (:? float32 as second) ->
                BitConverter.SingleToInt32Bits first = BitConverter.SingleToInt32Bits second
            | first, second -> Object.Equals (first, second)

        first.Kind = second.Kind
        && first.Number = second.Number
        && Object.Equals (first.Subject, second.Subject)
        && sameDetail

    /// <inheritdoc />
    override token.Equals (other : objnull) : bool =
        match other with
        | :? ShapeToken as other -> ShapeToken.AreSame (token, other)
        | _ -> false

    /// <inheritdoc />
    override token.GetHashCode () : int = HashCode.Combine (int token.Kind, token.Number, token.Subject, token.Detail)

    /// The token as text, for the message of a failed assertion and for a debugger.
    override token.ToString () : string =
        let text = StringBuilder (string token.Kind)

        if
            token.Number <> 0
            || token.Kind = ShapeTokenKind.Variable
            || token.Kind = ShapeTokenKind.Slot
        then
            text.Append(' ').Append(token.Number) |> ignore

        match token.Subject with
        | null -> ()
        | subject -> text.Append(' ').Append(subject) |> ignore

        match token.Kind, token.Detail with
        | ShapeTokenKind.Constant, detail -> text.Append(" = ").Append($"%A{detail}") |> ignore
        | _, null -> ()
        | _, detail -> text.Append(' ').Append(detail) |> ignore

        text.ToString ()

    interface IEquatable<ShapeToken> with

        /// <inheritdoc />
        member token.Equals (other : ShapeToken) : bool = ShapeToken.AreSame (token, other)

/// <summary>
/// The shape of a quotation: everything in it that decides the text of the query, and nothing that only decides the
/// values of the parameters. Two quotations with equal keys give the same text, so the second one does not have to be
/// translated again.
/// </summary>
/// <remarks>
/// <para>
/// A quotation cannot be its own key. It compares its variables by identity, and every evaluation of a quotation
/// literal creates new variables, so no two evaluations of one literal are equal. It also compares the values it
/// captured, which are what a key has to leave out.
/// </para>
/// <para>
/// The key is the list of the nodes of the quotation, every node before the nodes under it. A variable is in it as
/// the distance to its binding, so the names of variables do not matter. A
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Slot"/> is in it as its index, type and name, without its value.
/// </para>
/// </remarks>
[<Struct; CustomEquality; NoComparison>]
type internal ShapeKey = {
    /// The nodes of the quotation, every node before the nodes under it.
    Tokens : ImmutableArray<ShapeToken>
    /// The hash code of the tokens, computed once when the key is made.
    Hash : int
} with

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> are the same key.
    /// </summary>
    static member private AreSame (first : ShapeKey, second : ShapeKey) : bool =
        // Spans, because the key that no code has filled holds an array that is not there, which a span reads as empty.
        first.Hash = second.Hash
        && MemoryExtensions.SequenceEqual (first.Tokens.AsSpan (), second.Tokens.AsSpan ())

    /// <inheritdoc />
    override key.Equals (other : objnull) : bool =
        match other with
        | :? ShapeKey as other -> ShapeKey.AreSame (key, other)
        | _ -> false

    /// <inheritdoc />
    override key.GetHashCode () : int = key.Hash

    /// The tokens as text, one on a line, for the message of a failed assertion and for a debugger.
    override key.ToString () : string =
        let text = StringBuilder ()

        for token in key.Tokens.AsSpan () do
            text.Append(token.ToString ()).Append('\n') |> ignore

        text.ToString ()

    interface IEquatable<ShapeKey> with

        /// <inheritdoc />
        member key.Equals (other : ShapeKey) : bool = ShapeKey.AreSame (key, other)

/// <summary>
/// A place in a query for a value that is known before the query runs and is not a part of its text: a value that the
/// quotation captured from the code around it, or the value of a part of the quotation that reads nothing of the
/// document. The parameters of the query are made of the values of its slots.
/// </summary>
[<Struct>]
type internal Slot = {
    /// <summary>
    /// The variable that stands for the value in the <see cref="P:FSharp.Azure.Cosmos.Quotations.Template.Body"/>.
    /// Its type is the type of the value.
    /// </summary>
    Variable : Var
    /// The name of the captured variable that the value comes from; none for a value that is computed.
    Name : string voption
}

/// <summary>
/// What is known about a quotation without translating it: its
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.ShapeKey"/>, by which a translation made earlier is found, and the
/// values to run that translation with.
/// </summary>
[<Struct>]
type internal Shape = {
    /// The shape of the quotation.
    Key : ShapeKey
    /// <summary>
    /// The value of every <see cref="T:FSharp.Azure.Cosmos.Quotations.Slot"/>, by the index of the slot. A slot has no
    /// value when the quotation computes it only in a branch that it does not take this time: the query does not
    /// take that branch either, so whatever stands in for the value there changes no result.
    /// </summary>
    Values : ImmutableArray<objnull voption>
}

/// <summary>
/// A quotation prepared for translation: what its <see cref="T:FSharp.Azure.Cosmos.Quotations.Shape"/> holds, and the
/// quotation itself with a variable at every place where a value goes into the query as a parameter.
/// </summary>
[<Struct>]
type internal Template = {
    /// The shape of the quotation.
    Key : ShapeKey
    /// <summary>
    /// The value of every slot, by the index of the slot: see
    /// <see cref="P:FSharp.Azure.Cosmos.Quotations.Shape.Values"/>.
    /// </summary>
    Values : ImmutableArray<objnull voption>
    /// <summary>
    /// The quotation with the variable of a <see cref="T:FSharp.Azure.Cosmos.Quotations.Slot"/> at every place of a
    /// value that is a parameter. Every value that is left in it is a part of the text of the query.
    /// </summary>
    Body : Expr
    /// The slots, by their index.
    Slots : ImmutableArray<Slot>
}

/// <summary>
/// Tells the shape of a quotation from the values it was written with: which values are parameters of the query, what
/// their values are this time, and what is left as the <see cref="T:FSharp.Azure.Cosmos.Quotations.ShapeKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// The quotation has to be in the normal form that
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Normalizer.normalize(Microsoft.FSharp.Quotations.FSharpExpr)"/> gives.
/// </para>
/// <para>
/// These are the rules.
/// </para>
/// <list type="bullet">
/// <item><description>A literal is a part of the text of the query, and so of the key: a boolean, a character, a
/// string, an integer or a finite floating-point number of a built-in type, a value of an enumeration, the unit value
/// and <see langword="null"/>. A value of one of these types that is declared with
/// <see cref="T:Microsoft.FSharp.Core.LiteralAttribute"/> is such a literal, because the compiler writes it into the
/// quotation as one. <c>NaN</c> and the infinities are no literals: the text of a query has no such number, so
/// they are slots. The default value of a type is the literal of that type when the type has one, as
/// <c>Unchecked.defaultof&lt;int&gt;</c> is zero; the default value of a date, a guid, a nullable or a union is data,
/// and a slot.</description></item>
/// <item><description>A decimal is not a literal, however it is written: the compiler writes <c>18m</c> as a call
/// that makes the decimal, so it is computed like any call and is a slot. A parameter keeps its digits; a number in
/// the text of a query is read as a floating-point number.</description></item>
/// <item><description>A captured value is a <see cref="T:FSharp.Azure.Cosmos.Quotations.Slot"/> named after its
/// variable. A value of another type that code put into a quotation it built is a slot without a
/// name.</description></item>
/// <item><description>A part that
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Evaluator.canEvaluate(FSharp.Azure.Cosmos.Quotations.Evaluator.Refused,Microsoft.FSharp.Quotations.FSharpExpr)"/>
/// accepts, the largest one at its place, is computed and is a slot without a name: a property of a captured value, a
/// call, a module-level value.</description></item>
/// <item><description>A tuple, record, union case or array that the quotation builds stays what it is, with its parts
/// handled on their own, even when no part reads the document. The translator has to see that <c>Some limit</c> is an
/// option with a value, and that a list is written item by item.</description></item>
/// <item><description><see cref="M:FSharp.Azure.Cosmos.Quotations.Query.constant``1(``0)"/> and
/// <see cref="M:FSharp.Azure.Cosmos.Quotations.Query.parameter``1(``0)"/> say the opposite for their argument. A
/// constant has to be a value of one of the types of a literal: the key holds it, and equal keys have to mean one
/// text.</description></item>
/// </list>
/// <para>
/// A slot stands for one node of the quotation, at every place the node is at. A value that a
/// <see langword="let"/> bound is such a node: the normalizer writes the bound node at every place the value is used.
/// So the value is one slot, and its code runs once, as it does in compiled code, also when the places are inside
/// two different parts that are computed: in <c>let limit = read () in p.Age &gt; limit + 1 || p.Age &lt; limit + 2</c>
/// both sums are slots of their own, and <c>read</c> is called once. One node at two places is one slot whatever put it
/// there: one quotation that is spliced into two places is one slot for both, with one value.
/// </para>
/// <para>
/// A captured variable that is used at several places is a slot at each of them. The compiler writes every use as a
/// node of its own, and nothing in those nodes says that they are one variable: two quotations that are spliced
/// together can each have captured a variable of the same name. Their values must not say it either. Were two such
/// nodes one slot whenever they hold one value, the key would depend on whether two values happen to be equal this
/// time, and one query would have two texts. A value that has to be one parameter is bound by a
/// <see langword="let"/> inside the quotation.
/// </para>
/// <para>
/// A choice whose condition is known before the query runs computes only the parts of the branch it takes, as compiled
/// code does: in <c>filter.IsNone || p.Name = filter.Value.Trim ()</c> nothing is trimmed when there is no filter. The
/// parts of the other branch are slots without a value. The key does not depend on which branch is taken.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module internal Parameterizer =

    /// The reason a quotation has no shape, on its way out of the walk over the quotation.
    [<Sealed>]
    type private Refusal (error : TranslationError) =
        inherit Exception (error.Message)

        /// The reason.
        member _.Error = error

    /// <summary>
    /// What a function of <see cref="T:FSharp.Azure.Cosmos.Quotations.Query"/> says about its argument.
    /// </summary>
    [<Struct>]
    type private Marker =
        /// The argument is a part of the text of the query.
        | AsConstant
        /// The argument is a parameter of the query.
        | AsParameter

    /// <summary>
    /// The generic definition of the method that <paramref name="call"/> calls.
    /// </summary>
    let private calledDefinition (call : Expr) : MethodInfo =
        match call with
        | Call (None, method, _) -> method.GetGenericMethodDefinition ()
        | _ -> invalidArg (nameof call) $"The quotation is not a call of a function: %A{call}"

    /// <summary>
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.Query.constant``1(``0)"/>.
    /// </summary>
    let private constantFunction = calledDefinition <@ Query.constant 0 @>

    /// <summary>
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.Query.parameter``1(``0)"/>.
    /// </summary>
    let private parameterFunction = calledDefinition <@ Query.parameter 0 @>

    /// <summary>
    /// What <paramref name="method"/> says about its argument, when it is a function of
    /// <see cref="T:FSharp.Azure.Cosmos.Quotations.Query"/> that says something.
    /// </summary>
    [<return : Struct>]
    let private (|MarkerFunction|_|) (method : MethodInfo) : Marker voption =
        // The declaring type is compared first: it is one reference comparison, and it fails for nearly every method.
        if Type.(=) (method.DeclaringType, constantFunction.DeclaringType) then
            let token = method.MetadataToken

            if token = constantFunction.MetadataToken then
                ValueSome AsConstant
            elif token = parameterFunction.MetadataToken then
                ValueSome AsParameter
            else
                ValueNone
        else
            ValueNone

    /// <summary>
    /// Whether a null of the type <paramref name="declared"/> is a literal: the null of a reference type that is no
    /// union, and the unit value. A case of a union that is represented by null, as the None of an option is, is no
    /// null: the compiler writes a case as a case.
    /// </summary>
    let private isLiteralNull (declared : Type) : bool =
        not declared.IsValueType
        && not (FSharpType.IsUnion (declared, true))

    /// <summary>
    /// Whether a value of the type <paramref name="declared"/> can be a literal by its type alone: a boolean, a
    /// character, a string, an integer or a floating-point number of a built-in type, and a value of an enumeration.
    /// </summary>
    /// <remarks>
    /// An enumeration has the type code of the number under it, and a nullable has the one of an object. There is no
    /// decimal here: the compiler never writes one as a value.
    /// </remarks>
    let private isLiteralType (declared : Type) : bool =
        match Type.GetTypeCode declared with
        | TypeCode.Boolean
        | TypeCode.Char
        | TypeCode.String
        | TypeCode.SByte
        | TypeCode.Byte
        | TypeCode.Int16
        | TypeCode.UInt16
        | TypeCode.Int32
        | TypeCode.UInt32
        | TypeCode.Int64
        | TypeCode.UInt64
        | TypeCode.Single
        | TypeCode.Double -> true
        | _ -> false

    /// <summary>
    /// Whether the compiler could have written <paramref name="value"/> into a quotation as a literal of the type
    /// <paramref name="declared"/>. Any other value without a name was put there by code that builds quotations, and
    /// that code has no other way to pass data, so the value is taken for data.
    /// </summary>
    /// <remarks>
    /// The declared type decides, and not the type of the value: a nullable with a value is boxed as that value, and a
    /// nullable without one is a null. So is a case of a union that is represented by null, as the None of an option
    /// is. The compiler writes none of them as a value: a case of a union it writes as a case.
    /// </remarks>
    let private isLiteral (value : objnull) (declared : Type) : bool =
        match value with
        | null -> isLiteralNull declared
        | _ ->
            // The text of a query has no NaN and no infinity: a number is digits with a fraction and an exponent.
            match Type.GetTypeCode declared with
            | TypeCode.Single -> Single.IsFinite (Convert.ToSingle value)
            | TypeCode.Double -> Double.IsFinite (Convert.ToDouble value)
            | _ -> isLiteralType declared

    /// <summary>
    /// Whether the default value of <paramref name="valueType"/> is a literal: the zero of a number, <c>false</c>, the
    /// character with the code zero, the value of an enumeration that has the number zero, and the null of a class that
    /// is no union. The default value of any other type, such as a date, a guid, a nullable or a union, is data.
    /// </summary>
    /// <remarks>
    /// The type tells, and the default value is not made to ask it: a struct can have a constructor without
    /// parameters, which the default value of the struct never runs and a value made by a constructor would.
    /// </remarks>
    let private hasLiteralDefault (valueType : Type) : bool = isLiteralNull valueType || isLiteralType valueType

    /// <summary>
    /// Whether <paramref name="valueType"/> is the type of F# functions or a class made of it, as the class of every
    /// compiled function is and as a class that code declares can be.
    /// </summary>
    let rec private isFunctionClass (valueType : Type | null) : bool =
        match valueType with
        | null -> false
        | valueType ->
            (valueType.IsGenericType
             && Type.(=) (valueType.GetGenericTypeDefinition (), typedefof<_ -> _>))
            || isFunctionClass valueType.BaseType

    /// <summary>
    /// Whether a value of <paramref name="valueType"/> is a function or a delegate.
    /// </summary>
    let private isFunction (valueType : Type) : bool =
        // A function and a delegate are objects, so a value type is neither, and nearly every slot is of one or a string.
        not valueType.IsValueType
        && (typeof<Delegate>.IsAssignableFrom valueType
            || isFunctionClass valueType)

    /// <summary>
    /// Whether a part of the type <paramref name="valueType"/> has a function or a delegate as its
    /// <paramref name="value"/> although its type does not say so. Only an object and an interface can hold one
    /// unseen, so a value of any other type is not looked at.
    /// </summary>
    let private isHiddenFunction (valueType : Type) (value : objnull) : bool =
        (Type.(=) (valueType, typeof<obj>) || valueType.IsInterface)
        && (
            match value with
            | null -> false
            | value -> isFunction (value.GetType ())
        )

    /// <summary>
    /// The error for a function value that would have to be a parameter of the query.
    /// </summary>
    let private functionValue (part : Expr) : TranslationError =
        part
        |> TranslationError.ofQuotation
            TranslationErrorCode.CapturedFunctionValue
            "A function that exists only as a compiled value is passed into the query. A query cannot use it, because its definition cannot be read, and a function is not a value that a parameter of a query can hold."
            (ImmutableArray.Create<string>(
                "Mark the function with [<ReflectedDefinition>], so that its definition can be read.",
                "Or write the function as a quotation and splice it into the query with %."
            ))

    /// <summary>
    /// The error for an argument of a function of <see cref="T:FSharp.Azure.Cosmos.Quotations.Query"/> that is not
    /// known before the query runs.
    /// </summary>
    let private notKnownBefore (marker : Marker) (call : Expr) : TranslationError =
        let name =
            match marker with
            | AsConstant -> nameof Query.constant
            | AsParameter -> nameof Query.parameter

        call
        |> TranslationError.ofQuotation
            TranslationErrorCode.Unsupported
            $"The argument of Query.%s{name} has to be known before the query runs, but it reads the document or holds a function written in the quotation."
            (ImmutableArray.Create<string> $"Pass Query.%s{name} a value that does not depend on the document.")

    /// <summary>
    /// The error for an argument of <see cref="M:FSharp.Azure.Cosmos.Quotations.Query.constant``1(``0)"/> that cannot
    /// be written into the text of a query as a literal.
    /// </summary>
    let private notLiteral (valueType : Type) (call : Expr) : TranslationError =
        call
        |> TranslationError.ofQuotation
            TranslationErrorCode.Unsupported
            $"The argument of Query.constant has to be a value that a query can hold as a literal: a boolean, a character, a string, an integer or a finite floating-point number of a built-in type, a value of an enumeration, or the null of a reference type that is no union of F#, as an option is. This one is a %s{valueType.Name}."
            (ImmutableArray.Create<string>
                "Use the value without Query.constant: it becomes a parameter of the query, which holds a value of any type.")

    /// <summary>
    /// The error for a variable that nothing in the quotation binds.
    /// </summary>
    let private unbound (variable : Var) (part : Expr) : TranslationError =
        part
        |> TranslationError.ofQuotation
            TranslationErrorCode.Unsupported
            $"The quotation reads the variable '%s{variable.Name}', which nothing in it binds, so the variable has no value."
            (ImmutableArray.Create<string>
                "Bind the variable with a function around the part that reads it, or put its value into the quotation.")

    /// <summary>
    /// The error for a construct that is in no quotation in normal form except as a part that is not computed.
    /// </summary>
    let private inexpressible (part : Expr) : TranslationError =
        part
        |> TranslationError.ofQuotation
            TranslationErrorCode.Unsupported
            "A query cannot express this part of the quotation: it is neither a value known before the query runs nor an operation on the document."
            ImmutableArray.Empty

    /// <summary>
    /// A slot that the walk has found.
    /// </summary>
    [<Struct>]
    type private Found = {
        /// The part of the quotation that the slot stands for, by which another use of the very same part is told.
        Part : Expr
        /// The name of the captured variable; none for a value that is computed.
        Name : string voption
        /// The type of the value.
        Type : Type
    }

    /// <summary>
    /// One walk over a quotation. It writes the tokens of the key and collects the slots with their values; when
    /// <paramref name="rebuild"/> is set, it also builds the quotation with a variable at the place of every slot.
    /// </summary>
    /// <remarks>
    /// Both results come from one walk, so that the key of a quotation is the same whether or not it is rebuilt.
    /// Rebuilding is left out when the key is all that is asked for: building a node of a quotation costs microseconds,
    /// and a quotation whose translation is already known needs no node built.
    /// </remarks>
    [<Sealed>]
    type private Walk (rebuild : bool) =

        let tokens = ImmutableArray.CreateBuilder<ShapeToken> 32
        let mutable hash = HashCode ()
        let values = ImmutableArray.CreateBuilder<objnull voption> 4
        let found = ResizeArray<Found> 4
        let slots = ImmutableArray.CreateBuilder<Slot>()
        // The variables that the functions around the current node bind, the innermost one last.
        let bound = ResizeArray<Var> 4
        // The nodes from the quotation down to the current node. When the walk is left, it tells where it was.
        let path = ResizeArray<Expr> 16
        // The parts that the evaluator has refused. The walk asks about a part and then about the parts under it, and
        // the evaluator went down those parts to refuse the first one, so it answers for them from here.
        let refused = Evaluator.Refused ()
        // What the walk has computed, node by node, so that a node that stands at several places is computed once,
        // also when the places are inside different parts.
        let computed = Evaluator.Computed ()
        // The node that was made a slot last, and the index of that slot: how a choice knows its condition.
        let mutable latestPart : Expr | null = null
        let mutable latestValue : objnull voption = ValueNone

        /// Writes a token into the key.
        let emit (kind : ShapeTokenKind) (number : int) (subject : objnull) (detail : objnull) : unit =
            let token = { Kind = kind; Number = number; Subject = subject; Detail = detail }

            tokens.Add token
            hash.Add token

        /// Whether a part can be computed before the query runs.
        let canCompute (part : Expr) : bool = Evaluator.canEvaluate refused part

        /// The value of a part that reads nothing of the document; leaves the walk when the code of the part raises.
        let compute (part : Expr) : objnull =
            match Evaluator.tryEvaluateOnce computed part with
            | Ok value -> value
            | Error error -> raise (Refusal error)

        /// The index of the slot that stands for this very part, or -1.
        let indexOf (part : Expr) : int =
            let mutable index = found.Count - 1

            while index >= 0
                  && not (obj.ReferenceEquals (found[index].Part, part)) do
                index <- index - 1

            index

        /// <summary>
        /// The type of the value of a part that becomes a slot or a constant. A function is refused here, before
        /// anything is computed for it.
        /// </summary>
        let valueTypeOf (part : Expr) : Type =
            let valueType = part.Type

            if isFunction valueType then
                raise (Refusal (functionValue part))

            valueType

        /// <summary>
        /// <paramref name="value"/>, the value of a part that becomes a slot or a constant. A function that is passed
        /// as an object is refused here: the type of the part did not say what it is.
        /// </summary>
        let noFunction (part : Expr) (valueType : Type) (value : objnull) : objnull =
            if isHiddenFunction valueType value then
                raise (Refusal (functionValue part))

            value

        /// The index of a new slot.
        let add (part : Expr) (valueType : Type) (name : string voption) (value : objnull voption) : int =
            let index = found.Count

            found.Add { Part = part; Name = name; Type = valueType }

            values.Add value

            if rebuild then
                let variableName =
                    match name with
                    | ValueSome name -> name
                    | ValueNone -> $"p%d{index}"

                slots.Add { Variable = Var (variableName, valueType); Name = name }

            index

        /// <summary>
        /// The index of the slot of a value that the quotation holds: a captured variable at one of the places it is
        /// used at, which has a <paramref name="name"/>, or a value that code put into a quotation it built, which
        /// has none. Another place is another slot unless it holds this very part: nothing else says that two parts
        /// are one variable, and their values must not decide it, or the key would depend on them.
        /// </summary>
        let held (part : Expr) (name : string voption) (value : objnull) : int =
            match indexOf part with
            | -1 ->
                let valueType = valueTypeOf part
                add part valueType name (ValueSome (noFunction part valueType value))
            | index -> index

        /// <summary>
        /// The index of the slot of a part that is computed. The part is computed when <paramref name="live"/> says
        /// that the quotation takes the branch it is in, and once for all the places the very same part is at.
        /// </summary>
        let computed (live : bool) (part : Expr) : int =
            match indexOf part with
            | -1 ->
                let valueType = valueTypeOf part

                add
                    part
                    valueType
                    ValueNone
                    (if live then
                         ValueSome (noFunction part valueType (compute part))
                     else
                         ValueNone)
            | index ->
                // The part was met before in a branch that is not taken, and is met now where its value is needed.
                if live && values[index].IsNone then
                    values[index] <- ValueSome (noFunction part found[index].Type (compute part))

                index

        /// Writes the token of a slot; the node that stands at the place of the slot from now on.
        let slot (index : int) (node : Expr) : Expr =
            let entry = found[index]

            let name : objnull =
                match entry.Name with
                | ValueSome name -> name
                | ValueNone -> null

            emit ShapeTokenKind.Slot index entry.Type name
            latestPart <- node
            latestValue <- values[index]

            if rebuild then Expr.Var slots[index].Variable else node

        /// Writes the token of a variable: how many bindings lie between the variable and its own.
        let variable (bindingOf : Var) (node : Expr) : unit =
            let mutable index = bound.Count - 1

            while index >= 0
                  && not (obj.ReferenceEquals (bound[index], bindingOf)) do
                index <- index - 1

            if index < 0 then
                raise (Refusal (unbound bindingOf node))

            emit ShapeTokenKind.Variable (bound.Count - 1 - index) null null

        /// <summary>
        /// The value of the condition of a choice that has just been walked, when it is known: the condition is a slot
        /// with a value. A literal is never a condition in normal form.
        /// </summary>
        let chosen (condition : Expr) : bool voption =
            if obj.ReferenceEquals (latestPart, condition) then
                match latestValue with
                | ValueSome (:? bool as value) -> ValueSome value
                | _ -> ValueNone
            else
                ValueNone

        /// <summary>
        /// Walks <paramref name="node"/> and the nodes under it. <paramref name="live"/> says whether the quotation
        /// takes the branch the node is in. The result is the node, or the node rebuilt when a slot is under it and
        /// the walk rebuilds.
        /// </summary>
        let rec visit (live : bool) (node : Expr) : Expr =
            path.Add node
            let visited = inspect live node
            path.RemoveAt (path.Count - 1)
            visited

        /// What a visit does for a node, between entering it and leaving it.
        and inspect (live : bool) (node : Expr) : Expr =
            // The kinds of nodes that a query is mostly made of come first: every case that does not match costs a
            // test, and a node is tested against the cases in their order.
            match node with
            | Var bindingOf ->
                variable bindingOf node
                node
            | ValueWithName (value, _, name) -> slot (held node (ValueSome name) value) node
            | Value (value, valueType) ->
                if isLiteral value valueType then
                    // A literal that is not null is of no function type. A null is the literal of every class, of a
                    // function type too, and there it is refused as a function is: the query can use neither.
                    let valueType =
                        match value with
                        | null -> valueTypeOf node
                        | _ -> valueType

                    emit ShapeTokenKind.Constant 0 valueType value
                    node
                else
                    slot (held node ValueNone value) node
            | PropertyGet (receiver, property, arguments) ->
                if canCompute node then
                    slot (computed live node) node
                else
                    emit ShapeTokenKind.PropertyGet 0 property null
                    members live node receiver arguments
            | Call (receiver, method, arguments) ->
                match method, arguments with
                | MarkerFunction marker, [ argument ] -> marked live marker node argument
                | _ when canCompute node -> slot (computed live node) node
                | _ ->
                    emit ShapeTokenKind.Call 0 method null
                    members live node receiver arguments
            | IfThenElse (condition, whenTrue, whenFalse) ->
                if canCompute node then
                    slot (computed live node) node
                else
                    emit ShapeTokenKind.IfThenElse 0 null null
                    let visitedCondition = visit live condition

                    // A branch that the quotation does not take is still walked: the key holds both branches,
                    // whichever is taken this time.
                    let taken = if live then chosen condition else ValueNone
                    let visitedWhenTrue = visit (live && taken <> ValueSome false) whenTrue
                    let visitedWhenFalse = visit (live && taken <> ValueSome true) whenFalse

                    if
                        obj.ReferenceEquals (visitedCondition, condition)
                        && obj.ReferenceEquals (visitedWhenTrue, whenTrue)
                        && obj.ReferenceEquals (visitedWhenFalse, whenFalse)
                    then
                        node
                    else
                        Expr.IfThenElse (visitedCondition, visitedWhenTrue, visitedWhenFalse)
            | Lambda (parameter, body) ->
                emit ShapeTokenKind.Lambda 0 parameter.Type null
                bound.Add parameter
                let visited = visit live body
                bound.RemoveAt (bound.Count - 1)

                if obj.ReferenceEquals (visited, body) then
                    node
                else
                    Expr.Lambda (parameter, visited)
            | NewUnionCase (case, items) ->
                emit ShapeTokenKind.NewUnionCase 0 case null
                parts live node items
            | NewTuple items ->
                emit ShapeTokenKind.NewTuple 0 node.Type null
                parts live node items
            | NewRecord (recordType, items) ->
                emit ShapeTokenKind.NewRecord 0 recordType null
                parts live node items
            | NewArray (itemType, items) ->
                emit ShapeTokenKind.NewArray items.Length itemType null
                parts live node items
            | Coerce (value, targetType) ->
                emit ShapeTokenKind.Coerce 0 targetType null
                parts live node [ value ]
            | DefaultValue defaultType ->
                if canCompute node && not (hasLiteralDefault defaultType) then
                    slot (computed live node) node
                else
                    // The default value of a function type is the null of it.
                    emit ShapeTokenKind.DefaultValue 0 (valueTypeOf node) null
                    node
            | NewDelegate (delegateType, parameters, body) ->
                emit ShapeTokenKind.NewDelegate parameters.Length delegateType null
                bound.AddRange parameters
                let visited = visit live body
                bound.RemoveRange (bound.Count - parameters.Length, parameters.Length)

                if obj.ReferenceEquals (visited, body) then
                    node
                else
                    Expr.NewDelegate (delegateType, parameters, visited)
            // Every kind of node below is computed as a whole when the evaluator can compute it.
            | _ when canCompute node -> slot (computed live node) node
            | FieldGet (receiver, field) ->
                emit ShapeTokenKind.FieldGet 0 field null
                members live node receiver []
            | NewObject (constructor, arguments) ->
                emit ShapeTokenKind.NewObject 0 constructor null
                parts live node arguments
            | TupleGet (tuple, index) ->
                emit ShapeTokenKind.TupleGet index null null
                parts live node [ tuple ]
            | UnionCaseTest (union, case) ->
                emit ShapeTokenKind.UnionCaseTest 0 case null
                parts live node [ union ]
            | TypeTest (value, testedType) ->
                emit ShapeTokenKind.TypeTest 0 testedType null
                parts live node [ value ]
            | Application (func, argument) ->
                emit ShapeTokenKind.Application 0 null null
                parts live node [ func; argument ]
            | _ -> raise (Refusal (inexpressible node))

        /// <summary>
        /// Walks the argument of a function of <see cref="T:FSharp.Azure.Cosmos.Quotations.Query"/>, which says what
        /// the argument is. The call itself leaves no trace: it only says how its argument is read.
        /// </summary>
        and marked (live : bool) (marker : Marker) (call : Expr) (argument : Expr) : Expr =
            match marker, argument with
            | AsParameter, ValueWithName (value, _, name) -> slot (held argument (ValueSome name) value) call
            | AsParameter, Value (value, _) -> slot (held argument ValueNone value) call
            | AsParameter, _ when canCompute argument -> slot (computed live argument) call
            | AsConstant, _ when canCompute argument ->
                // The value is a part of the key, so it is computed whichever branch the call is in.
                let valueType = valueTypeOf argument
                let value = noFunction argument valueType (compute argument)

                // The key holds the value, and equal keys have to mean one text. Only a literal says by its equality
                // what is written for it: an object is equal to itself after it has changed, and two objects can be
                // equal and be written differently.
                if not (isLiteral value valueType) then
                    raise (Refusal (notLiteral valueType call))

                emit ShapeTokenKind.Constant 0 valueType value
                latestPart <- call
                latestValue <- ValueSome value

                if rebuild then Expr.Value (value, valueType) else call
            | _ -> raise (Refusal (notKnownBefore marker call))

        /// Walks the receiver and the arguments of a member.
        and members (live : bool) (node : Expr) (receiver : Expr option) (arguments : Expr list) : Expr =
            match receiver with
            | Some receiver -> parts live node (receiver :: arguments)
            | None -> parts live node arguments

        /// <summary>
        /// Walks <paramref name="items"/>, the nodes under <paramref name="node"/> in their order. The result is the
        /// node, or the node built anew over the items that the walk rebuilt.
        /// </summary>
        and parts (live : bool) (node : Expr) (items : Expr list) : Expr =
            if rebuild then
                let visited = items |> List.map (visit live)

                if List.forall2 (fun (item : Expr) (after : Expr) -> obj.ReferenceEquals (item, after)) items visited then
                    node
                else
                    match node with
                    | ShapeCombination (shape, _) -> RebuildShapeCombination (shape, visited)
                    | _ -> node
            else
                for item in items do
                    visit live item |> ignore

                node

        /// <summary>
        /// Walks <paramref name="expr"/>, which the walk has not been used for before; the quotation, rebuilt when the
        /// walk rebuilds.
        /// </summary>
        member _.Run (expr : Expr) : Expr = visit true expr

        /// <summary>
        /// <paramref name="error"/>, which the walk was left with, with a place in the source: its own, or the place
        /// of the nearest node around the part it is about that has one. A captured value has none of its own, and a
        /// node that the normalizer built has the place of a part under it.
        /// </summary>
        member _.Located (error : TranslationError) : TranslationError =
            if error.Location.IsSome then
                error
            else
                let mutable index = path.Count - 1
                let mutable place = ValueNone

                while index >= 0 && place.IsNone do
                    place <- SourceRange.tryOfQuotation path[index]
                    index <- index - 1

                { error with Location = place }

        /// The key of the quotation that was walked.
        member _.Key : ShapeKey = { Tokens = tokens.ToImmutable (); Hash = hash.ToHashCode () }

        /// The values of the slots of the quotation that was walked.
        member _.Values : ImmutableArray<objnull voption> = values.ToImmutable ()

        /// The slots of the quotation that was walked, when the walk rebuilds.
        member _.Slots : ImmutableArray<Slot> = slots.ToImmutable ()

    /// <summary>
    /// The key of <paramref name="expr"/>, a quotation in normal form, and the values of its slots, or the reason it
    /// has none. This is all that a quotation needs when its translation is known already.
    /// </summary>
    /// <remarks>
    /// These are errors:
    /// <list type="bullet">
    /// <item><description>a part that has to be computed raised an exception,
    /// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.ClientEvaluationFailed"/>;</description></item>
    /// <item><description>a function value would have to be a parameter,
    /// <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.CapturedFunctionValue"/>;</description></item>
    /// <item><description>the argument of a function of <see cref="T:FSharp.Azure.Cosmos.Quotations.Query"/> depends
    /// on the document, the argument of
    /// <see cref="M:FSharp.Azure.Cosmos.Quotations.Query.constant``1(``0)"/> is no value that a query can hold as a
    /// literal, a variable is bound nowhere, or the quotation holds a construct that is neither a value nor an
    /// operation on the document, <see cref="T:FSharp.Azure.Cosmos.Quotations.TranslationErrorCode.Unsupported"/>.
    /// </description></item>
    /// </list>
    /// </remarks>
    let shapeOf (expr : Expr) : Result<Shape, TranslationError> =
        let walk = Walk false

        try
            walk.Run expr |> ignore

            Ok { Key = walk.Key; Values = walk.Values }
        with :? Refusal as refusal ->
            Error (walk.Located refusal.Error)

    /// <summary>
    /// <paramref name="expr"/>, a quotation in normal form, prepared for translation, or the reason it cannot be:
    /// the errors of <see cref="M:FSharp.Azure.Cosmos.Quotations.Parameterizer.shapeOf(Microsoft.FSharp.Quotations.FSharpExpr)"/>.
    /// The key and the values are the ones that function gives.
    /// </summary>
    let templateOf (expr : Expr) : Result<Template, TranslationError> =
        let walk = Walk true

        try
            let body = walk.Run expr

            Ok { Key = walk.Key; Values = walk.Values; Body = body; Slots = walk.Slots }
        with :? Refusal as refusal ->
            Error (walk.Located refusal.Error)
