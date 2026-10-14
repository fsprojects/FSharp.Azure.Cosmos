namespace FSharp.Azure.Cosmos.Quotations.Benchmarks

open System.Collections.Generic
open System.Collections.Immutable
open Microsoft.Azure.Cosmos
open FSharp.Quotations
open BenchmarkDotNet.Attributes

open FSharp.Azure.Cosmos.Sql

/// A translated query the way a plan cache holds it: the query text and, for every slot, the name of its parameter.
[<Struct>]
type CachedPlan = { Sql : string; ParameterNames : ImmutableArray<string> }

/// <summary>
/// What an execution of a query costs on the client before the request leaves, once its translation is cached: the
/// quotation literal is evaluated, walked for its
/// <see cref="T:FSharp.Azure.Cosmos.Quotations.Benchmarks.QueryShape"/>, the plan is found by the key, and the values
/// are bound to a new <see cref="T:Microsoft.Azure.Cosmos.QueryDefinition"/>.
/// </summary>
[<MemoryDiagnoser>]
type QuotationBenchmarks () =

    let minAge = 18
    let city = "Kyiv"
    let predicate = SampleQuery.predicate minAge city
    let shape = ShapeKey.compute predicate

    let plan = {
        Sql = Printer.print PropertyStyle.Brackets (SampleQuery.syntaxTree ())
        ParameterNames = SampleQuery.parameterNames
    }

    let plans = Dictionary<EquatableArray<int64>, CachedPlan>()

    do
        plans.Add (shape.Key, plan)

        // The prototype has no tests of its own, so what the numbers rest on is checked before anything is measured
        if shape.SlotValues.Length <> plan.ParameterNames.Length then
            invalidOp $"The predicate has {shape.SlotValues.Length} slots, the query {plan.ParameterNames.Length} parameters."

        if not (plans.ContainsKey (ShapeKey.compute (SampleQuery.predicate 21 "Lviv")).Key) then
            invalidOp "The same predicate over other values should have the same key."

        if plans.ContainsKey (ShapeKey.compute <@ fun (p : Person) -> p.Age > minAge && p.City = city @>).Key then
            invalidOp "Another predicate should have another key."

    let bind (plan : CachedPlan) (values : ImmutableArray<objnull>) : QueryDefinition =
        let mutable definition = QueryDefinition plan.Sql

        for index in 0 .. values.Length - 1 do
            definition <- definition.WithParameter (plan.ParameterNames[index], values[index])

        definition

    /// The cost FSharp.Core adds to every use of a quotation literal: it reads the pickled tree anew each time.
    [<Benchmark(Description = "Evaluate the quotation literal")>]
    member _.EvaluateLiteral () : Expr<Person -> bool> = SampleQuery.predicate minAge city

    [<Benchmark(Description = "Evaluate a literal of one condition")>]
    member _.EvaluateShortLiteral () : Expr<Person -> bool> = SampleQuery.shortPredicate minAge

    [<Benchmark(Description = "Shape key and slot values")>]
    member _.ComputeShape () : QueryShape = ShapeKey.compute predicate

    [<Benchmark(Description = "Find the plan by the key")>]
    member _.FindPlan () : CachedPlan = plans[shape.Key]

    [<Benchmark(Description = "QueryDefinition with 3 parameters")>]
    member _.Bind () : QueryDefinition = bind plan shape.SlotValues

    [<Benchmark(Description = "All four: one cached execution")>]
    member _.CachedExecution () : QueryDefinition =
        let shape = ShapeKey.compute (SampleQuery.predicate minAge city)
        bind plans[shape.Key] shape.SlotValues
