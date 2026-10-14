# Benchmarks of the quotation translator

ADR 0001 plans a translator from F# quotations to Cosmos DB SQL. Its design rests on a cost model: a query is translated once, the translation is cached, and every later call only binds the values the quotation captured (section 11 of the ADR). These benchmarks measure the parts of that model that can be measured before the translator exists, and one request to the emulator to compare them with.

The project is a console application that the solution builds and nothing runs: it is run by hand.

## What is measured

| Class | Question |
|---|---|
| `QuotationBenchmarks` | What does a call cost on the client once its translation is cached? The quotation literal is evaluated, walked once for its cache key and its captured values, the cached query text is found, and the values are bound to a new `QueryDefinition`. |
| `EvaluationBenchmarks` | What does it cost to turn a subexpression that does not read the document, such as `filter.MinAge` or `DateTimeOffset.UtcNow.AddDays -30.0`, into the value of a parameter: by reflection over its nodes, or by compiling it the way `LeafExpressionConverter.EvaluateQuotation` does? |
| `SqlModelBenchmarks` | What does `FSharp.Azure.Cosmos.Sql` add to a translation that is not cached yet: building, validating and printing the syntax tree? |
| `RoundTripBenchmarks` | What does one request cost: a point read and a query against the Cosmos DB Emulator? |

The sample query is

```fsharp
<@ fun p -> p.Age >= minAge && p.City = city && p.Nick.IsSome && p.Tags.Contains "vip" @>
```

and its translation, written by hand in `SampleQuery.fs` until the translator exists:

```sql
SELECT VALUE p FROM p WHERE ((((p["Age"] >= @minAge) AND (p["City"] = @city)) AND (IS_DEFINED(p["Nick"]) AND (NOT IS_NULL(p["Nick"])))) AND ARRAY_CONTAINS(p["Tags"], @p0))
```

`ShapeKey.fs` is a prototype of the pass that computes the cache key. A quotation cannot be a dictionary key as it is: every evaluation of a literal builds a new tree, and two such trees are not equal. The pass walks the tree once and writes down its structure, with the values taken out, as a sequence of numbers. It is written to be measured: it covers the nodes of the sample query and refuses every other one.

## Running

```powershell
# No emulator needed
dotnet run -c Release --project benchmarks/Cosmos.Quotations.Benchmarks -- --filter *QuotationBenchmarks* *EvaluationBenchmarks* *SqlModelBenchmarks*

# Needs a running Cosmos DB Emulator; creates one database with one container of 100 items and deletes it afterwards
dotnet run -c Release --project benchmarks/Cosmos.Quotations.Benchmarks -- --filter *RoundTripBenchmarks*
```

BenchmarkDotNet writes its reports to `BenchmarkDotNet.Artifacts`, which Git ignores. On a machine that does other work at the same time a benchmark may take a hundred iterations to settle; `--maxIterationCount 20` bounds that.

The round trip benchmarks read the emulator's endpoint and key the way the tests do (`COSMOS_EMULATOR_ENDPOINT`, `COSMOS_EMULATOR_KEY`), and name their database with the prefix of the test databases, so the sweep of a later test run deletes it if a benchmark run is aborted.

## Results

Measured on 2026-10-11: AMD Ryzen AI Max+ 395, Windows 11, .NET 10.0.12, FSharp.Core 10.0.100, BenchmarkDotNet 0.15.8 with `--maxIterationCount 20`, the Windows Cosmos DB Emulator on the same machine. The machine did other work at the time. Three runs of the same benchmarks within two hours differed by a factor of up to three, so read the times as orders of magnitude; the allocations were the same in every run. The last row of the first table is lower than its first row for that reason: the load changed while the run went on.

A call whose translation is cached:

| Step | Mean | Allocated |
|---|---:|---:|
| Evaluate the quotation literal (four conditions) | 74.7 µs | 55.0 KB |
| Evaluate a literal of one condition | 23.7 µs | 21.8 KB |
| Shape key and slot values | 0.89 µs | 2.9 KB |
| Find the plan by the key | 0.11 µs | 0 |
| `QueryDefinition` with 3 parameters | 0.05 µs | 0.5 KB |
| The literal of four conditions and the three steps after it together | 53.6 µs | 58.4 KB |

A value that does not come from the document:

| Expression | By reflection | Compiled |
|---|---:|---:|
| `filter.MinAge` | 0.23 µs | 98 µs |
| `DateTimeOffset.UtcNow.AddDays -30.0` | 0.42 µs | 705 µs |

The SQL model:

| Step | Mean | Allocated |
|---|---:|---:|
| Build the syntax tree | 0.26 µs | 1.5 KB |
| Validate | 0.24 µs | 0.2 KB |
| Print with parameters | 0.59 µs | 2.0 KB |
| Print with the values inline | 0.98 µs | 4.2 KB |

One request to the emulator:

| Request | Mean | Allocated |
|---|---:|---:|
| Point read of one item | 1.38 ms | 46 KB |
| Query in one partition, 10 of 100 items | 2.12 ms | 150 KB |

## What the numbers say

1. **The quotation literal is the cost of a cached call.** FSharp.Core keeps a literal as pickled bytes and unpickles the whole tree on every evaluation of `<@ … @>`, resolving every type, method and property through reflection again. That is 24 µs for one condition and 75 µs for four, against about 1 µs for everything this library does afterwards: the walk, the lookup and the binding. A cache of translations takes the translation off the path of a call, but it cannot make an inline quotation cheaper than this, because the library only receives the tree once it is built.
2. **A query that is held in a value pays none of it.** The compiled queries of the ADR evaluate their quotation once and take what changes as arguments. On a path where 25 to 75 µs per call matter, that is the form to use; against a request of 1.4 to 2.1 ms an inline quotation adds between 1 and 5 percent.
3. **Values are evaluated by reflection, never compiled.** Reflection over the nodes costs a fraction of a microsecond; compiling the same expression costs 400 to 1700 times as much, as much as the quotation literal itself or ten times more.
4. **The SQL model is not a cost.** Building, validating and printing the tree of the sample query takes about 1 µs together.
