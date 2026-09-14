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

MCP server configuration lives in #file:'.mcp.json'. Local tool packages are pinned in #file:'.config/dotnet-tools.json' – run `dotnet tool restore` before first use.

| Server | Tool package | Command | Notes |
|---|---|---|---|
| `F#` | `fslangmcp` | `dotnet tool run fslangmcp` | Semantic F# MCP backed by the compiler and FSAC. Use it for cross-project symbol search, project/file outlines, diagnostics, rename previews, dead-code checks, and other F#-aware analysis that plain text search misses. See <https://github.com/Neftedollar/FsLangMCP>. |
| `GitHub` | – | HTTP | Code search in dependency repositories. |
| `Microsoft Docs` | – | HTTP | Official Microsoft and Azure documentation. |

For F# work, prefer FsLangMCP over `rg`/plain text search whenever the task depends on symbol meaning, compile context, cross-project usage, diagnostics, or safe refactoring preview.

## F# Coding Guidelines

### Language Preferences

* Always use the latest F# 10 features over old syntax.
* If you are running outside of an IDE, or the IDE does not provide F# semantic tools, use FsLangMCP as the primary tool for F# code navigation, symbol discovery, diagnostics, usage search and refactoring preview. Prefer its semantic tools over plain text search.
* Prefer `voption` over `option`.
* Prefer `task` CE over `async` CE.
* Prefer underscore lambda syntax like `Seq.map _.Name` over `Seq.map (fun x -> x.Name)`, but only when the expression is a simple member access. Complex expressions like `Seq.where (fun x -> x.Name = name)` or `Seq.map (fun x -> x.Field1, x.Field2)` cannot be simplified.
* Simplify `Seq.map (fun x -> someFunction x)` to `Seq.map someFunction`.
* When pipe operators are used on a materializable collection multiple times in a row, prefer `Seq` module for the chain and materialize at the end.
* Prefer interpolated strings over `printf` functions for string formatting.
* Use `withNull` for null checks instead of boxing delegates/functions (avoid `isNull (box value)`).

### Nullable Reference Types

* Declare variables non-nullable; check for `null` at entry points only.
* Trust the SDK null annotations – do not add null checks when the type system says a value cannot be null.
* Prefer `match` on `null` over `if isNull`:

  ```fsharp
  // Preferred
  match someObject with
  | null -> ()
  | someObject -> someObject.SomeProperty
  ```

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
* Use Unquote only for complex object/hierarchy assertions; for simple scalar checks prefer standard `Assert.*` APIs.
* Every `Assert.*` call **must include a failure message** so output is self-explanatory.
* Async tests must return `Task`, not `Async` or `Task<unit>` – always declare `) : Task = task {`.

## General

* Make only high-confidence suggestions when reviewing code changes.
* Write code with good maintainability practices, including comments on why certain design decisions were made.
* Handle edge cases and write clear exception handling.
* Never duplicate code unless explicitly allowed.
* All comments, documentation, README files, and markdown files must be written in **English only**.
