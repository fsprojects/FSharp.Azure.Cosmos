---
applyTo: "tests/**/*.fs"
---

# Test scenarios

* If all tests of an emulator test class read the same data, do not write the data in each test. Use a scenario: a `DatabaseTestApplicationFactory` descendant in `tests/<project>/Scenarios/`.
* If a test examines a write (create, replace, upsert, patch, or delete), write the data of the test in the test.
* Refer to the `TestApplicationFactory` of a scenario through its namespace, for example `OperationTestBase<SingleItemScenario.TestApplicationFactory>`. Open the namespace for the other types of the scenario.
* To write or change a scenario, use the `test-data-seeding` skill.
