# Copilot Instructions

## Project Details

* .NET SDK pinned in #file:'global.json'
* Common parameters specified in #file:'Directory.Build.props'
* Central NuGet package version management – versions go in #file:'Directory.Packages.props', not in `.fsproj` files
* Build: `dotnet build FSharp.Azure.Cosmos.slnx`
* Test: `dotnet test FSharp.Azure.Cosmos.slnx` (requires Azure Cosmos DB Emulator – see below)

## Solution Structure

```text
/
├── src/Cosmos.Sql/      – Cosmos DB SQL model (FSharp.Azure.Cosmos.Sql)
│   ├── EquatableArray.fs – immutable array with structural equality
│   └── ImmutableArrayPatterns.fs – active patterns that match an `ImmutableArray` by its length
├── src/Cosmos/          – main library (FSharp.Azure.Cosmos)
│   ├── SubStatusCodes.fs – documented Cosmos DB sub-status codes
│   ├── Cosmos.fs        – core types and container extensions
│   ├── Create.fs        – item creation operations
│   ├── Read.fs          – single-item read operations
│   ├── ReadMany.fs      – multi-item read operations
│   ├── Replace.fs       – item replace operations
│   ├── Upsert.fs        – item upsert operations
│   ├── Patch.fs         – item patch operations
│   ├── Delete.fs        – item delete operations
│   ├── CosmosResponse.fs – response type wrappers
│   ├── TaskSeq.fs       – TaskSeq integration
│   └── UniqueKey.fs     – unique key helpers
├── src/Shared/          – source files that every F# project under src and tests compiles
│   └── ValueCollections.fs – `Seq`, `List` and `Array` functions that return `voption` and struct tuples
├── tests/Cosmos.Tests/  – MSTest integration test project
├── tests/Cosmos.Sql.Tests/ – MSTest unit tests of FSharp.Azure.Cosmos.Sql, no emulator needed
├── tests/Cosmos.Tests.Infrastructure/ – shared test fixtures, emulator settings and assertion helpers
├── build/               – FAKE build scripts
└── docsSrc/             – FSharp.Formatting documentation source
```

## Libraries in Use

