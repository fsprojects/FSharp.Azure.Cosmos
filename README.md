# FSharp.Azure.Cosmos

An F# idiomatic wrapper for [Azure Cosmos DB SDK](https://github.com/Azure/azure-cosmos-dotnet-v3) that provides strongly-typed response handling and computation expressions for all Cosmos DB operations.

## Features

### F# Idiomatic Operations
- Responds with discriminated unions for each Cosmos DB operation to handle status codes as values instead of exceptions
- F# computation expressions for all Cosmos DB operations:
  - Read
  - ReadMany
  - Create
  - Replace
  - Upsert
  - Delete
  - Patch
- Unique key definition through computation expressions
- Extension methods for executing operations defined with computation expressions

### Modern Query Support
- Query extensions that create `IAsyncEnumerable` (`TaskSeq`) from `FeedIterator`/`IQueryable`
- `CancellableTaskSeq` module

[Documentation](https://fsprojects.github.io/FSharp.Azure.Cosmos/)

---

## Builds

GitHub Actions |
:---: |
[![GitHub Actions](https://github.com/fsprojects/FSharp.Azure.Cosmos/workflows/Build%20main/badge.svg)](https://github.com/fsprojects/FSharp.Azure.Cosmos/actions?query=branch%3Amain) |
[![Build History](https://buildstats.info/github/chart/fsprojects/FSharp.Azure.Cosmos)](https://github.com/fsprojects/FSharp.Azure.Cosmos/actions?query=branch%3Amain) |

## NuGet

Package | Stable | Prerelease
--- | --- | ---
FSharp.Azure.Cosmos | [![NuGet Badge](https://buildstats.info/nuget/FSharp.Azure.Cosmos)](https://www.nuget.org/packages/FSharp.Azure.Cosmos/) | [![NuGet Badge](https://buildstats.info/nuget/FSharp.Azure.Cosmos?includePreReleases=true)](https://www.nuget.org/packages/FSharp.Azure.Cosmos/)

---

### Developing

Make sure the following **requirements** are installed on your system:

- [dotnet SDK](https://www.microsoft.com/net/download/core) 8.0 or higher

or

- [VSCode Dev Container](https://code.visualstudio.com/docs/remote/containers)


---

### Environment Variables

- `CONFIGURATION` will set the [configuration](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-build?tabs=netcore2x#options) of the dotnet commands.  If not set, it will default to Release.
  - `CONFIGURATION=Debug ./build.sh` will result in `-c` additions to commands such as in `dotnet build -c Debug`
- `ENABLE_COVERAGE` Will enable running code coverage metrics.  AltCover can have [severe performance degradation](https://github.com/SteveGilham/altcover/issues/57) so code coverage evaluation are disabled by default to speed up the feedback loop.
  - `ENABLE_COVERAGE=1 ./build.sh` will enable code coverage evaluation


---

### Building
> build.cmd <optional buildtarget> // on windows

> ./build.sh  <optional buildtarget>// on unix
---

### Build Targets

- `Clean` - Cleans artifact and temp directories.
- `DotnetRestore` - Runs [dotnet restore](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-restore?tabs=netcore2x) on the [solution file](https://docs.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file?view=vs-2019).
- [`DotnetBuild`](#Building) - Runs [dotnet build](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-build?tabs=netcore2x) on the [solution file](https://docs.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file?view=vs-2019).
- `FSharpAnalyzers` - Runs [BinaryDefense.FSharp.Analyzers](https://github.com/BinaryDefense/BinaryDefense.FSharp.Analyzers).
- `DotnetTest` - Runs [dotnet test](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test?tabs=netcore21) on the [solution file](https://docs.microsoft.com/en-us/visualstudio/extensibility/internals/solution-dot-sln-file?view=vs-2019).
- `GenerateCoverageReport` - Code coverage is run during `DotnetTest` and this generates a report via [ReportGenerator](https://github.com/danielpalme/ReportGenerator).
- `ShowCoverageReport` - Shows the report generated in `GenerateCoverageReport`.
- `WatchTests` - Runs [dotnet watch](https://docs.microsoft.com/en-us/aspnet/core/tutorials/dotnet-watch?view=aspnetcore-3.0) with the test projects. Useful for rapid feedback loops.
- `GenerateAssemblyInfo` - Generates [AssemblyInfo](https://docs.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.applicationservices.assemblyinfo?view=netframework-4.8) for libraries.
- `DotnetPack` - Runs [dotnet pack](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-pack). This includes running [Source Link](https://github.com/dotnet/sourcelink).
- `SourceLinkTest` - Runs a Source Link test tool to verify Source Links were properly generated.
- `PublishToNuGet` - Publishes the NuGet packages generated in `DotnetPack` to NuGet via [paket push](https://fsprojects.github.io/Paket/paket-push.html). Runs only from `Github Actions`.
- `GitRelease` - Creates a commit message with the [Release Notes](https://fake.build/apidocs/v5/fake-core-releasenotes.html) and a git tag via the version in the `Release Notes`.
- `GitHubRelease` - Publishes a [GitHub Release](https://help.github.com/en/articles/creating-releases) with the Release Notes and any NuGet packages. Runs only from `Github Actions`.
- `FormatCode` - Runs [Fantomas](https://github.com/fsprojects/fantomas) on the solution file.
- `CheckFormatCode` - Runs [Fantomas --check](https://fsprojects.github.io/fantomas/docs/end-users/FormattingCheck.html) on the solution file.
- `BuildDocs` - Generates [Documentation](https://fsprojects.github.io/FSharp.Formatting) from `docsSrc` and the [XML Documentation Comments](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/xmldoc/) from your libraries in `src`.
- `WatchDocs` - Generates documentation and starts a webserver locally.  It will rebuild and hot reload if it detects any changes made to `docsSrc` files, or libraries in `src`.

---


### Releasing

- [Start a git repo with a remote](https://help.github.com/articles/adding-an-existing-project-to-github-using-the-command-line/)
git init
git add .
git commit -m "Scaffold"
git branch -M main
git remote add origin https://github.com/fsprojects/FSharp.Azure.Cosmos.git
git push -u origin main
- [Create an Environment](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment#creating-an-environment) on your repository named `nuget`.
- [Create a NuGet API key](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package#create-an-api-key)
- Add your `NUGET_TOKEN` to the [Environment Secrets](https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment#environment-secrets) of your newly created environment.
- Then update the `CHANGELOG.md` with an "Unreleased" section containing release notes for this version, in [KeepAChangelog](https://keepachangelog.com/en/1.1.0/) format.

NOTE: Its highly recommend to add a link to the Pull Request next to the release note that it affects. The reason for this is when the `RELEASE` target is run, it will add these new notes into the body of git commit. GitHub will notice the links and will update the Pull Request with what commit referenced it saying ["added a commit that referenced this pull request"](https://github.com/TheAngryByrd/MiniScaffold/pull/179#ref-commit-837ad59). Since the build script automates the commit message, it will say "Bump Version to x.y.z". The benefit of this is when users goto a Pull Request, it will be clear when and which version those code changes released. Also when reading the `CHANGELOG`, if someone is curious about how or why those changes were made, they can easily discover the work and discussions.

### Releasing Documentation

- Set Source for "Build and deployment" on [GitHub Pages](https://github.com/fsprojects/FSharp.Azure.Cosmos/settings/pages) to `GitHub Actions`.
- Documentation is auto-deployed via [GitHub Action](https://github.com/fsprojects/FSharp.Azure.Cosmos/blob/main/.github/workflows/fsdocs-gh-pages.yml) to [Your GitHub Page](https://fsprojects.github.io/FSharp.Azure.Cosmos/)
