# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
[Unreleased]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.0.1...HEAD
[1.0.1]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.0.0...releases/1.0.1
[1.0.0]: https://github.com/fsprojects/FSharp.Azure.Cosmos/releases/tag/releases/1.0.0
