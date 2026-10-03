/// <summary>
/// The built-in functions of the query language as data: one <see cref="T:FSharp.Azure.Cosmos.Sql.FunctionSpec"/> per
/// name, which the translator, the validator, the interpreter, the emulator capability gating and the generated
/// function coverage page share.
/// <para>
/// The catalog holds every name the SDK lists, except its internal names, and every name Microsoft Learn documents.
/// Entries the translator does not need yet are marked unsupported in their
/// <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.Status"/>, with the reason.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Sql.Catalog

open System
open System.Collections.Frozen
open System.Collections.Immutable

/// Every built-in function, grouped like the function index on Microsoft Learn.
let all : ImmutableArray<FunctionSpec> = CatalogData.functions

/// <summary>
/// Every built-in function by name. Function names are case-insensitive in the query language, so the lookup uses
/// <see cref="P:System.StringComparer.OrdinalIgnoreCase"/>.
/// </summary>
let byName : FrozenDictionary<string, FunctionSpec> =
    all.ToFrozenDictionary (_.Name, StringComparer.OrdinalIgnoreCase)

/// <summary>
/// Looks up the function named <paramref name="name"/>, in any letter case.
/// </summary>
/// <param name="name">The name of the function.</param>
let tryFind (name : string) =
    // The byref overload, because matching on the tuple form allocates a reference tuple per lookup
    let mutable spec = Unchecked.defaultof<FunctionSpec>
    if byName.TryGetValue (name, &spec) then
        ValueSome spec
    else
        ValueNone

/// <summary>
/// Builds a call of the built-in function named <paramref name="name"/> with <paramref name="arguments"/>, after
/// checking that the catalog knows the function, specifies it and accepts the number of arguments.
/// <para>
/// The call uses the catalog's spelling of the name, so the printed text does not depend on how the caller spelled it.
/// </para>
/// </summary>
/// <param name="name">The name of the function, in any letter case.</param>
/// <param name="arguments">The arguments of the call.</param>
/// <returns>
/// The call, or the <see cref="T:FSharp.Azure.Cosmos.Sql.CatalogError"/> that says why it cannot be built: the name is
/// unknown, the entry is not specified, or <see cref="P:FSharp.Azure.Cosmos.Sql.FunctionSpec.Arity"/> does not accept
/// the number of arguments.
/// </returns>
let call (name : string) (arguments : ImmutableArray<ScalarExpression>) : Result<ScalarExpression, CatalogError> =
    match tryFind name with
    | ValueNone -> Error (CatalogError.UnknownFunction name)
    | ValueSome spec ->
        match spec.Status with
        | FunctionStatus.Unsupported reason -> Error (CatalogError.Unsupported (spec.Name, reason))
        | FunctionStatus.Supported ->
            let count = if arguments.IsDefault then 0 else arguments.Length

            if spec.Arity.Accepts count then
                Ok (ScalarExpression.FunctionCall (FunctionRef.BuiltIn spec.Name, EquatableArray arguments))
            else
                Error (CatalogError.ArityMismatch (spec.Name, spec.Arity, count))
