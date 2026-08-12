---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/MaintainOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/MaintainReport.cs
---

[← Toolkit](../toolkit.md)

# Maintain

`maintain` is the compiled front door for Maintenance mode — `change-classification.md`'s "available
capacity, no requested outcome" work: renaming for clarity, extracting helpers, deleting dead code,
tidying interior tests, and bumping a dependency. It is that compiled path, and it is deliberately narrower than `route`: helper or any other
caller that has already fixed the work to Maintenance mode invokes this action directly instead of
routing through a second classifier.

Maintenance is Small Fix by definition — `change-classification.md` says so in the same sentence that
defines the mode — so a caller invoking `maintain` has already fixed the work's Scope before this
action is ever reached. `maintain` therefore runs the declared work directly against `SmallFixWorker`,
this repository's own proven, budget-disciplined Small Fix implementer, rather than constructing a
`Router` and asking a routing oracle to classify Effort or select a worker: the oracle pass `route`
runs for exactly that purpose would be reclassifying a Scope the caller already fixed, the same
redundant-ceremony cost `route.md`'s own "one oracle pass, not two" decision already declines to pay
for Effort. No worker type is new; `maintain` composes machinery that already exists.

What `maintain` *does* add, because nothing else in the Toolkit already enforces it: `change-
classification.md` requires Maintenance to be "bounded before it starts" — a declared file set, before
any file is touched — and to "never edit the architecture tree, `.anneal/work/constraints.md`, or `.anneal/work/backlog.md`",
naming an architectural finding a *report*, never a license to act on it. Today both of those rules
are prose a model is trusted to follow. `maintain` makes each one a mechanical, post-run check against
what the worker actually changed, so neither rule depends on a model's good behavior to hold.

## Contract

### Provides

- **TOOLKIT-29** — `maintain` takes a Maintenance work item and a declared file-scope bound — one or
  more file or path patterns, mirroring `route`'s own changed-file-hint argument shape — and runs it
  directly against `SmallFixWorker`, asking no routing oracle to classify Effort or select a worker,
  since Maintenance is Small Fix by definition before `maintain` is ever invoked. A caller naming no
  file-scope entries is a usage error under `TOOLKIT-10`: unbounded Maintenance work has no bound to
  declare, and `change-classification.md` requires the bound to exist before the work starts, not
  after. It succeeds when the worker completes the work within its declared bound; escalates when the
  worker names a reroute, when a protected-path write is refused, or when either post-run check below
  (`TOOLKIT-30`, `TOOLKIT-31`) trips; and fails when the worker's repair budget is exhausted or no
  model could be reached.
  *Verified by:* `MaintainRunsDeclaredBoundDirectlyThroughSmallFixWorker`

- **TOOLKIT-30** — After the worker's run, `maintain` checks the actual files the worker reports having
  changed against the declared file-scope bound by strict-subset containment — the same mechanical
  check `Router.RunAsync`'s own phase decomposition already runs for `TOOLKIT-26`, applied here to a
  worker's real output instead of a phase's declared intent. Every changed file must be contained by an
  entry the bound declared; a changed file the bound did not cover forces the same escalation outcome
  `TOOLKIT-29` defines, naming the offending file, rather than being reported as an unqualified success.
  *Verified by:* `MaintainEscalatesWhenActualChangesExceedTheDeclaredBound`

- **TOOLKIT-31** — After the worker's run, `maintain` runs `ProtectedPathTripwire` — unchanged from its
  existing form, reused exactly as `TOOLKIT-27` already uses it — against the worker's actual
  changed-file list rather than only the declared bound, and forces the same escalation outcome
  whenever it trips, naming the tripped path, regardless of what the containment check (`TOOLKIT-30`)
  concludes for the same run. This is the mechanical enforcement of `change-classification.md`'s
  "Maintenance may never edit the architecture tree, `.anneal/work/constraints.md`, or `.anneal/work/backlog.md`" rule: a finding
  the tripwire reports is escalated to a person, never silently discarded and never silently allowed to
  stand as a completed Maintenance run.
  *Verified by:* `MaintainEscalatesWhenActualChangesTripTheProtectedPathCheck`

- **TOOLKIT-49** — When `maintain` escalates or fails and the worker had already written files to disk
  before stopping, `maintain` writes a patch file under `.anneal/logs/` named
  `interrupted-<timestamp>.patch` — via the shared `InterruptedDiffSnapshot` helper, the same one
  `TOOLKIT-23` uses — containing `git diff HEAD -- <those files>`, and prints
  `maintain: pre-triage snapshot written to <path>` alongside the escalation or failure output. A
  completed run never produces a patch file. The snapshot step is silent and non-throwing when there
  is no git repository, git is unavailable, or the diff is empty: the reported outcome is unaffected.
  *Verified by:* `InterruptedMaintainContractTests.MaintainWritesSnapshotPatchOnInterruptedOutcome`,
  `InterruptedMaintainContractTests.SucceededMaintainRunProducesNoSnapshotPatch`,
  `InterruptedMaintainContractTests.MaintainSnapshotFailureDoesNotMaskReportedOutcome`

