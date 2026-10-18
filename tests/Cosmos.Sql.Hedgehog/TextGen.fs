/// Generators of text. The characters are ASCII letters and digits, so a value never depends on a culture or on a
/// dictionary of words.
[<RequireQualifiedAccess>]
module FSharp.Azure.Cosmos.Tests.Models.TextGen

open System
open Hedgehog
open Hedgehog.FSharp

// The longest text of the notes generator: long enough to be told from a name, short enough to read in a failure
[<Literal>]
let private MaxNotesLength = 128

let private capitalizeText (text : string) : string =
    if text.Length = 0 then
        text
    else
        String.Create (
            text.Length,
            text,
            fun target source ->
                source.CopyTo target
                target[0] <- Char.ToUpperInvariant target[0]
        )

/// <summary>
/// Generates the texts of <paramref name="source"/> with the first character in upper case, by the rules of the
/// invariant culture.
/// </summary>
/// <param name="source">The generator of the texts.</param>
let capitalize (source : Gen<string>) : Gen<string> = source |> Gen.map capitalizeText

/// <summary>
/// Generates a text of ASCII letters and digits that is <paramref name="minLength"/> to <paramref name="maxLength"/>
/// characters long.
/// </summary>
/// <param name="minLength">The shortest length.</param>
/// <param name="maxLength">The longest length.</param>
let nameString (minLength : int) (maxLength : int) : Gen<string> =
    Gen.alphaNum
    |> Gen.string (Range.linear minLength maxLength)

/// Generates a text of ASCII letters and digits that is up to 128 characters long and can be empty.
let notes : Gen<string> = nameString 0 MaxNotesLength
