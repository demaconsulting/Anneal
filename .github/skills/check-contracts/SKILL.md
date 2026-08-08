---
name: check-contracts
description: Run and interpret the system contract check. Use when implementing or verifying a
  Contract Change or Structural Change, when a contract clause is added or altered, or when
  lint.ps1 reports a contract failure. Covers which invocation to use for each scope and how to
  resolve each failure.
---

# Check Contracts

`dotnet anneal check-contracts` verifies that every contract clause in `docs/architecture/` names a
real boundary test that exists and passed. It is the **only** mechanically enforced relationship in
this process; everything else is judgement. Treat its output as authoritative and do not re-verify
by hand what it proved.

It **fails closed**: a clause it cannot parse, or a system document with no `## Contract` section,
is an error rather than a silent skip. A check that quietly stops looking is worse than no check.

The operation is invoked as `dotnet anneal check-contracts` and is run by `lint.ps1`, so CI gates
on it whether or not an agent is involved.

# Discovery Is Configurable

The defaults describe a C# xUnit repository: `*.cs` files under `test/` and `tests/`,
attribute-marked methods, a `Contract/` folder, and TRX results. Four dimensions cover everything
that varies between frameworks:

| CLI flag | What it controls |
| --- | --- |
| `-TestFilePatterns` | Which files are searched for test declarations |
| `-TestDeclarationPattern` | What a declaration looks like (regex with named capture `name`) |
| `-ContractTestFolder` | What marks a declaration as a boundary test; empty means no interior/boundary split |
| `-TestResultFormat` | Result format: `trx` or `text` |

Supply them through `-TestProfiles` to configure several discovery shapes in one run (one
`-TestProfiles` argument per shape, fields semicolon-separated, list values comma-separated), or as
flat arguments when configuring a single shape. Supplying both for the same run is rejected.
Configure them at the call site. Editing defaults to teach the operation a new shape is the wrong
repair — it would change behavior for every caller.

# Which Invocation to Use

| Situation | Command |
| --- | --- |
| Small Fix change | Not required — no clause changed |
| Contract Change or Structural Change, implementing | `dotnet anneal check-contracts` |
| Contract Change or Structural Change, verifying a completed change | `dotnet anneal check-contracts -Strict` |
| Bootstrapping a tree, tests not yet written | `dotnet anneal check-contracts` |
| Landing a migration stage with later stages outstanding | `dotnet anneal check-contracts` |

`-Strict` promotes unfulfilled planned obligations, and absent test results, from warnings to errors.
Use it once implementation is complete — before that, a planned obligation is a deliberate placeholder
written by `architecture-design`, not a defect.

**Run `pwsh ./build.ps1` first, always.** The pass check reads recorded test results; without them
it verifies nothing and says so. `build.ps1` clears `artifacts/tests` before each run, so results
cannot accumulate — an outcome is always the most recent one for that test.

If `dotnet anneal` is not available, the repository's `.config/dotnet-tools.json` has not been
restored. Run `dotnet tool restore` or `build.ps1`, which does it. Do not hand-verify the
clause-to-test links as a substitute, and do not write your own checker.

# Resolving Failures

**Never resolve any failure by editing the clause to match the code.** The clause is the promise;
the code is the implementation. Editing the promise to match what got built silently narrows the
contract and defeats the entire check.

- **`has no '## Contract' section`** — the system document is missing its contract, or the heading
  was renamed. The heading must be exactly `## Contract`. Restore it; do not rename the check.
- **`is not a well-formed clause ID`** — a bolded item under `Provides` or `Invariants` does not
  parse as `{SYSTEM}-nn` or `{SYSTEM}-In`. Usually an unresolved template placeholder or a stray
  bolded bullet. Give it a real ID, or move it out of those subsections.
- **`names no verifying test`** — the clause has no `*Verified by:*` line. Write the contract test,
  then name it in the clause.
- **`No test declarations found in ...`** — discovery matched no test file, or matched files in
  which nothing looked like a declaration, while clauses named real tests. The tests are almost
  certainly there: the patterns point somewhere else. Fix `-TestRoots`, `-TestFilePatterns` or
  `-TestDeclarationPattern` at the call site. **Do not write the tests again**, and do not resolve it
  by marking the clauses as planned obligations — that silences the check permanently. A tree whose
  clauses are all planned obligations is exempt, so bootstrapping never trips this.
- **`is not declared as a test method`** — the name does not resolve to a declared test. It may have
  been renamed or deleted, or the clause may be pointing at a helper. Restore or write the test under
  the name the clause gives. A deliberate rename is a contract change; route it to
  `architecture-update`. If *every* clause reports this, look for the message above instead.
- **`is not in a 'Contract' folder`** — the test exists but is an interior test. Interior tests are
  disposable, so they cannot carry a durable promise. Move it to `test/{System}.Tests/Contract/` and
  rewrite it to use only the public boundary.
- **`whose most recent result is '...'`** — the test ran and did not pass. Fix the code so the
  promise holds; a failing contract test means the system does not do what it claims.
- **`has no result - it did not run`** — the test is declared but was not executed. Usually a
  filtered or skipped run, or a test project missing from the solution.
- **`Duplicate clause ID`** — the same ID appears in two system documents. Assign the next unused
  number and never reuse a retired one.
- **`unfulfilled test obligation`** — the clause's verifier opens with the planned-obligation
  placeholder. Write the test and replace the placeholder. Expected during bootstrap; a real gap under
  `-Strict`. `system-contracts.md` defines the form and owns the rule.
- **`Test results are stale`** — sources changed after the last test run. Re-run `pwsh ./build.ps1`.
  Never delete results to silence this.
- **`No test results matching ...`** — `build.ps1` has not run in this working tree. Run it; the
  pass check cannot verify anything without results, and under `-Strict` this is an error.

Nothing outside that placeholder form is exempted, so a genuine test named `TodoItemsAreReturned`, and
a case named `suite.ps1: "TODO obligation is an error"`, are checked normally rather than quietly
skipped. Do not reach for a placeholder to silence any other failure above.

A name that survives only in a comment does not count as a living test — comments are stripped
before matching, and by default a clause is satisfied only by a real test-method declaration, so
neither a private helper nor a string literal can keep a deleted promise alive.

# Scope

This check proves the clause-to-test **link**, and that the test is a real boundary test with a
recent passing result. It cannot tell you whether a clause is pitched at the right altitude,
describes WHAT rather than HOW, or is observable from outside the system. Those are judgement calls
— see `system-contracts.md`.

A clean exit is necessary, not sufficient.