* [`Microsoft.Azure.Cosmos`](https://github.com/Azure/azure-cosmos-dotnet-v3) – Azure Cosmos DB SDK v3
* [`FSharp.Control.TaskSeq`](https://github.com/fsprojects/FSharp.Control.TaskSeq) – `IAsyncEnumerable` / `taskSeq` support
* [`FSharp.Control.Reactive`](https://github.com/fsprojects/FSharp.Reactive) – Rx extensions for F#
* [`FsToolkit.ErrorHandling`](https://github.com/demystifyfp/FsToolkit.ErrorHandling) – `taskResult`, `Result`, `voption` CEs
* [`Unquote`](https://github.com/SwensenSoftware/unquote) – test assertions
* [`MSTest`](https://github.com/microsoft/testfx) – test framework

Use GitHub MCP tools for code search in these repositories when needed.

## MCP Servers

Agents discover servers from #file:'.mcp.json'; these are only hints on when to prefer one.

* For F# work, prefer the `F#` server ([FsLangMCP](https://github.com/Neftedollar/FsLangMCP)) over `rg`/plain text search whenever the task depends on symbol meaning, compile context, cross-project usage, diagnostics, or safe refactoring preview. Its tool package is pinned in #file:'.config/dotnet-tools.json' – run `dotnet tool restore` before first use.
* Use the `GitHub` server for code search in dependency repositories. In Claude Code it authenticates through `.claude/scripts/github-mcp-headers.ps1`, which reuses the GitHub token stored by Git Credential Manager, because the server does not support the OAuth dynamic client registration Claude Code needs.
* Use the `Microsoft Docs` server for official Microsoft and Azure documentation.
* When adding or changing a server, keep `servers` (VS Code / GitHub Copilot) and `mcpServers` (Claude Code) in sync and sorted alphabetically by key.

## F# Coding Guidelines

### Language and Tooling

* Always use the latest F# 10 features over old syntax.
* If you are running outside of an IDE, or the IDE does not provide F# semantic tools, use FsLangMCP as the primary tool for F# code navigation, symbol discovery, diagnostics, usage search and refactoring preview. Prefer its semantic tools over plain text search.
* The compiler generates `IsCaseName` instance properties (for example `IsOk`, `IsNotFound`) for each DU case – use them when a single-case check is needed.

### Asynchrony and Cancellation

* Prefer `task` CE over `async` CE.
* When a method must return non-generic `Task`, annotate the return type explicitly: `member _.MyMethod (...) : Task = task { ... }`. Never cast through pipelines.
* `task` CE can await `ValueTask` APIs directly.
* Curried functions take the `CancellationToken` as their **first** parameter, so that it can be partially applied.
* Never hand-roll wrappers such as `ValueTask (task { ... })`, `.AsTask ()` round-trips or manual `unit -> Task` thunks. Use the matching [`IcedTasks`](https://github.com/TheAngryByrd/IcedTasks) CE instead (`valueTask`, `valueTaskUnit`, `taskUnit`, `coldTask`, `cancellableTask`, `cancellableValueTask`, `backgroundTask`, …) and always use the full CE names, not the short aliases (`vTask`, `pvTask`, …). The maintainers confirmed the `IcedTasks` dependency on 2026-09-27. Debug builds on the pinned SDK run every resumable computation expression through its builder's dynamic implementation (dotnet/fsharp#20466), so library code uses a builder only once every one of its probes in `IcedTasksProbeTests` passes in both Debug and Release, none skipped. With `IcedTasks` 0.11.10-beta001, the first release whose dynamic implementation starts every cold and cancellable value with a state machine of its own (TheAngryByrd/IcedTasks#67), that holds for `valueTask`, `valueTaskUnit`, `cancellableTask` and `cancellableValueTask`; every other builder needs probes first. Referencing `IcedTasks` from the library makes it a transitive dependency for every consumer.

### Values and Collections

* Prefer `voption` (`ValueSome`/`ValueNone`) over `option`. Fields, members, parameters and values shared between threads included: none of them is a reason to pick `option`. Exception: when an API hands you `'T option` and has no `voption` counterpart, unwrap it with `Option.defaultValue`/`Option.defaultWith` directly – do not insert `ValueOption.ofOption` just to switch modules.
* The mirror case, an API that *takes* `'T option` (an optional argument `?name = …`, a field typed `'T option`): stay in `ValueOption` through the whole chain and convert once, last – `x |> ValueOption.bind _.Value |> ValueOption.toOption`, never `x |> ValueOption.toOption |> Option.bind _.Value`.
* The `Seq`, `List` and `Array` functions that return an option in FSharp.Core (`tryHead`, `tryLast`, `tryItem`, `tryExactlyOne`, `tryFind`, `tryFindBack`, `tryFindIndex`, `tryFindIndexBack`, `tryPick`) return a `voption` here, `tryPick` takes a chooser that returns one, and `zip` returns struct tuples: #file:'src/Shared/ValueCollections.fs' shadows them in the namespace `FSharp.Azure.Cosmos` of every F# project under `src` and `tests`, so they are in effect in the code of that namespace and in every file that opens it; a file in another namespace, a child namespace such as `FSharp.Azure.Cosmos.Tests` included, opens `FSharp.Azure.Cosmos` to get them. Match their results with `ValueSome`/`ValueNone`; when another function of this kind is needed, add its shadow to that file instead of converting at the call site. The build project keeps the functions of FSharp.Core.
* Prefer `struct ('T1 * 'T2)` over reference tuples, and anonymous struct records (`struct {| ... |}`) over tuples for return types of public functions and methods.
* Never group with `Seq.groupBy` – use `ToLookup` from `System.Linq`. It groups once into an `ILookup<'Key, 'T>` instead of re-grouping on every enumeration and does not allocate a tuple per group. Pass a lambda (`xs.ToLookup (fun x -> keyOf x)`), not a bare function value.
* When casting sequence items use `Seq.cast<TargetType>` instead of `Seq.map (fun item -> item :> TargetType)`.
* When concatenating two sequences or lists, prefer `seq { yield! xs; yield! ys }` (or `[ yield! xs; yield! ys ]` for lists) over the `@` operator or `Seq.append`.
* When pipe operators are used on a materializable collection multiple times in a row, prefer `Seq` module for the chain and materialize at the end.
* Keep a pipeline in harmony: when consecutive steps can come from one module, take them from that module, the conversion at the end included – `xs |> Seq.filter isValid |> Seq.sort |> Seq.toArray`, not `… |> Seq.sort |> Array.ofSeq`, and `items |> List.map _.Id |> List.toArray`, not `… |> Array.ofList`. Change the module only where the steps that follow need the other collection.

### Functions, Lambdas and Strings

* Prefer underscore lambda syntax like `Seq.map _.Name` over `Seq.map (fun x -> x.Name)`, but only when the expression is a simple member access. Complex expressions like `Seq.where (fun x -> x.Name = name)` or `Seq.map (fun x -> x.Field1, x.Field2)` cannot be simplified. A member chain ending in a method call is not complex: `_.Changed.Subscribe(handler)`. The shorthand needs its input type known, so pipe the value in first – `value |> ValueOption.map _.Id`, not `ValueOption.map _.Id value` (FS0072). Never write a space in `_.MethodCall()` – it breaks parsing.
* Simplify `Seq.map (fun x -> someFunction x)` to `Seq.map someFunction`.
* Prefer interpolated strings over `printf` functions for string formatting. Format specifiers like `$"%s{value}"` are valid in interpolated strings and help type inference.
* Pass an explicit `StringComparison` to every `Equals`, `StartsWith`, `EndsWith`, `IndexOf`, `Contains` and `Compare`, and an explicit comparer to every `HashSet<string>` and `Dictionary<string, _>`. `Ordinal` by default; culture-sensitive comparison is a decision, never a default. Use `OrdinalIgnoreCase` only where the thing compared really is case-insensitive – never for identifiers such as item ids or partition keys, which are case-sensitive.
* A slice of a string that is only inspected – compared, trimmed, scanned, matched against a prefix – is a `ReadOnlySpan<char>` (`text.AsSpan (start, length)`), not a `Substring`: the substring allocates a copy per call, the span does not. Materialize with `Substring`/`ToString ()` only for the value that leaves the function or is stored.
* The `StringComparison` rule applies to spans unchanged: `MemoryExtensions` has `StringComparison` overloads of `StartsWith`, `EndsWith`, `Equals`, `CompareTo`, `IndexOf` and `Contains` for `ReadOnlySpan<char>` – `s.AsSpan().TrimStart(' ').StartsWith("//", StringComparison.Ordinal)`. The overloads without one are the generic element-wise `ReadOnlySpan<'T>` ones – ordinal for `char` by accident, not by statement.
* A slice that must be stored – kept in a record or class field, captured by a closure or used across a `let!`/`do!` in a `task` CE, put into a tuple, option or collection – is `ReadOnlyMemory<char>` (`text.AsMemory (...)`), never a `ReadOnlySpan<char>`: a byref-like value cannot be stored. Returning a `ReadOnlySpan<char>` sliced from a string or from a span parameter is fine.
* To compare two slices without building either, use `String.CompareOrdinal (a, aIndex, b, bIndex, length)` or `spanA.Equals (spanB, StringComparison.Ordinal)`.

### Nullable Reference Types

* Declare variables non-nullable; check for `null` at entry points only.
* Trust the SDK null annotations – do not add null checks when the type system says a value cannot be null.
* Use `withNull` for null checks instead of boxing delegates/functions (avoid `isNull (box value)`).
* Prefer `match` on `null` over `if isNull` – it narrows the type and suppresses nullness warnings:

  ```fsharp
  // Preferred
  match someObject with
  | null -> ()
  | someObject -> someObject.SomeProperty
  ```

* Before suppressing a nullness warning (3261, 3262, …), exhaust these alternatives in order:
  1. `nonNull value` – asserts non-null at runtime and fails fast.
  2. `Unchecked.nonNull value` – skips the runtime check; only when non-null was already verified upstream.
  3. An inline `#nowarn` / `#warnon` pair around the smallest possible scope – last resort for interop boundaries, centralised in a single helper rather than scattered across call sites.
* Never suppress warnings file-wide; `#nowarn` and `#warnon` are valid anywhere in a file.

### XML Documentation Comments

* A doc comment is either plain text with no tags at all, or fully explicit XML starting with `<summary>` – never a mixture. The compiler adds `<summary>` by itself only when the comment has no tags, and silently escapes tags otherwise:
  * No tags anywhere → bare `///` lines, no `<summary>`.
  * Any tag at all (`<see/>`, `<c/>`, `<para>`, `<param>`, `<returns>`, …) → the comment must start with an explicit `<summary>`, and every `<para>` must be inside it.

  ```fsharp
  // ✅ plain text — the compiler supplies <summary>
  /// Represents the result of a read operation.

  // ❌ a sibling tag without <summary> — the <param> is escaped into the summary and lost
  /// Reads an item.
  /// <param name="id">Item Id</param>

  // ✅ any tag present, so the comment is explicit XML throughout
  /// <summary>Reads an item.</summary>
  /// <param name="id">Item Id</param>
  ```

* Every public API must have XML documentation.
* On an explicit interface implementation (`interface X with member _.M (...) = ...`), write `/// <inheritdoc />` alone instead of restating the interface member's documentation, unless this implementation has behavior worth calling out beyond what the interface already documents – write a normal `<summary>`/`<remarks>` there instead.
* Refer to types and members through `<see cref="Type.Member"/>`, never through `<c>` or plain text. `<c>` is for literal values only (JSON, SQL, setting names). Refer to language keywords through `<see langword="null"/>`.
* Split multi-paragraph documentation into `<para>` elements inside `<summary>` – bare line breaks are collapsed by documentation renderers.
* Write enumerations as `<list type="bullet">` (or `type="number"`) with `<item><description>…</description></item>`, never as Markdown-style bullets.

### Opens Sorting

Sort `open` statements alphabetically within groups: `System` first, `Microsoft` second, `FSharp` third; then other external namespaces; then this solution's namespaces. `open type` goes last in each group. Type and module aliases form a separate final group.

### Class Constructors

This is how to define a non-default F# class constructor:

```fsharp
type DerivedClass =
    inherit BaseClass

    new (``arguments here``) as ``created object``
        =
        // create any objects used in the base class constructor
        let fieldValue = ""
        {
            inherit
                BaseClass (``arguments here``)
        }
        then
            ``created object``.otherField <- fieldValue

    [<DefaultValue>]
    val mutable otherField : FieldType
```

### Class Instantiation

Always prefer F# class initializers over property assignment! **You absolutely must use F# class initializers instead of property assignment**!

Class declaration:

```fsharp
type MyClass (someConstructorParam : string) =
    member ReadOnlyProperty = someConstructorParam

    member val MutableProperty1 = "" with get, set
    member val MutableProperty2 = "" with get, set
```

Wrong:

```fsharp
let myClass = MyClass("some value")
myClass.MutableProperty1 <- "new value"
myClass.MutableProperty2 <- "new value"
```

Right:

```fsharp
let myClass =
    MyClass(
        // constructor parameters go first without names
        "some value",
        // then mutable properties go next with names
        MutableProperty1 = "new value",
        MutableProperty2 =
            // operations must be placed into parentheses
            (5 |> string)
    )
```

### C#-Consumable Extension Members

```fsharp
// AutoOpen makes the module automatically available without an explicit open statement
// Extension makes the members visible to C#
[<AutoOpen; Extension>]
module MyTypeExtensions =

    type MyType with

        // Extension is visible to C#
        // CompiledName makes the method name friendly to C#
        [<Extension; CompiledName "ExtensionMethod">]
        member this.ExtensionMethod (param1 : string) : ReturnType =
            ()
```

## Naming Conventions

* Use PascalCase for modules, types, and public members.
* Use camelCase for `let` bindings, functions, private fields, and local variables.
* Prefix interface names with `I` (e.g., `ICosmosContext`).
* Do not prefix type parameters with `T` (e.g., use `'Result` instead of `'TResult`).
* Name tests using spaces (e.g., `member this.``Test name with spaces``() : Task = ...`).

## Testing

* Tests use MSTest 4.
* Integration tests run against the **Azure Cosmos DB Emulator**.
* The emulator must be running locally or installed via the `copilot-setup-steps.yml` workflow.
* Emulator endpoint: `https://127.0.0.1:8081`
* Emulator primary key: `C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==`
* `COSMOS_EMULATOR_ENDPOINT` overrides the emulator endpoint the tests connect to.
* `COSMOS_EMULATOR_KEY` overrides the emulator key the tests authenticate with.
* `COSMOS_EMULATOR_PARTITION_COUNT` is the number of partitions the emulator offers (default `25`, the Windows emulator's default `/PartitionCount`); the fixtures keep at most that many test containers at a time, one partition each.
* `COSMOS_TEST_LEFTOVER_AGE_MINUTES` is how many minutes a `fsac-test-` database must stay unmodified before the leftover sweep of `[<AssemblyInitialize>]` and `[<AssemblyCleanup>]` deletes it (default `60`), so that the live databases of another test process on the same emulator are kept; `0` deletes every test database and is safe only while no other test process uses the emulator.
* Every test project under `tests/` references `tests/Cosmos.Tests.Infrastructure` through `tests/Directory.Build.props`; a new test project gets it without a `ProjectReference` of its own.
* `CollectionAssert` cannot work with F# lists – use F# array syntax (`[| ... |]`) instead.
* `StringAssert` has overloads with `StringComparison`.
* Use `Assert.Contains` instead of `Assert.IsTrue (str.Contains ..., "message")`, and do not put the actual value into the message.
* Check collection size with `Assert.HasCount (expected, collection, "message")` instead of `Assert.AreEqual (expected, collection.Length, "message")` (or `.Count` / `Seq.length`); use `Assert.IsEmpty`, `Assert.IsNotEmpty` and `Assert.ContainsSingle` for the zero, non-zero and single-item cases. On failure they report the actual count and items.
* Try running tests with `--no-build` first, run them individually where possible, and use the trx format for results so failures can be consumed and fixed.
* Use Unquote only for complex object/hierarchy assertions; for simple scalar checks prefer standard `Assert.*` APIs.
* Every `Assert.*` call **must include a failure message** so output is self-explanatory.
* Async tests must return `Task`, not `Async` or `Task<unit>` – always declare `) : Task = task {`.

## General

* Make only high-confidence suggestions when reviewing code changes.
* Write code with good maintainability practices, including comments on why certain design decisions were made.
* Handle edge cases and write clear exception handling.
* Never duplicate code unless explicitly allowed.
* All comments, documentation, README files, and markdown files must be written in **English only**.
