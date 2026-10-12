// The types and functions that the tests of the normalizer write their quotations over. They are made for the
// normalizer: every kind of function whose definition it can read is here once, next to functions it cannot read.
namespace FSharp.Azure.Cosmos.Quotations.Tests

open System.Collections.Immutable

/// A range of ages; a record that tests build in place and read a field of.
type AgeRange = { Low : int; High : int }

/// A way to choose persons; a union that tests build in place and match on.
type PersonFilter =
    /// The persons with this name.
    | ByName of name : string
    /// The persons older than this age.
    | OlderThan of age : int

/// A person, the document most quotations of the tests are written over.
type Person = {
    Name : string
    Age : int
    Active : bool
    Nick : string option
    Tags : string list
    /// The best and the worst score, a tuple that is a part of the document.
    Scores : int * int
} with

    /// A method with a reflected definition.
    [<ReflectedDefinition>]
    member this.IsOlderThan (age : int) = this.Age > age

    /// A method of two arguments, which its definition takes as one tuple.
    [<ReflectedDefinition>]
    member this.IsBetween (low : int, high : int) = this.Age >= low && this.Age <= high

    /// A curried method.
    [<ReflectedDefinition>]
    member this.IsWithin (low : int) (high : int) = this.Age >= low && this.Age <= high

    /// A property with a reflected definition.
    [<ReflectedDefinition>]
    member this.IsAdult = this.Age >= 18

    /// An inline method with an operator of a member constraint: the compiler passes it the person as the receiver
    /// and the addition as a witness.
    [<ReflectedDefinition>]
    member inline this.IsOlderThanTwice limit = this.Age > limit + limit

    /// A static method with a reflected definition.
    [<ReflectedDefinition>]
    static member IsNamed (name : string, person : Person) = person.Name = name

    /// An inline static method with an operator of a member constraint.
    [<ReflectedDefinition>]
    static member inline Doubled value = value + value

    /// A method without a reflected definition: a call of it stays a call.
    member this.IsRetired () = this.Age >= 65

/// The base of a class hierarchy, whose members are defined for the base class and called on a derived one.
[<AbstractClass>]
type DocumentBase (id : string) =

    /// The id of the document.
    member _.Id = id

    /// A method of the base class with a reflected definition.
    [<ReflectedDefinition>]
    member this.HasId (expected : string) = this.Id = expected

    /// A property of the base class with a reflected definition.
    [<ReflectedDefinition>]
    member this.IsIdentified = this.Id <> ""

    /// A member that a derived class can override.
    abstract Describe : unit -> string

    /// The definition of the member for the documents that do not override it. It has a reflected definition, which
    /// says nothing about a document that does.
    [<ReflectedDefinition>]
    default this.Describe () = "document " + this.Id

/// A derived class.
type Invoice (id : string, total : int) =
    inherit DocumentBase (id)

    /// The total of the invoice.
    member _.Total = total

    /// A static property with a reflected definition: its getter runs on every reading, unlike a module-level value.
    [<ReflectedDefinition>]
    static member LargeTotal = 100

    /// An invoice describes itself in its own way.
    override this.Describe () = "invoice " + this.Id

    /// A property of a struct type on a class: to call a member of its value, the compiler binds a copy of the value to
    /// a mutable variable.
    member val Note : string voption = ValueNone with get, set

/// A class with a property that can be set: an assignment is something a query cannot express.
type Counter () =

    /// The count.
    member val Count = 0 with get, set

/// A type that can apply itself, which gives a quotation that has no normal form without any recursive function.
type Loop = Loop of (Loop -> bool)

/// A folder in a tree of folders.
type Folder = {
    Name : string
    Parent : Folder option
} with

    /// A property with a reflected definition that reads itself, of the parent.
    [<ReflectedDefinition>]
    member this.Depth : int =
        match this.Parent with
        | Some parent -> parent.Depth + 1
        | None -> 0

