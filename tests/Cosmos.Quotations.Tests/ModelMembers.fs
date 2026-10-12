namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Reflection

/// <summary>
/// The members of a type of a test model that hold data, for the tests that compare what a layout says of every member
/// with a document that the serializer wrote.
/// </summary>
[<RequireQualifiedAccess>]
module ModelMembers =

    /// The members of a type that hold data: its properties and fields, also the ones that are not public, without
    /// what the compiler adds.
    let ofType (holderType : Type) : MemberInfo seq = seq {
        for found in
            holderType.GetMembers (
                BindingFlags.Instance
                ||| BindingFlags.Public
                ||| BindingFlags.NonPublic
            ) do
            match found with
            | :? PropertyInfo as property when property.GetIndexParameters().Length = 0 && property.CanRead -> found
            // The field that the compiler makes for a property has a name that no source can write.
            | :? FieldInfo as field when
                not (
                    field.Name.Contains ('@', StringComparison.Ordinal)
                    || field.Name.Contains ('<', StringComparison.Ordinal)
                )
                ->
                found
            | _ -> ()
    }

    /// The value that a member holds in a value of the model.
    let valueOf (holder : obj) (dataMember : MemberInfo) : objnull =
        match dataMember with
        | :? PropertyInfo as property -> property.GetValue holder
        | :? FieldInfo as field -> field.GetValue holder
        | other -> invalidOp $"The member is neither a property nor a field: %s{other.Name}"

    /// The type of the value of a member.
    let typeOf (dataMember : MemberInfo) : Type =
        match dataMember with
        | :? PropertyInfo as property -> property.PropertyType
        | :? FieldInfo as field -> field.FieldType
        | other -> invalidOp $"The member is neither a property nor a field: %s{other.Name}"
