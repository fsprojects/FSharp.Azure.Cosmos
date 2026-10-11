# The SDK's parser of Cosmos DB SQL

The grammar and the generated sources of this folder are copied unchanged from the Azure Cosmos DB .NET SDK v3 (MIT, see `THIRD-PARTY-NOTICES.md` at the root of the repository), tag `3.62.0`, folder `Microsoft.Azure.Cosmos/src/Query/Core/Parser`:

| File | What it is |
|---|---|
| `sql.g4` | the grammar; it is not compiled here and is kept for reading |
| `sqlLexer.cs`, `sqlParser.cs` | the lexer and the parser that ANTLR 4.7.2 generated from it |
| `IsqlVisitor.cs`, `sqlBaseVisitor.cs`, `IsqlListener.cs`, `sqlBaseListener.cs` | the generated visitor and listener types that the parser refers to |

Nothing is generated in this repository, so the build needs neither Java nor the ANTLR tool, and the parser is the one the SDK runs.

The runtime is the package `Antlr4.Runtime.Standard` 4.9.3. It targets .NET Standard 2.0 and has no dependencies, where 4.7.2 targets .NET Standard 1.3. It must stay below 4.10, which changed the type of `SerializedAtn`: these sources do not compile against it (checked with 4.10.0 and 4.13.1).

The generated types are internal and have no namespace. `InternalsVisibleTo.cs` opens them to `FSharp.Azure.Cosmos.Sql.Grammar` (`tests/Cosmos.Sql.Grammar`), which converts a parse tree into the syntax tree of `FSharp.Azure.Cosmos.Sql`. That conversion cannot live in a test project: the SDK assembly, which every test project references, has internal types of the same names (`sqlParser`, `sqlLexer`), and F# then finds those and not these.

To take the files of another SDK version, first name its tag in this file and its tag and commit in `THIRD-PARTY-NOTICES.md`; then run `./Update-Generated.ps1 -Tag <tag>`, which copies nothing while those documents name another tag or commit, and build and run the tests.
