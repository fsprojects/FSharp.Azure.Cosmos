# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
* BREAKING: the package targets `net10.0` only; `net8.0` is no longer supported, so the next release is a major version (2.0.0)
* `Microsoft.Azure.Cosmos` updated from 3.60.0 to 3.62.0, which becomes the minimum version for consumers. Its release notes list a breaking change of their own: thin client mode is enabled by default and does not support resource-token authentication (opt out with `AZURE_COSMOS_THIN_CLIENT_ENABLED=false`)
* Removed the unused `System.Linq.Async` central package version pin: no project referenced the package directly or transitively, and on `net10.0` `System.Linq.AsyncEnumerable` is part of the framework

## [1.2.0] - 2026-10-01

### Added
* `ResponseMessage.SubStatusCode` extension property and `ResponseMessage.getSubStatusCode` function (`ResponseMessageModule.GetSubStatusCode` for C#) to read the Cosmos DB sub-status code of a stream response
* `SubStatusCodes` constants for the Cosmos DB sub-status codes from Microsoft Learn and the official .NET, Python, Java and Rust SDKs, each documented with the HTTP status it goes with

### Fixed
* `ExistsAsync` intermittently returned `false` for an existing item under concurrent calls: the shared query definition had its `@Id` parameter overwritten by other calls ([#31](https://github.com/fsprojects/FSharp.Azure.Cosmos/issues/31))
* `ExistsAsync` with a partition key now uses a point read instead of a query, falling back to the query for a prefix of a hierarchical partition key, and throws on failures other than a missing item (including a missing container) instead of reporting the item as missing

## [1.1.0] - 2026-09-15

### Added
`patchConcurrenly` / `patchConcurrenlyAndRead` computation expressions and `Container.ExecuteConcurrentlyAsync` for `PatchConcurrentlyOperation`: read the item, compute patch operations from it, apply them with the read eTag, retry on 412 (`PatchConcurrentResult`)

### Changed
Return `ValueOption` from Cosmos DB exception unwrappers

### Fixed
* `delete { eTag }` now sets `IfMatchEtag` instead of `IfNoneMatchEtag`, which the Cosmos SDK ignores on writes; a stale eTag now surfaces as a new `DeleteResult.ModifiedBefore` (412)
* `ExistsAsync` and `IsNotDeletedAsync` now treat any positive count as a match instead of requiring `count = 1`, fixing a false negative when the same id exists in more than one logical partition
* `IsNotDeletedAsync` now uses bracket notation for the deleted-marker field name, fixing a query syntax failure when the field name is a reserved Cosmos SQL keyword (e.g. `value`)
* `replaceConcurrenly` / `upsertConcurrenly` now send the builder's request options (session token, consistency level, indexing directive, triggers, content response) instead of silently dropping them; as a result the non-`AndRead` variants no longer return the item in `Ok` — use `replaceConcurrenlyAndRead` / `upsertConcurrenlyAndRead` to get it

## [1.0.1] - 2025-08-08

### Fixed
Count query for `CountAsync` container extension method

## [1.0.0] - 2025-05-22

First release

### Added response discriminated unions for each Cosmos DB operation
They allow to handle all the relevant status codes which can be considered as errors instead of exceptions.

### Added computation expressions for all Cosmos DB operations
* Read
* ReadMany
* Create
* Replace
* Upsert
* Delete
* Patch

### Added computation expressions for unique key definition

### Added extension methods to execute operations defined with computation expressions

### Added extension methods to perform queries on Cosmos DB
* Create `IAsyncEnumerable` (`TaskSeq`) from a `FeedIterator`/`IQueryable`
* Provide `CancellationToken` to `TaskSeq` using `CancellableTaskSeq` module
[Unreleased]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.2.0...HEAD
[1.2.0]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.1.0...releases/1.2.0
[1.1.0]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.0.1...releases/1.1.0
[1.0.1]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.0.0...releases/1.0.1
[1.0.0]: https://github.com/fsprojects/FSharp.Azure.Cosmos/releases/tag/releases/1.0.0