/// A pass for values that counts them: a test tells by it how often a part of a quotation was computed.
type Meter () =

    let mutable readings = 0

    /// How many values have passed.
    member _.Readings = readings

    /// <summary>
    /// <paramref name="value"/>, counted.
    /// </summary>
    member _.Read (value : 'Value) : 'Value =
        readings <- readings + 1
        value

/// <summary>
/// Members added to <see cref="T:FSharp.Azure.Cosmos.Quotations.Tests.Person"/> outside its definition: the compiler
/// makes them static methods that take the person first.
/// </summary>
[<AutoOpen>]
module PersonExtensions =

    type Person with

        /// An extension method with a reflected definition.
        [<ReflectedDefinition>]
        member this.IsYoungerThan (age : int) = this.Age < age

        /// An extension method of two arguments.
        [<ReflectedDefinition>]
        member this.IsOutside (low : int, high : int) = this.Age < low || this.Age > high

        /// An extension property with a reflected definition.
        [<ReflectedDefinition>]
        member this.HasNick = this.Nick.IsSome

        /// A static extension property with a reflected definition.
        [<ReflectedDefinition>]
        static member AdultAge = 18

/// Functions over the model: with a reflected definition, which the normalizer writes into the quotation, and without.
module Conditions =

    /// A function of one argument.
    [<ReflectedDefinition>]
    let isAdult (person : Person) = person.Age >= 18

    /// A curried function.
    [<ReflectedDefinition>]
    let isOlderThan (age : int) (person : Person) = person.Age > age

    /// A function whose first argument is a tuple.
    [<ReflectedDefinition>]
    let isBetween (low : int, high : int) (person : Person) = person.Age >= low && person.Age <= high

    /// A function whose only argument is a tuple.
    [<ReflectedDefinition>]
    let isNamed (name : string, person : Person) = person.Name = name

    /// A function of the unit value.
    [<ReflectedDefinition>]
    let adultAge () = 18

    /// A generic function.
    [<ReflectedDefinition>]
    let hasValue (value : 'T option) = value.IsSome

    /// An inline function with an operator of a member constraint: the compiler passes it the addition as a witness.
    [<ReflectedDefinition>]
    let inline twice value = value + value

    /// A function that binds a value which no document gives and reads it twice: a call computes the value once, and
    /// every call computes it for itself.
    [<ReflectedDefinition>]
    let isWithinProcessors (person : Person) =
        let processors = System.Environment.ProcessorCount
        person.Age > processors && person.Age < processors * 10

    /// A function that calls other functions with reflected definitions.
    [<ReflectedDefinition>]
    let isWorkingAge (person : Person) = isAdult person && not (person |> isOlderThan 64)

    /// A function that takes a function and applies it under a function of its own.
    [<ReflectedDefinition>]
    let hasTagThat (predicate : string -> bool) (person : Person) = person.Tags |> List.exists (fun tag -> predicate tag)

    /// A function that reads a struct anonymous record, which the compiler does through the address of the record.
    [<ReflectedDefinition>]
    let isOlderThanMinimum (limits : struct {| MinAge : int |}) (person : Person) = person.Age > limits.MinAge

    /// A function that matches on a union, which a caller usually builds in place.
    [<ReflectedDefinition>]
    let satisfies (filter : PersonFilter) (person : Person) =
        match filter with
        | ByName name -> person.Name = name
        | OlderThan age -> person.Age > age

    /// A function that calls itself.
    [<ReflectedDefinition>]
    let rec factorial (number : int) : int = if number <= 1 then 1 else number * factorial (number - 1)

    /// A function that chooses between a condition and one that calls a function which calls itself. With a constant
    /// for its first argument, one of the two is code that never runs.
    [<ReflectedDefinition>]
    let isActiveOrCounted (onlyActive : bool) (person : Person) =
        if onlyActive then
            person.Active
        else
            factorial person.Age > 0

    /// A function that calls itself through another function.
    [<ReflectedDefinition>]
    let rec isEven (number : int) : bool = number = 0 || isOdd (number - 1)

    /// The other function of the pair.
    and [<ReflectedDefinition>] isOdd (number : int) : bool = number <> 0 && isEven (number - 1)

    /// A module-level value with a reflected definition. It is computed once, when the module is initialized, so the
    /// normalizer leaves it a reference.
    [<ReflectedDefinition>]
    let retirementAge = 65

    /// A module-level value that is a function of the unit value. The definition of a static property is such a
    /// function too, but this one is the value itself: the binding before the function keeps the compiler from making
    /// a function of the module of it.
    [<ReflectedDefinition>]
    let earlyRetirementAge : unit -> int =
        let age = retirementAge - 5
        fun () -> age

    /// A function without a reflected definition: a call of it stays a call.
    let isTeenager (person : Person) = person.Age >= 13 && person.Age <= 19

    /// A function without a reflected definition that returns a function. The binding before the function keeps the
    /// compiler from making one function of two arguments of it.
    let olderThan (age : int) : Person -> bool =
        let limit = age
        fun person -> person.Age > limit

    /// A module-level value that is a function: the result of a call, so there is no definition to read.
    let isSenior : Person -> bool = olderThan 59

/// The persons that the tests evaluate quotations on.
module Persons =

    /// A child without a nickname and without tags.
    let child = {
        Name = "Ann"
        Age = 9
        Active = true
        Nick = None
        Tags = []
        Scores = (3, 1)
    }

    /// An adult with a nickname and tags.
    let adult = {
        Name = "Bob"
        Age = 30
        Active = false
        Nick = Some "bobby"
        Tags = [ "vip"; "" ]
        Scores = (10, 7)
    }

    /// A person exactly at the age of majority.
    let eighteen = {
        Name = "Cy"
        Age = 18
        Active = true
        Nick = Some ""
        Tags = [ "new" ]
        Scores = (0, 0)
    }

    /// A retired person.
    let retired = {
        Name = "Dee"
        Age = 70
        Active = false
        Nick = None
        Tags = [ "vip" ]
        Scores = (65, 2)
    }

    /// Every person above.
    let all = ImmutableArray.Create (child, adult, eighteen, retired)
