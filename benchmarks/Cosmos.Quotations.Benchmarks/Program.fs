/// <summary>
/// Runs the benchmarks chosen on the command line, such as <c>--filter *QuotationBenchmarks*</c>; without arguments
/// BenchmarkDotNet asks which ones to run.
/// </summary>
module FSharp.Azure.Cosmos.Quotations.Benchmarks.Program

open BenchmarkDotNet.Running

[<EntryPoint>]
let main (arguments : string array) : int =
    BenchmarkSwitcher.FromAssembly(typeof<QuotationBenchmarks>.Assembly).Run arguments
    |> ignore

    0
