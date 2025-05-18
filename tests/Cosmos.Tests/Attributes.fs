namespace FSharp.Azure.Cosmos

open Microsoft.VisualStudio.TestTools.UnitTesting

[<assembly : Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)>]

do ()
