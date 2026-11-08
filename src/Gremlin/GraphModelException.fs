namespace FSharp.Gremlin

open System

/// <summary>
/// The exception that <see cref="T:FSharp.Gremlin.GraphModel"/> throws when it cannot describe how a graph stores a
/// record type.
/// </summary>
/// <remarks>
/// The message names the record type, the field when one field is the cause, and the reason.
/// </remarks>
type GraphModelException (recordType : Type, field : string voption, message : string, innerException : exn | null) =
    inherit Exception (message, innerException)

    /// <summary>
    /// Creates the exception for <paramref name="recordType"/> and, when one field is the cause, for
    /// <paramref name="field"/>.
    /// </summary>
    /// <param name="recordType">The record type that the model refuses.</param>
    /// <param name="field">The name of the field that the model refuses; none when the record type is the cause.</param>
    /// <param name="message">The message that names the record type, the field and the reason.</param>
    new (recordType : Type, field : string voption, message : string) = GraphModelException (recordType, field, message, null)

    /// The record type that the model refuses.
    member _.RecordType = recordType

    /// The name of the field that the model refuses; none when the record type itself is the cause.
    member _.Field = field
