// The stages of the translator are internal: a consumer sees the translator as a whole. Their tests call each stage
// directly, so the test assembly sees the internals. AssemblyInfo.fs cannot hold the attribute, because FAKE rewrites
// that file for every release.
namespace FSharp.Azure.Cosmos.Quotations

open System.Runtime.CompilerServices

[<assembly : InternalsVisibleTo "FSharp.Azure.Cosmos.Quotations.Tests">]
do ()
