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

[<AbstractClass; Sealed; DebuggerNonUserCode>]
type CosmosAssert private () =

    static member private GetMessageOrDefault (message : string) (defaultMessage : string) =
        if String.IsNullOrWhiteSpace message then
            defaultMessage
        else
            message

    static member WantOk<'T> (response : ItemResponse<'T>, [<Optional>] message) =
        match response.StatusCode with
        | HttpStatusCode.OK
        | HttpStatusCode.Created -> response.Resource
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected OK or Created but got {response.StatusCode}.")
            Unchecked.defaultof<_>

    static member IsOk (response : ItemResponse<'T>, [<Optional>] message) = CosmosAssert.WantOk (response, message) |> ignore

    static member WantOk<'T> (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected CreateResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : CreateResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReadResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : ReadResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : ReplaceResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : PatchResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : UpsertResult<'T>, [<Optional>] message) =
        match result with
        | UpsertResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected UpsertResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : UpsertResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.Ok ok -> ok
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.Ok but got {result}.")
            Unchecked.defaultof<_>

    static member IsOk (result : DeleteResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantNotFound<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReadResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    static member IsNotFound (result : ReadResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantNotFound<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    static member IsNotFound (result : DeleteResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantNotFound<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    static member IsNotFound (result : ReplaceResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantNotFound<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.NotFound response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.NotFound but got {result}.")
            Unchecked.defaultof<_>

    static member IsNotFound (result : PatchResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantModifiedBefore<'T> (result : UpsertResult<'T>, [<Optional>] message) =
        match result with
        | UpsertResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected UpsertResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : UpsertResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    static member WantModifiedBefore<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected ReplaceResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : ReplaceResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    static member WantModifiedBefore<'T> (result : PatchResult<'T>, [<Optional>] message) =
        match result with
        | PatchResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected PatchResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : PatchResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    static member WantModifiedBefore<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected DeleteResult.ModifiedBefore but got {result}.")
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : DeleteResult<'T>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    static member WantConflict (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.IdAlreadyExists _ -> ()
        | _ -> Assert.Fail (CosmosAssert.GetMessageOrDefault message $"Expected CreateResult.IdAlreadyExists but got {result}.")

    static member IsConflict (result : CreateResult<'T>, [<Optional>] message) =
        CosmosAssert.WantConflict (result, message) |> ignore

    static member WantCustomError<'T, 'E> (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | UpsertConcurrentResult.CustomError error -> error
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected UpsertConcurrentResult.CustomError but got {result}."
            )
            Unchecked.defaultof<_>

    static member WantCustomError<'T, 'E> (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | ReplaceConcurrentResult.CustomError error -> error
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected ReplaceConcurrentResult.CustomError but got {result}."
            )
            Unchecked.defaultof<_>

    static member WantModifiedBefore<'T, 'E> (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | UpsertConcurrentResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected UpsertConcurrentResult.ModifiedBefore but got {result}."
            )
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : UpsertConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore

    static member WantModifiedBefore<'T, 'E> (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        match result with
        | ReplaceConcurrentResult.ModifiedBefore response -> response
        | _ ->
            Assert.Fail (
                CosmosAssert.GetMessageOrDefault message $"Expected ReplaceConcurrentResult.ModifiedBefore but got {result}."
            )
            Unchecked.defaultof<_>

    static member IsModifiedBefore (result : ReplaceConcurrentResult<'T, 'E>, [<Optional>] message) =
        CosmosAssert.WantModifiedBefore (result, message) |> ignore
