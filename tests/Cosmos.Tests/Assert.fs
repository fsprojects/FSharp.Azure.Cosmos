namespace FSharp.Azure.Cosmos.Tests

open System.Runtime.InteropServices
open Microsoft.VisualStudio.TestTools.UnitTesting

[<AutoOpen>]
module AssertExtensions =

    type Assert with

        static member WantSome (value, [<Optional>] message : string | null) =
            match value with
            | Some some -> some
            | None ->
                Assert.Fail (message)
                Unchecked.defaultof<_>

        static member IsSome (value, [<Optional>] message : string | null) = Assert.WantSome (value, message) |> ignore

        static member IsNone (value, [<Optional>] message : string | null) =
            match value with
            | Some _ -> Assert.Fail (message)
            | None -> ()

        static member WantValueSome (value, [<Optional>] message : string | null) =
            match value with
            | ValueSome some -> some
            | ValueNone ->
                Assert.Fail (message)
                Unchecked.defaultof<_>

        static member IsValueSome (value, [<Optional>] message : string | null) = Assert.WantValueSome (value, message) |> ignore

        static member IsValueNone (value, [<Optional>] message : string | null) =
            match value with
            | ValueSome _ -> Assert.Fail (message)
            | ValueNone -> ()

        static member WantOk (value, [<Optional>] message : string | null) =
            match value with
            | Ok ok -> ok
            | Error error ->
                match message with
                | null -> Assert.Fail (string error)
                | message -> Assert.Fail ($"'{message}': {error}")
                Unchecked.defaultof<_>

        static member IsOk (value, [<Optional>] message : string | null) = Assert.WantOk (value, message) |> ignore

        static member WantError (value, [<Optional>] message : string | null) =
            match value with
            | Error error -> error
            | Ok value ->
                match message with
                | null -> Assert.Fail (string value)
                | message -> Assert.Fail ($"'{message}': {value}")
                Unchecked.defaultof<_>

        static member IsError (value, [<Optional>] message : string | null) = Assert.WantError (value, message) |> ignore

        static member inline IsDefaultOf< ^T> (value : ^T, [<Optional>] message : string) =
            Assert.AreEqual (box value, box Unchecked.defaultof< ^T>, message)

        static member inline OkEquals< ^R, 'E> (expected : ^R, actual : Result< ^R, 'E >, [<Optional>] message : string | null) =
            Assert.AreEqual (box expected, box (Assert.WantOk (actual, message)), message)

        static member inline ErrorEquals<'R, ^E> (expected : ^E, actual : Result<'R, ^E>, [<Optional>] message : string | null) =
            Assert.AreEqual (box expected, box (Assert.WantError (actual, message)), message)

        static member FailWithData<'T> ([<Optional>] message : string | null) =
            Assert.Fail (message)
            Unchecked.defaultof<'T>
