---
level: system
covers:
  - .github/template/check-contracts.ps1
  - .github/skills/check-contracts/**
  - test-check-contracts.ps1
---

[← Architecture Overview](./overview.md)

# ContractCheck

ContractCheck is the only part of Anneal that decides anything without a language model. It reads the
architecture tree of a repository, extracts every contract clause, and confirms that each names a test
which exists and most recently passed. Everything else in this process is judgement recorded in a report;
this is the one rule a build can fail on.

If it were rewritten, consumers would notice through its **exit code and its failure taxonomy**, not its
implementation. CI depends on the exit code. The `check-contracts` skill documents each failure and how
to resolve it, and `tier-check` invokes it with `-Strict`. That taxonomy is the real interface, which is
why the clauses below are written in terms of *what is rejected* rather than how parsing works.

Its contract is the only one in this repository whose verifiers exist today.
`test-check-contracts.ps1` builds a fixture repository per failure mode and asserts the exit code — the
same sandbox-on-throw-away-folders technique that behavioral verification uses elsewhere, applied to
something deterministic enough to run in CI.

## Contract

### Provides

- **CONTRACT-CHECK-01** — Accepts a repository whose clauses all name existing, passing tests and reports
  success.
  *Verified by:* `test-check-contracts.ps1: "clean repository passes"`

- **CONTRACT-CHECK-02** — Rejects a system document that has no `## Contract` section.
  *Verified by:* `test-check-contracts.ps1: "system document with no Contract section"`

- **CONTRACT-CHECK-03** — Rejects a clause identifier that is malformed or left as an unresolved
  placeholder.
  *Verified by:* `test-check-contracts.ps1: "unresolved placeholder is not a well-formed ID"`

- **CONTRACT-CHECK-04** — Rejects the same clause identifier appearing in two documents.
  *Verified by:* `test-check-contracts.ps1: "duplicate clause ID across two documents"`

- **CONTRACT-CHECK-05** — Rejects a clause that names no verifying test.
  *Verified by:* `test-check-contracts.ps1: "clause naming no verifying test"`

- **CONTRACT-CHECK-06** — Rejects a clause naming a test that no longer exists, including one surviving
  only inside a comment.
  *Verified by:* `test-check-contracts.ps1: "test surviving only in a comment does not satisfy a clause"`

- **CONTRACT-CHECK-07** — Rejects a clause verified by an interior test rather than a boundary test.
  *Verified by:* `test-check-contracts.ps1: "clause pointing at an interior test"`

- **CONTRACT-CHECK-08** — Rejects a clause whose named test most recently failed, did not run, or has no
  result at all under `-Strict`.
  *Verified by:* `test-check-contracts.ps1: "clause whose test most recently failed"`

- **CONTRACT-CHECK-09** — Rejects results older than the test sources they describe, so a stale passing
  run cannot vouch for current code.
  *Verified by:* `test-check-contracts.ps1: "stale results are rejected"`

- **CONTRACT-CHECK-10** — Reports a clause whose verifier name contains `TODO` as an unfulfilled
  obligation — a warning by default, an error under `-Strict`.
  *Verified by:* `test-check-contracts.ps1: "TODO obligation is an error under -Strict"`

- **CONTRACT-CHECK-11** — Exempts `overview.md` from the contract requirement, and ignores clauses inside
  fenced examples and entries under `Requires`.
  *Verified by:* `test-check-contracts.ps1: "overview.md is exempt from the contract requirement"`

- **CONTRACT-CHECK-12** — Discovers tests and results through caller-supplied patterns, so a repository
  that is not C# and xUnit can be checked without modifying the script.
  *Verified by:* `TODO.DiscoveryPatternsAreConfigurable`

### Requires

- **PowerShell 7** — file globbing, XML parsing, and exit-code propagation.

### Invariants

- **CONTRACT-CHECK-I1** — Parsing fails closed: input the script cannot understand is an error, never a
  silent skip.
  *Verified by:* `test-check-contracts.ps1: "unresolved placeholder is not a well-formed ID"`

- **CONTRACT-CHECK-I2** — A single failing case within a data-driven test fails the clause it verifies.
  *Verified by:* `test-check-contracts.ps1: "one failing data-driven case fails the clause"`

## Composition

The script runs in three passes that are kept separate on purpose: collect clauses from the tree, collect
declared tests from the test sources, then reconcile both against recorded results. Merging them would be
shorter and would lose the distinction between *a clause naming nothing*, *a clause naming something that
does not exist*, and *a clause naming something that failed* — three failures with three different
repairs, which the skill has to be able to tell apart.

The fixture suite is the other half of the system. `test-check-contracts.ps1` constructs a complete
throw-away repository per case rather than asserting against strings, because the failures being tested
are properties of a repository, not of a function. One fixture per documented failure keeps the skill
honest: a failure mode the skill describes and the suite does not cover is a gap that shows up as a
missing fixture.

`CONTRACT-CHECK-12` is the newest pressure on this system and the only clause here without a verifier.
Anneal itself is the second consumer, and it is not a C# repository — so discovery patterns that were
sensible defaults are now an interface.

## Decisions

**Fail closed, always** — a clause the parser cannot read is an error. Skipping it was rejected outright:
a renamed heading would remove a clause from the check while the run still reported success, which is
worse than having no check at all, because it manufactures false confidence.

**Warn on planned clauses, error under `-Strict`** — `architecture-design` must be able to write a
contract before its tests exist, so `TODO` verifiers are tolerated during design and promoted to errors
once implementation is complete. Rejecting them outright would force either fabricated tests or an
undocumented contract.

**Verified by fixture repositories, not unit tests** — the suite pays the cost of building real
directory trees because the behavior under test is repository-shaped. Unit-testing the parser was
rejected as testing the easy half.
