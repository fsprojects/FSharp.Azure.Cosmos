namespace Tests.Integration

open System.Diagnostics
open System.Net
open System.Runtime.InteropServices
open FSharp.Azure.Cosmos.Create
open FSharp.Azure.Cosmos.Delete
open FSharp.Azure.Cosmos.Read
open FSharp.Azure.Cosmos.Replace
open Microsoft.Azure.Cosmos
open Microsoft.VisualStudio.TestTools.UnitTesting

[<AbstractClass; Sealed; DebuggerNonUserCode>]
type CosmosAssert private () =

    static member WantOk<'T> (response : ItemResponse<'T>, [<Optional>] message) =
        match response.StatusCode with
        | HttpStatusCode.OK
        | HttpStatusCode.Created -> response.Resource
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsOk (response : ItemResponse<'T>, [<Optional>] message) = CosmosAssert.WantOk (response, message) |> ignore

    static member WantOk<'T> (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.Ok ok -> ok
        | _ ->
            Assert.Fail ($"Expected CreateResult.Ok, but {result} given. {message}")
            Unchecked.defaultof<_>

    static member IsOk (result : CreateResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.Ok ok -> ok
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsOk (result : ReadResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : ReplaceResult<'T>, [<Optional>] message) =
        match result with
        | ReplaceResult.Ok ok -> ok
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsOk (result : ReplaceResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantOk<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.Ok ok -> ok
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsOk (result : DeleteResult<'T>, [<Optional>] message) = CosmosAssert.WantOk (result, message) |> ignore

    static member WantNotFound<'T> (result : ReadResult<'T>, [<Optional>] message) =
        match result with
        | ReadResult.NotFound response -> response
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsNotFound (result : ReadResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantNotFound<'T> (result : DeleteResult<'T>, [<Optional>] message) =
        match result with
        | DeleteResult.NotFound response -> response
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsNotFound (result : DeleteResult<'T>, [<Optional>] message) =
        CosmosAssert.WantNotFound (result, message) |> ignore

    static member WantConflict (result : CreateResult<'T>, [<Optional>] message) =
        match result with
        | CreateResult.IdAlreadyExists _ -> ()
        | _ ->
            Assert.Fail (message)
            Unchecked.defaultof<_>

    static member IsConflict (result : CreateResult<'T>, [<Optional>] message) =
        CosmosAssert.WantConflict (result, message) |> ignore