- **TOOLKIT-55** — Whenever `TOOLKIT-49` writes a patch file, `maintain` also writes a companion JSON
  file at the same path stem with a `.json` extension (e.g. `interrupted-<timestamp>.json` alongside
  `interrupted-<timestamp>.patch`), recording the triage narrative — outcome, recommended next step,
  the work item that was running, files changed before stopping, a summary of the partial work, and
  any escalation or failure reason — so a later human or agent that only sees a dirty working tree
  (without having watched the live console output) can discover why the run stopped and what remains
  unverified. The JSON companion step is silent and non-throwing: companion failure never masks the
  reported outcome or the patch file written by `TOOLKIT-49`.
  *Verified by:* `InterruptedMaintainContractTests.MaintainWritesTriageContextJsonAlongsidePatch`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from, and the escalation outcome this operation reports through.
- **[Model Seam](./model-seam.md)** — every model call `SmallFixWorker`'s own `Developer` and
  `DeterministicCheck` steps make.
- **[Process](../process.md)** — `SmallFixWorker` and `ProtectedPathTripwire`, both reused unchanged
  from the machinery `route` and its Massive decomposition already built.

## Decisions

**No new worker type, and no routing-oracle pass** — `SmallFixWorker` is already proven and already
budget-disciplined, and Maintenance's Scope is fixed to Small Fix by `change-classification.md`'s own
definition before a caller ever invokes `maintain`. Asking a routing oracle to classify Effort or
select among three workers, the way `route` does for a work item whose Scope is still open, would
reclassify a decision the caller already made — the same "one oracle pass, not two" reasoning
`route.md` § Decisions already applies to Effort, extended here to the coarser question of which
worker runs at all. `maintain` is a thinner front door than `route`, not a second router.

**The two post-run checks are mechanical, not model-judged, because that is what `change-
classification.md` itself requires of them** — "bounded before it starts" and "never edit the
architecture tree" are both *what*, not *how*, and a model asked "did this stay in bounds?" is exactly
the self-report `TOOLKIT-27`'s own precedent already rejected for a different question: a refusal or a
containment violation is checked against what actually happened on disk, never against what the worker
says happened. `TOOLKIT-30` reuses `Router.RunAsync`'s existing strict-subset containment logic
verbatim rather than inventing a second implementation of "is A a subset of B", and `TOOLKIT-31` reuses
`ProtectedPathTripwire` verbatim rather than a second protected-path list that could drift from the
first.

**`ProtectedPathTripwire` is reused unchanged, not widened** — its public surface,
`FindTrippedPath(IReadOnlyList<string> fileScope)` and `Trips(IReadOnlyList<string> fileScope)`, already
takes an arbitrary list of path patterns and answers a pure, mechanical question about them; it carries
no assumption anywhere in its implementation that the list it is handed is a phase's *declared* scope
rather than a worker's *actual* changed-file list. Calling it a second time, after the worker's run,
against a different list is exactly the kind of reuse the type was already built to support — its own
doc comment states the discipline as "every fact here is computed by reading files and matching text,
never by a model call," which holds regardless of whose list is handed in. Widening it would have meant
adding a parameter or a mode it does not need; none was found.

**The containment check runs before the tripwire check, but both always run, and either can escalate
independently** — order does not change which check is authoritative, because neither one is: a bound
violation with no protected-path involvement is exactly as much a stop condition as a protected-path
trip with no bound violation, and `maintain` reports whichever tripped first without suppressing the
other's finding. This mirrors `TOOLKIT-26`/`TOOLKIT-27`'s own ordering rationale in `route.md` — the
mechanical checks run before anything is allowed to stand as complete, and a later check is never
skipped because an earlier one happened to pass.

**No separate permitted-edit-category argument** — `change-classification.md` bounds Maintenance by
file set and by the "interior code and interior tests only" restriction the mode definition already
carries, and `TOOLKIT-31`'s tripwire is exactly the mechanical form of the one edit category Maintenance
can never touch (the architecture tree, `.anneal/work/constraints.md`, `.anneal/work/backlog.md`). A second, separate
edit-category argument alongside the file-scope bound would let a caller declare a category
`SmallFixWorker` already has no way to violate differently than the file-scope bound already
constrains, duplicating a constraint the file-scope argument and the tripwire together already cover
rather than adding one they miss.

**`MaintainReport` is a new record, projecting `WorkerExecutionResult` directly rather than routing
through `RouterOutcome`** — `RouteReport` projects `Process.RouterOutcome`, which carries phase and
reroute-rejection history a `Router` run accumulates; `maintain` never constructs a `Router`, so that
history does not exist for it to project, and reusing `RouteReport` would leave most of its fields
permanently empty for every `maintain` invocation, which is `RouteReport`'s own documented "populated
half" shape stretched to fields that would *always* be the empty half rather than sometimes. A new
record projecting `WorkerExecutionResult` — files changed, summary, or (on escalation) which of
`TOOLKIT-30` or `TOOLKIT-31` tripped and on what path — keeps the same "internal type stays internal,
a public record projects it" discipline `RouteReport` already established, without inheriting fields
that make no sense on this narrower path. It is additive alongside `RouteReport`, not a fourth
incompatible outcome shape: both still report through the same `OperationOutcome`/`WorkerRunResult`
vocabulary underneath: only the public projection differs, and only because the internal shape being
projected genuinely differs.

**Argument shape mirrors `route`'s own positional style** — work item first, file-scope bound entries
following positionally, the same reasoning `route.md` § Decisions already gives for its own
changed-file hints: every other action's own positional style (`probe-rule-owner <rule>`), and no named
option is warranted for a list `Router.RunAsync`/`SmallFixWorker` both already treat as an ordered
sequence of patterns rather than a single flag's value.
