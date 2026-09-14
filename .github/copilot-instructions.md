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
├── src/Cosmos/          – main library (FSharp.Azure.Cosmos)
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
├── tests/Cosmos.Tests/  – MSTest integration test project
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

MCP server configuration lives in #file:'.mcp.json':

* `servers` – read by VS Code / GitHub Copilot.
* `mcpServers` – read by Claude Code. Mirrors the same servers; keep both sections in sync when adding or changing a server.

Local tool packages are pinned in #file:'.config/dotnet-tools.json' – run `dotnet tool restore` before first use.

| Server | Tool package | Command | Notes |
|---|---|---|---|
| `F#` | `fslangmcp` | `dotnet tool run fslangmcp` | Semantic F# MCP backed by the compiler and FSAC. Use it for cross-project symbol search, project/file outlines, diagnostics, rename previews, dead-code checks, and other F#-aware analysis that plain text search misses. See <https://github.com/Neftedollar/FsLangMCP>. |
| `GitHub` | – | HTTP | Code search in dependency repositories. |
| `Microsoft Docs` | – | HTTP | Official Microsoft and Azure documentation. |

For F# work, prefer FsLangMCP over `rg`/plain text search whenever the task depends on symbol meaning, compile context, cross-project usage, diagnostics, or safe refactoring preview.

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
* Never hand-roll wrappers such as `ValueTask (task { ... })`, `.AsTask ()` round-trips or manual `unit -> Task` thunks. Use the matching [`IcedTasks`](https://github.com/TheAngryByrd/IcedTasks) CE instead (`valueTask`, `valueTaskUnit`, `taskUnit`, `coldTask`, `cancellableTask`, `cancellableValueTask`, `backgroundTask`, …) and always use the full CE names, not the short aliases (`vTask`, `pvTask`, …). This library does not reference `IcedTasks` yet – adding it introduces a transitive dependency for every consumer, so confirm with the maintainers first.

### Values and Collections

* Prefer `voption` over `option`.
* Prefer `struct ('T1 * 'T2)` over reference tuples, and anonymous struct records (`struct {| ... |}`) over tuples for return types of public functions and methods.
* Never group with `Seq.groupBy` – use `ToLookup` from `System.Linq`. It groups once into an `ILookup<'Key, 'T>` instead of re-grouping on every enumeration and does not allocate a tuple per group. Pass a lambda (`xs.ToLookup (fun x -> keyOf x)`), not a bare function value.
* When casting sequence items use `Seq.cast<TargetType>` instead of `Seq.map (fun item -> item :> TargetType)`.
* When concatenating two sequences or lists, prefer `seq { yield! xs; yield! ys }` (or `[ yield! xs; yield! ys ]` for lists) over the `@` operator or `Seq.append`.
* When pipe operators are used on a materializable collection multiple times in a row, prefer `Seq` module for the chain and materialize at the end.

### Functions, Lambdas and Strings

* Prefer underscore lambda syntax like `Seq.map _.Name` over `Seq.map (fun x -> x.Name)`, but only when the expression is a simple member access. Complex expressions like `Seq.where (fun x -> x.Name = name)` or `Seq.map (fun x -> x.Field1, x.Field2)` cannot be simplified. Never write a space in `_.MethodCall()` – it breaks parsing.
* Simplify `Seq.map (fun x -> someFunction x)` to `Seq.map someFunction`.
* Prefer interpolated strings over `printf` functions for string formatting. Format specifiers like `$"%s{value}"` are valid in interpolated strings and help type inference.

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
