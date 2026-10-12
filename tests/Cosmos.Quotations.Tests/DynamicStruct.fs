namespace FSharp.Azure.Cosmos.Quotations.Tests

open System
open System.Reflection
open System.Reflection.Emit

/// Structs that F# cannot declare, which a test makes at run time.
[<RequireQualifiedAccess>]
module internal DynamicStruct =

    /// <summary>
    /// A struct with one field of an integer, named Value, and a constructor without parameters, as C# can declare
    /// one and F# cannot. The default value of such a struct is the one with every field zero: the constructor runs for
    /// a value that <see cref="M:System.Activator.CreateInstance(System.Type)"/> makes, and never for the default
    /// value.
    /// </summary>
    /// <param name="name">The name of the struct.</param>
    /// <param name="writeConstructor">
    /// Writes the code of the constructor, from its code generator and the field, up to and including the return or the
    /// throw that ends it.
    /// </param>
    let withConstructor (name : string) (writeConstructor : ILGenerator -> FieldInfo -> unit) : Type =
        let declared =
            AssemblyBuilder
                .DefineDynamicAssembly(AssemblyName $"{name}.Dynamic", AssemblyBuilderAccess.Run)
                .DefineDynamicModule("Structs")
                .DefineType(
                    name,
                    TypeAttributes.Public
                    ||| TypeAttributes.Sealed
                    ||| TypeAttributes.SequentialLayout,
                    typeof<ValueType>
                )

        let field = declared.DefineField ("Value", typeof<int>, FieldAttributes.Public)

        let code =
            declared.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator()

        writeConstructor code field
        declared.CreateType ()
