# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
[Unreleased]: https://github.com/fsprojects/FSharp.Azure.Cosmos/compare/releases/1.0.0...HEAD
[1.0.0]: https://github.com/fsprojects/FSharp.Azure.Cosmos/releases/tag/releases/1.0.0
