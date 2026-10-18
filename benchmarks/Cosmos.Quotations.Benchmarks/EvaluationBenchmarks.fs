namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open FSharp.Linq.RuntimeHelpers
open BenchmarkDotNet.Attributes

/// <summary>
/// What it costs to turn a subexpression that does not read the document into the value of a parameter, in two ways:
/// by reflection over its nodes, as <see cref="T:FSharp.Azure.Cosmos.Quotations.Benchmarks.ShapeKey"/> does, and by
/// <see cref="M:Microsoft.FSharp.Linq.RuntimeHelpers.LeafExpressionConverter.EvaluateQuotation(Microsoft.FSharp.Quotations.FSharpExpr)"/>,
/// which converts the quotation to a LINQ expression tree and compiles it.
/// </summary>
[<MemoryDiagnoser>]
type EvaluationBenchmarks () =

    let memberOfRecord = SampleQuery.minAgeOf { MinAge = 18; City = "Kyiv" }
    let clockArithmetic = SampleQuery.thirtyDaysAgo ()

    [<Benchmark(Description = "Reflection: filter.MinAge")>]
    member _.ReflectMember () : QueryShape = ShapeKey.compute memberOfRecord

    [<Benchmark(Description = "Compiled: filter.MinAge")>]
    member _.CompileMember () : objnull = LeafExpressionConverter.EvaluateQuotation memberOfRecord

    [<Benchmark(Description = "Reflection: UtcNow.AddDays -30.0")>]
    member _.ReflectClock () : QueryShape = ShapeKey.compute clockArithmetic

    [<Benchmark(Description = "Compiled: UtcNow.AddDays -30.0")>]
    member _.CompileClock () : objnull = LeafExpressionConverter.EvaluateQuotation clockArithmetic
