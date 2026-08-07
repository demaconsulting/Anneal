---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/CheckContractsOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Enforcement/**
  - src/DemaConsulting.Anneal.Toolkit/Architecture/**
  - .github/skills/check-contracts/**
  - .github/template/check-contracts.ps1
  - test-check-contracts.ps1
---

[← Toolkit](../toolkit.md)

# ContractCheck

ContractCheck enforces the clause-to-test link: it reads the architecture tree of a repository,
extracts every contract clause, and confirms that each names a test which exists and most recently
passed. That is the one promise-bearing rule a build fails on; the other build checks guard form, not
contracts.

If it were rewritten, consumers would notice through its **exit code and its failure taxonomy**, not its
implementation. CI depends on the exit code. The `check-contracts` skill documents each failure and how
to resolve it, and `scope-check` invokes it with `-Strict`. That taxonomy is the real interface, which is
why the clauses below are written in terms of *what is rejected* rather than how parsing works.

Its own verifiers are fixture repositories rather than unit tests.
`test-check-contracts.ps1` builds a fixture repository per failure mode and asserts the exit code — the
same sandbox-on-throw-away-folders technique that behavioral verification uses elsewhere, applied to
something deterministic enough to run in CI.

## Contract

### Provides

- **TOOLKIT-17** — `check-contracts` verifies a repository's architecture tree against the clause-to-test
  link, reporting whether every contract clause names a test that exists and most recently passed. It
  reaches this verdict deterministically and consults no model.
  *Verified by:* `ToolkitContractTests.CheckContractsVerifiesTheClauseToTestLink`

- **CONTRACT-CHECK-01** — Accepts a repository whose clauses all name existing, passing tests and reports
  success.
  *Verified by:* `test-check-contracts.ps1: "clean repository passes"`

- **CONTRACT-CHECK-02** — Rejects a level-2 system document that has no `## Contract` section; a deeper
  section node without its own contract is legal and is not rejected.
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

- **CONTRACT-CHECK-10** — Reports a clause as an unfulfilled obligation — a warning by default, an error
  under `-Strict` — when and only when its verifier string opens with the placeholder form: an uppercase
  `TODO.` or `TODO_`, matched case-sensitively at the start of the string as written rather than against
  the name it resolves to. Any other verifier mentioning the word is checked like any other.
  *Verified by:* `test-check-contracts.ps1: "a planned obligation is an error under -Strict"`

- **CONTRACT-CHECK-11** — Exempts `overview.md` from the contract requirement, and ignores clauses inside
  fenced examples and entries under `Requires`.
  *Verified by:* `test-check-contracts.ps1: "overview.md is exempt from the contract requirement"`

- **CONTRACT-CHECK-12** — Discovers tests and results through caller-supplied patterns covering all four
  things that vary between test frameworks: which files are searched, what a test declaration looks like,
  what marks a declaration as a boundary test rather than an interior one, and what form a recorded result
  takes. A repository whose verifiers are named fixture cases rather than attribute-marked methods, and
  whose results are not TRX, is checkable through those patterns alone. Their defaults describe a C# xUnit
  repository, so a caller that supplies none of them gets the C# behavior unchanged.
  *Verified by:* `test-check-contracts.ps1: "a fixture-case repository is checked through discovery patterns"`

- **CONTRACT-CHECK-13** — Rejects a run that discovered no test declarations at all while some clause names
  a verifier that is not a planned obligation, naming the discovery patterns that matched nothing rather
  than reporting each clause as a missing test.
  *Verified by:* `test-check-contracts.ps1: "discovery that matches nothing is its own failure"`

- **CONTRACT-CHECK-14** — Accepts several discovery configurations for one run, so a single invocation
  resolves clauses whose verifying tests are written in different languages, laid out differently, and
  recorded in different result formats. Configuring several at once and configuring a single one through
  the settings they replace are alternatives: supplying both is rejected rather than merged, so which
  layout a run checked is always readable at the call site.
  *Verified by:* `test-check-contracts.ps1: "two discovery profiles resolve clauses in both languages"`

- **CONTRACT-CHECK-15** — Judges emptiness, missing results, and staleness within each discovery
  configuration rather than across the run, and names the configuration at fault. A configuration that
  matches no tests, has no results, or has results older than the sources they describe is an error even
  when another configuration in the same run is complete and fresh.
  *Verified by:* `test-check-contracts.ps1: "a profile matching no test declarations is an error"`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every Toolkit operation is
  built from; `check-contracts` is deterministic and reaches for nothing in the model seam.
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
repairs, which the skill has to be able to tell apart. `CONTRACT-CHECK-13` is the same distinction applied
to the second pass itself: *discovery found nothing anywhere* is a fourth failure with a fourth repair, and
collapsing it into the third would send a reader off to write tests that already exist.

The fixture suite is the other half of the system. `test-check-contracts.ps1` constructs a complete
throw-away repository per case rather than asserting against strings, because the failures being tested
are properties of a repository, not of a function. One fixture per documented failure keeps the skill
honest: a failure mode the skill describes and the suite does not cover is a gap that shows up as a
missing fixture.

Anneal is its own second consumer, and it is no longer a single-framework repository: some of its
verifiers are cases in that same fixture suite, named by the quoted case string rather than by a method
identifier and recorded as a text tally that suite writes, while others are C# boundary tests recorded as
TRX. That is what turns `CONTRACT-CHECK-12` from a defaulting convenience
into an interface — a fixture-case repository is a first-class shape, not a variation on the C# one, so the
patterns have to reach the declaration form and the result form and not only the file extension — and it is
why `CONTRACT-CHECK-14` exists: the two shapes disagree on every one of those patterns at once, so no
single configuration describes the repository. The C# defaults are held still while that widens, because
every downstream repository reads them.

Adding a fixture case follows a fixed shape: build a throw-away repository with `New-Repo`,
`Set-SystemDoc`, `Set-ContractTests`, and `Set-Trx`, then assert with `Test-Case` on the exit code and
message substrings. Assert with `-Reject` as well as `-Expect` where a case is about something *not*
firing — both must agree for the case to mean anything. A new case earns its place by failing when the
behavior it protects is removed: comment out the implementing line in `check-contracts.ps1`, watch the
case fail, then restore it. A case that passes either way is documentation, not a test.

## Decisions

**Fail closed, always** — a clause the parser cannot read is an error. Skipping it was rejected outright:
a renamed heading would remove a clause from the check while the run still reported success, which is
worse than having no check at all, because it manufactures false confidence.

**An empty discovery is a finding, not a quiet baseline** — a run that looks for test declarations and
finds none has learned something, and until `CONTRACT-CHECK-13` it discarded it. Silence there reads as
"there are no tests here", which is indistinguishable from "the patterns point somewhere that has no
tests", and the two have opposite repairs. The escape hatch is the one that already exists: a clause whose
verifier is a planned obligation is not expected to resolve to anything, so a tree of planned
clauses with no tests yet stays green and a bootstrapping repository is unaffected. A dedicated flag for
tolerating an empty discovery was rejected — it would be set once while a repository was being scaffolded
and never removed, and a permanently silenced check is the failure mode this decision exists to prevent.

**Discovery is parameterized, and the defaults do not move** — the patterns are widened to reach a
fixture-case repository, but every default keeps naming the C# xUnit shape, so a green check in a
downstream repository keeps meaning exactly what it meant. Detecting the repository's shape automatically
was rejected: the detection would itself be a silent decision made on the check's behalf, and a wrong guess
would either look for the wrong tests or, worse, find something that is not a test.

**Emptiness is judged per configuration** — `CONTRACT-CHECK-15` applies the fail-closed property of
`CONTRACT-CHECK-13` to each discovery configuration separately, because a whole-run check would pass as
soon as any one configuration found something, and a repository that checks two frameworks would report
success with one of them entirely unexamined. That is the failure this facility would otherwise introduce:
silently checking less than the call site says it checks. Merging a set of configurations with the
single-configuration settings they replace was rejected for the same reason — whichever won would be
invisible to the reader.

**Warn on planned clauses, error under `-Strict`** — `architecture-design` must be able to write a
contract before its tests exist, so placeholder verifiers are tolerated during design and promoted to
errors once implementation is complete. Rejecting them outright would force either fabricated tests or an
undocumented contract.

**The marker is a placeholder form, not a word** — `CONTRACT-CHECK-10` matches the verifier string as the
author wrote it, not the name it resolves to, because those disagree in both directions: the standard
`TODO.SomeTest` shape resolves to a name carrying no marker, while a real fixture case that discusses
obligations resolves to one that does. Matching the resolved name, or matching the word anywhere, exempts
genuine tests from the only enforced check in the process — which is a clause passing on a promise nobody
verified. The narrow form costs an author one character of discipline and cannot fail open.

**Verified by fixture repositories, not unit tests** — the suite pays the cost of building real
directory trees because the behavior under test is repository-shaped. Unit-testing the parser was
rejected as testing the easy half.
