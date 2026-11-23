---
name: writing-tests
description: Write and review automated tests. Picks the right level for each test (unit, integration, contract, system), keeps each test small and about one behaviour, decides what to replace with a double, and links every test to the acceptance criterion it covers. Use this whenever the user asks to add, write, change or review tests, to decide whether something is a unit or an integration test, or to check that the acceptance criteria are covered, even if they only say "add tests for X".
---

# Writing tests

A test is worth having when it would fail for a real defect. This skill is about writing tests that earn their place: where each one goes, what makes it good, what to replace with a double, and how it links to the spec. It does not say in which order to work (test first or after); that is a separate matter.

Before writing, look at how the project already names, organizes and runs its tests, and follow it. Levels and tools differ between projects; the rules below do not.

## 1. Start from the criteria

If the spec has acceptance criteria with IDs (like `UC-03.AC2`), they are the list of tests to write. Every criterion gets at least one test, and each test says which criteria it covers, with the ID, in its name or in whatever marker the framework offers (tag, trait, attribute, decorator, comment). One test may cover several criteria and one criterion may need several tests. What matters is that none is left uncovered, and that someone can check it by searching for the ID.

Map the criterion onto the test directly: Given is the setup, When is the one action, Then is the assertions. If the test needs a behaviour no criterion states, say so: the spec may be missing a criterion, and that is for the user to decide.

If there are no criteria, ask the user which behaviours must be protected before writing.

## 2. Pick the level

Each rule is tested at the lowest level that can show it. A test goes up a level only when the lower one cannot prove the thing, never to repeat what a lower level already proves. The number of tests per level is a result of this, not a goal. Ask in this order and stop at the first yes:

1. Does it need the whole system running, a restart or the real startup? **System.**
2. Is it about what an external client sees (names, schemas, formats, the shape of errors)? **Contract.**
3. Does it need real infrastructure (database constraints, real files, links, races, a real rollback, a real PDF or other format)? **Integration.**
4. Otherwise it is a rule of the code: a validation, a normalization, a comparison, a message, a mapping of errors, what a service calls and in which order. **Unit.**

Use the project's own names for the levels. Signs that the pyramid is turning upside down: an integration test that builds real infrastructure but only asserts a message or an error code; several integration tests that differ only in the input value (make them one parameterized unit test); a contract test that repeats a business scenario instead of checking the contract; a service that cannot be unit tested because it uses the disk or the clock directly (extract the dependency). A test that only repeats another one at a higher level is deleted.

## 3. What makes a test worth keeping

- **Small.** One behaviour, with a name that says which. An "and" in the name means two tests. Several assertions are fine when they describe the same behaviour.
- **It names the break it catches.** Before writing, say which change in the code would make it fail, and whether that change is a bug or a decision. If you cannot name one, the test is not worth writing.
- **It checks behaviour through the public interface**, not private structure. A test that breaks when the code is refactored without changing behaviour is coupled to the wrong thing.
- **Expected values are independent.** Use literals or hand-checked values, never a value computed by the code under test or by its helpers: such a test passes whatever the code does.
- **No change detectors.** Asserting a constant, a config value or the exact wording of a message fails on every intentional change and sleeps through bugs. Assert the behaviour that depends on it. The exception is text the spec defines as part of the contract.
- **It tests your code, not the framework or a library.** Test the contract your code makes at its boundaries.
- **Independent and deterministic.** No order between tests, no sleeps, no real clock or randomness (inject them). A flaky test is fixed or deleted.
- **It can fail.** Check each new test against a broken version of the behaviour and see it fail for the expected reason. Then think of the realistic mistakes (a wrong constant, the wrong branch, a missing side effect, an empty result, a missing validation): at least one test should fail for each. A mistake nothing catches means the behaviour is unprotected or a test says nothing.

## 4. Doubles

A double replaces a boundary (database, file system, clock, network, an external service), never a rule. Your own logic runs for real in the test.

- A simplified double, such as an in-memory fake, is fine in unit tests. Then what the real implementation does (constraints, parsing, path rules) needs its own test against the real thing.
- To test failures, make the double throw. That shows the caller gets a controlled error and that nothing is left half done. That the real store really rolls back is an integration test.
- Do not assert that a double exists or was configured. Assert behaviour. Assert calls only when the arguments or the order are part of the contract.
- A double returns the complete real structure, not only the fields the test reads, and each branch (success, error, malformed) has its own.
- If setting up doubles takes longer than the test itself, the design is too coupled or the test belongs one level up.
- Production code carries no method that only tests call.

## 5. Before handing it over

- Every criterion has a test marked with its ID. List the ones that do not, if any.
- Each test sits at the lowest level that can show it.
- Each test names the break it catches and fails when that break is made.
- Run the project's whole test command, not only the new file, and report every failure by name, including ones you did not cause.

## Example

```
UC-02.AC1  a book already on loan is rejected        [unit]
  Given  a book that another member has borrowed
  When   a member borrows it
  Then   the loan is rejected with book_unavailable and no loan exists

UC-01.AC3  only one loan per book under concurrent requests   [integration]
  Given  an available book
  When   two members borrow it at the same time
  Then   one loan exists and the other request is rejected
```

The second test needs real infrastructure (the database decides the race), so it is an integration test. The first one is a rule of the code and stays at unit level.
