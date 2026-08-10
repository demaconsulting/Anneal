---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/CheckContractsOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Enforcement/**
  - src/DemaConsulting.Anneal.Toolkit/Architecture/**
  - .github/skills/check-contracts/**
  - test/DemaConsulting.Anneal.Toolkit.Tests/Contract/CheckContractsSubprocessTests.cs
---

[← Toolkit](../toolkit.md)

# ContractCheck

ContractCheck enforces the clause-to-test link: it reads the architecture tree of a repository,
extracts every contract clause, and confirms that each names a test which exists and most recently
passed. That is the one promise-bearing rule a build fails on; the other build checks guard form, not
contracts.

If it were rewritten, consumers would notice through its **exit code and its failure taxonomy**, not its
implementation. CI depends on the exit code. The `check-contracts` skill documents each failure and how
to resolve it, and `verify-change` invokes it with `-Strict`. That taxonomy is the real interface, which is
why the clauses below are written in terms of *what is rejected* rather than how parsing works.

Its own verifiers are fixture repositories rather than unit tests.
`CheckContractsSubprocessTests` builds a fixture repository per failure mode and spawns the real,
installed `dotnet anneal check-contracts` against it, asserting on exit code and output — the same
sandbox-on-throw-away-folders technique that behavioral verification uses elsewhere, applied to
something deterministic enough to run in CI, and the same black-box subprocess boundary the PowerShell
suite it replaced used, now compiled and run by `dotnet test` instead of shelled out separately.

## Contract

### Provides

- **TOOLKIT-17** — `check-contracts` verifies a repository's architecture tree against the clause-to-test
  link, reporting whether every contract clause names a test that exists and most recently passed. It
  reaches this verdict deterministically and consults no model.
  *Verified by:* `ToolkitContractTests.CheckContractsVerifiesTheClauseToTestLink`

- **CONTRACT-CHECK-01** — Accepts a repository whose clauses all name existing, passing tests and reports
  success.
  *Verified by:* `CheckContractsSubprocessTests.CleanRepositoryPasses`

- **CONTRACT-CHECK-02** — Rejects a level-2 system document that has no `## Contract` section; a deeper
  section node without its own contract is legal and is not rejected.
  *Verified by:* `CheckContractsSubprocessTests.SystemDocumentWithNoContractSectionIsRejected`

- **CONTRACT-CHECK-03** — Rejects a clause identifier that is malformed or left as an unresolved
  placeholder.
  *Verified by:* `CheckContractsSubprocessTests.UnresolvedPlaceholderIsNotAWellFormedId`

- **CONTRACT-CHECK-04** — Rejects the same clause identifier appearing in two documents.
  *Verified by:* `CheckContractsSubprocessTests.DuplicateClauseIdAcrossTwoDocumentsIsRejected`

- **CONTRACT-CHECK-05** — Rejects a clause that names no verifying test.
  *Verified by:* `CheckContractsSubprocessTests.ClauseNamingNoVerifyingTestIsRejected`

- **CONTRACT-CHECK-06** — Rejects a clause naming a test that no longer exists, including one surviving
  only inside a comment.
  *Verified by:* `CheckContractsSubprocessTests.TestSurvivingOnlyInACommentDoesNotSatisfyAClause`

- **CONTRACT-CHECK-07** — Rejects a clause verified by an interior test rather than a boundary test.
  *Verified by:* `CheckContractsSubprocessTests.ClausePointingAtAnInteriorTestIsRejected`

- **CONTRACT-CHECK-08** — Rejects a clause whose named test most recently failed, did not run, or has no
  result at all under `-Strict`.
  *Verified by:* `CheckContractsSubprocessTests.ClauseWhoseTestMostRecentlyFailedIsRejected`

- **CONTRACT-CHECK-09** — Rejects results older than the test sources they describe, so a stale passing
  run cannot vouch for current code.
  *Verified by:* `CheckContractsSubprocessTests.StaleResultsAreRejected`

- **CONTRACT-CHECK-10** — Reports a clause as an unfulfilled obligation — a warning by default, an error
  under `-Strict` — when and only when its verifier string opens with the placeholder form: an uppercase
  `TODO.` or `TODO_`, matched case-sensitively at the start of the string as written rather than against
  the name it resolves to. Any other verifier mentioning the word is checked like any other.
  *Verified by:* `CheckContractsSubprocessTests.PlannedObligationIsAnErrorUnderStrict`

- **CONTRACT-CHECK-11** — Exempts `overview.md` from the contract requirement, and ignores clauses inside
  fenced examples and entries under `Requires`.
  *Verified by:* `CheckContractsSubprocessTests.OverviewIsExemptFromTheContractRequirement`

- **CONTRACT-CHECK-12** — Discovers tests and results through caller-supplied patterns covering all four
  things that vary between test frameworks: which files are searched, what a test declaration looks like,
  what marks a declaration as a boundary test rather than an interior one, and what form a recorded result
  takes. A repository whose verifiers are named fixture cases rather than attribute-marked methods, and
  whose results are not TRX, is checkable through those patterns alone. Their defaults describe a C# xUnit
  repository, so a caller that supplies none of them gets the C# behavior unchanged.
  *Verified by:* `CheckContractsSubprocessTests.FixtureCaseRepositoryIsCheckedThroughDiscoveryPatterns`

- **CONTRACT-CHECK-13** — Rejects a run that discovered no test declarations at all while some clause names
  a verifier that is not a planned obligation, naming the discovery patterns that matched nothing rather
  than reporting each clause as a missing test.
  *Verified by:* `CheckContractsSubprocessTests.DiscoveryThatMatchesNothingIsItsOwnFailure`

- **CONTRACT-CHECK-14** — Accepts several discovery configurations for one run, so a single invocation
  resolves clauses whose verifying tests are written in different languages, laid out differently, and
  recorded in different result formats. Configuring several at once and configuring a single one through
  the settings they replace are alternatives: supplying both is rejected rather than merged, so which
  layout a run checked is always readable at the call site.
  *Verified by:* `CheckContractsSubprocessTests.TwoDiscoveryProfilesResolveClausesInBothLanguages`

- **CONTRACT-CHECK-15** — Judges emptiness, missing results, and staleness within each discovery
  configuration rather than across the run, and names the configuration at fault. A configuration that
  matches no tests, has no results, or has results older than the sources they describe is an error even
  when another configuration in the same run is complete and fresh.
  *Verified by:* `CheckContractsSubprocessTests.ProfileMatchingNoTestDeclarationsIsAnError`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every Toolkit operation is
  built from; `check-contracts` is deterministic and reaches for nothing in the model seam.

### Invariants

- **CONTRACT-CHECK-I1** — Parsing fails closed: input the operation cannot understand is an error, never a
  silent skip.
  *Verified by:* `CheckContractsSubprocessTests.UnresolvedPlaceholderIsNotAWellFormedId`

- **CONTRACT-CHECK-I2** — A single failing case within a data-driven test fails the clause it verifies.
  *Verified by:* `CheckContractsSubprocessTests.OneFailingDataDrivenCaseFailsTheClause`

## Composition

The operation runs in three passes that are kept separate on purpose: collect clauses from the tree,
collect declared tests from the test sources, then reconcile both against recorded results. Merging them
would be shorter and would lose the distinction between *a clause naming nothing*, *a clause naming
something that does not exist*, and *a clause naming something that failed* — three failures with three
different repairs, which the skill has to be able to tell apart. `CONTRACT-CHECK-13` is the same
distinction applied to the second pass itself: *discovery found nothing anywhere* is a fourth failure with
a fourth repair, and collapsing it into the third would send a reader off to write tests that already
exist.

The fixture suite is the other half of the system. `CheckContractsSubprocessTests` constructs a complete
throw-away repository per case, outside the repository tree, and spawns the real, installed `dotnet
anneal check-contracts` against it rather than calling the operation's classes in-process — the failures
being tested are properties of a repository observed across the real CLI boundary, not of a function.
One fixture per documented failure keeps the skill honest: a failure mode the skill describes and the
suite does not cover is a gap that shows up as a missing fixture. That subprocess boundary is also what
this suite does that `test/DemaConsulting.Anneal.Toolkit.Tests/ContractChecking/` does not: the in-process
tests there exercise the same parsing and reconciliation logic directly, but only `CheckContractsSubprocessTests`
proves the compiled, packaged, `dotnet anneal`-invoked tool actually behaves the same way.

Anneal is its own second consumer, and it is no longer a single-framework repository: some of its
verifiers are fixture cases in `test-process-contract.ps1`, named by the quoted case string rather than
by a method identifier and recorded as a text tally that suite writes, while others — including
`CheckContractsSubprocessTests` itself — are C# boundary tests recorded as TRX. That is what turns
`CONTRACT-CHECK-12` from a defaulting convenience into an interface — a fixture-case repository is a
first-class shape, not a variation on the C# one, so the patterns have to reach the declaration form and
the result form and not only the file extension — and it is why `CONTRACT-CHECK-14` exists: the two
shapes disagree on every one of those patterns at once, so no single configuration describes the
repository. The C# defaults are held still while that widens, because every downstream repository reads
them.

Adding a fixture case follows a fixed shape: build a throw-away repository under a temporary directory
outside the source tree, write its architecture tree and contract tests, drop the results file the case
needs, then invoke `dotnet anneal check-contracts` as a real subprocess and assert on its exit code and
output substrings. Assert the absence of a message as well as its presence where a case is about
something *not* firing — both must agree for the case to mean anything. A new case earns its place by
failing when the behavior it protects is removed: delete the implementing clause from the operation,
watch the case fail, then restore it. A case that passes either way is documentation, not a test.

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

**Warn on planned clauses, error under `-Strict`** — `helper` must be able to write a
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

**The fixture suite is compiled C#, not a PowerShell script, and it still spawns the real tool** —
`test-check-contracts.ps1` was retired in favor of `CheckContractsSubprocessTests`. Nothing about the
boundary being verified moved: both drove the packaged `dotnet anneal check-contracts` as a real
subprocess against throw-away fixture repositories and asserted on exit code and output. What changed
is which infrastructure gets to enforce that promise. The in-process xUnit coverage under
`ContractChecking/` already existed and stayed exactly as it was — it exercises the operation's classes
directly and was never a substitute for the subprocess boundary, so replacing the PowerShell driver with
one written in C# does not narrow what is verified; it only lets the fixture suite itself be held to the
same compiled code-quality enforcement (nullable checks, analyzers, the C# test conventions) as the rest
of the Toolkit, instead of living outside that enforcement as untyped script text. Rewriting the same
subprocess assertions in PowerShell versus C# was rejected as the wrong axis to prefer either language
on; the axis that matters is that the fixture suite is now built and checked the same way its subject is.
