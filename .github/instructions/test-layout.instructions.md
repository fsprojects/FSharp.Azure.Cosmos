---
applyTo: "tests/**/*.fs"
---

# Test layout

## Rules

* Put each test file into the folder of its topic group. A topic group is a set of related test categories. A group can have more than one category.
* If a test file has more than one category, and all categories are in one group, put the file into the folder of this group.
* If a test file has categories of different groups, keep the file at the root of the test project.
* If a group has only one test file, do not make a folder. Keep the file at the root.
* If a file is not a test file, or if files of other folders use it, keep it at the root. Examples are `TestCategories.fs`, `TestAssembly.fs`, and `OperationTestInfrastructure.fs`.
* Keep the scenarios in `Scenarios/` (refer to `test-scenarios.instructions.md`).
* Give the folder the name of its group, as one PascalCase word.
* Keep the files of one folder together in the `<Compile>` list of the `.fsproj` file. Thus the project tree shows each folder one time.
* Do not change the namespace of a file when you move it to a folder.
* When you add a test category, add it to a group in the table. If no group fits, add a new group.

## Groups of `tests/Cosmos.Tests`

| Folder | Test categories |
|---|---|
| `TestInfrastructure/` | `TestInfrastructure` |
| `Operations/` | `Builders`, `Create`, `Delete`, `Patch`, `Read`, `ReadMany`, `Replace`, `Upsert` |
| `Extensions/` | `IterationExtensions`, `ReadExtensions` |
| `Json/` | `JsonConverter`, `TimeSpan` |
| root | `IcedTasksProbe`, `QuerySemantics`, `ResponseMessage`, `Validation`, `ValueCollections` (one file for each category) |

Each name in the second column is the name of a category attribute without the `TestCategoryAttribute` suffix.
