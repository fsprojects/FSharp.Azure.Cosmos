namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open System.Collections.Immutable
open BenchmarkDotNet.Attributes

open FSharp.Azure.Cosmos.Sql

/// <summary>
/// What a query costs once, when its translation is not cached yet, after the translation itself: the
/// <see cref="T:FSharp.Azure.Cosmos.Sql.SqlQuery"/> is built, validated and printed.
/// </summary>
[<MemoryDiagnoser>]
type SqlModelBenchmarks () =

    let query = SampleQuery.syntaxTree ()

    do
        if not (SqlQuery.validate query).IsEmpty then
            invalidOp "The sample query should be valid."

    [<Benchmark(Description = "Build the syntax tree")>]
    member _.Build () : SqlQuery = SampleQuery.syntaxTree ()

    [<Benchmark(Description = "Validate")>]
    member _.Validate () : ImmutableArray<ValidationError> = SqlQuery.validate query

    [<Benchmark(Description = "Print with parameters")>]
    member _.Print () : string = Printer.print PropertyStyle.Brackets query

    [<Benchmark(Description = "Print with the values inline")>]
    member _.PrintInline () : Result<string, ImmutableArray<ValidationError>> =
        Printer.printInline PropertyStyle.Brackets SampleQuery.literalOf query
