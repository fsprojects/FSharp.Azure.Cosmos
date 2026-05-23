namespace Tests.Integration

module IntegrationTestPlan =

    let plannedScenarios : string array = [|
        "Create item: validates id/partition key persistence and response diagnostics."
        "Read item: validates successful reads and not-found responses."
        "Upsert item: validates insert-then-update behavior and returned resource state."
        "Replace item: validates optimistic concurrency with ETag preconditions."
        "Patch item: validates additive and replace patch operations for partial updates."
        "Delete item: validates deletion and subsequent not-found behavior."
        "Read many: validates batch read behavior across partitions."
        "Iterator/TaskSeq reads: validates paging and continuation behavior."
        "Unique key constraints: validates duplicate write failures."
        "Seeded data scenarios: empty container, single partition set, multi-partition set."
    |]
