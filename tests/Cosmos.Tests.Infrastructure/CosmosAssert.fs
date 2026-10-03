namespace FSharp.Azure.Cosmos.Tests.Integration

open System
open System.Diagnostics
open System.Net
open System.Runtime.InteropServices
open FSharp.Azure.Cosmos.Create
open FSharp.Azure.Cosmos.Delete
open FSharp.Azure.Cosmos.Patch
open FSharp.Azure.Cosmos.Read
open FSharp.Azure.Cosmos.Replace
open FSharp.Azure.Cosmos.Upsert
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Assertions on the results of the operations of this library, such as <see cref="CreateResult{T}"/>, and on
/// <see cref="ItemResponse{T}"/>.
/// <para>
/// Assertions such as <see cref="WantNotFound"/> return the payload of the expected case or fail the test; their twins
/// such as <see cref="IsNotFound"/> discard the payload. Without a message, the failure names the expected and the
/// actual case.
/// </para>
/// </summary>
[<AbstractClass; Sealed; DebuggerNonUserCode>]
type CosmosAssert private () =

    static member private GetMessageOrDefault (message : string) (defaultMessage : string) =
        if String.IsNullOrWhiteSpace message then
            defaultMessage
        else
            message

    /// <summary>
    /// Returns the resource of an HTTP 200 or 201 <paramref name="response"/>; fails the test with
    /// <paramref name="message"/> on any other status.
    /// </summary>
    static member WantOk<'T> (response : ItemResponse<'T>, [<Optional>] message) =
        match response.StatusCode with
        | HttpStatusCode.OK
        | HttpStatusCode.Created -> response.Resource
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected OK or Created but got {response.StatusCode}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="response"/> has HTTP status 200 or 201.
    /// </summary>
    static member IsOk (response : ItemResponse<'T>, [<Optional>] message) = CosmosAssert.WantOk (response, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="CreateResult.Ok"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantOk<'T> (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected CreateResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="CreateResult.Ok"/>.
    /// </summary>
    static member IsOk (result : CreateResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReadResult.Ok"/>; fails the test with <paramref name="message"/> on any other
    /// case.
    /// </summary>
    static member WantOk<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReadResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is <see cref="ReadResult.Ok"/>.
    /// </summary>
    static member IsOk (result : ReadResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReplaceResult.Ok"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantOk<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="ReplaceResult.Ok"/>.
    /// </summary>
    static member IsOk (result : ReplaceResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="PatchResult.Ok"/>; fails the test with <paramref name="message"/> on any other
    /// case.
    /// </summary>
    static member WantOk<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is <see cref="PatchResult.Ok"/>.
    /// </summary>
    static member IsOk (result : PatchResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="UpsertResult.Ok"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantOk<'T> (result : UpsertResult<'T>, [<Optional>] message) =
        match result with
        | UpsertResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected UpsertResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="UpsertResult.Ok"/>.
    /// </summary>
    static member IsOk (result : UpsertResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="DeleteResult.Ok"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantOk<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="DeleteResult.Ok"/>.
    /// </summary>
    static member IsOk (result : DeleteResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReadResult.NotFound"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantNotFound<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReadResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="ReadResult.NotFound"/>.
    /// </summary>
    static member IsNotFound (result : ReadResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="DeleteResult.NotFound"/>; fails the test with <paramref name="message"/> on
    /// any other case.
    /// </summary>
    static member WantNotFound<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="DeleteResult.NotFound"/>.
    /// </summary>
    static member IsNotFound (result : DeleteResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReplaceResult.NotFound"/>; fails the test with <paramref name="message"/> on
    /// any other case.
    /// </summary>
    static member WantNotFound<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="ReplaceResult.NotFound"/>.
    /// </summary>
    static member IsNotFound (result : ReplaceResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="PatchResult.NotFound"/>; fails the test with <paramref name="message"/> on any
    /// other case.
    /// </summary>
    static member WantNotFound<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="PatchResult.NotFound"/>.
    /// </summary>
    static member IsNotFound (result : PatchResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="UpsertResult.ModifiedBefore"/>; fails the test with <paramref name="message"/>
    /// on any other case.
    /// </summary>
    static member WantModifiedBefore<'T> (result : UpsertResult<'T>, [<Optional>] message) =
        match result with
        | UpsertResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected UpsertResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="UpsertResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : UpsertResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReplaceResult.ModifiedBefore"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantModifiedBefore<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="ReplaceResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : ReplaceResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="PatchResult.ModifiedBefore"/>; fails the test with <paramref name="message"/>
    /// on any other case.
    /// </summary>
    static member WantModifiedBefore<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="PatchResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : PatchResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="DeleteResult.ModifiedBefore"/>; fails the test with <paramref name="message"/>
    /// on any other case.
    /// </summary>
    static member WantModifiedBefore<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="DeleteResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : DeleteResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="CreateResult.IdAlreadyExists"/>.
    /// </summary>
    static member WantConflict (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.IdAlreadyExists _ -> ()
        | _ -> Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected CreateResult.IdAlreadyExists but got {result}.")

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="CreateResult.IdAlreadyExists"/>.
    /// </summary>
    static member IsConflict (result : CreateResult<'T>, [<Optional>] message) =
        CosmosAssert.WantConflict (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="UpsertConcurrentResult.CustomError"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantCustomError<'T, 'E> (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | UpsertConcurrentResult.CustomError error -> error
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected UpsertConcurrentResult.CustomError but got {result}."
            )
            Unchecked.defaultof<_>

    /// <summary>
    /// Returns the payload of <see cref="ReplaceConcurrentResult.CustomError"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantCustomError<'T, 'E> (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | ReplaceConcurrentResult.CustomError error -> error
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected ReplaceConcurrentResult.CustomError but got {result}."
            )
            Unchecked.defaultof<_>

    /// <summary>
    /// Returns the payload of <see cref="UpsertConcurrentResult.ModifiedBefore"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantModifiedBefore<'T, 'E> (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | UpsertConcurrentResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected UpsertConcurrentResult.ModifiedBefore but got {result}."
            )
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="UpsertConcurrentResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="ReplaceConcurrentResult.ModifiedBefore"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantModifiedBefore<'T, 'E> (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | ReplaceConcurrentResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected ReplaceConcurrentResult.ModifiedBefore but got {result}."
            )
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="ReplaceConcurrentResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="PatchConcurrentResult.CustomError"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantCustomError<'T, 'E> (result : PatchConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | PatchConcurrentResult.CustomError error -> error
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchConcurrentResult.CustomError but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Returns the payload of <see cref="PatchConcurrentResult.ModifiedBefore"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantModifiedBefore<'T, 'E> (result : PatchConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | PatchConcurrentResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected PatchConcurrentResult.ModifiedBefore but got {result}."
            )
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="PatchConcurrentResult.ModifiedBefore"/>.
    /// </summary>
    static member IsModifiedBefore (result : PatchConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    /// <summary>
    /// Returns the payload of <see cref="PatchConcurrentResult.NotFound"/>; fails the test with
    /// <paramref name="message"/> on any other case.
    /// </summary>
    static member WantNotFound<'T, 'E> (result : PatchConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | PatchConcurrentResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchConcurrentResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="result"/> is
    /// <see cref="PatchConcurrentResult.NotFound"/>.
    /// </summary>
    static member IsNotFound (result : PatchConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore
