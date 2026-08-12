---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteReport.cs
---

[← Toolkit](../toolkit.md)

# Route

`route` is the first action that ever constructs a `Process.Router` outside a throwaway test harness.
Every worker the migration has landed so far — Small Fix, Contract Change, Structural Change — had only
ever run inside interior tests against a fake endpoint. `route` hands a real work item to a real
`Router`, built with the production worker catalog, and runs whichever compiled worker the routing
oracle selects.

It takes the work item as its first argument and any further arguments as changed-file hints, mirroring
`Router.RunAsync`'s own two parameters. What comes back is a projection of the internal `RouterOutcome`
into the public `RouteReport`: the files a completing worker changed and its summary on success, or what
the run tried, learned, and recommends when no worker completed the work. When the run ends Escalated or
Failed and a worker had already written files before stopping, those files and their summary surface in
the report separately from the completion fields.

## Contract

### Provides

- **TOOLKIT-23** — `route` routes a real work item to this repository's own compiled worker catalog —
  `small-fix`, `contract-change`, `structural-change` — through a real Router, and runs whichever
  worker the routing oracle selects. It succeeds when a selected worker completes the work, escalates
  when the routing oracle or a worker names a step only a person can take, and fails when no route
  exists, a routing budget is exhausted, or the selected worker could not complete the work. On an
  Escalated or Failed outcome, `Router.GroundInterruptedAsync` reconciles the worker's
  self-reported interrupted data against a freshly-read git diff of the working tree before
  populating `RouteReport.FilesChangedBeforeStopping` and `RouteReport.SummaryBeforeStopping`.
  Both fields are never null. The reconciliation follows four rules. (1) When the diff is
  unavailable — because there is no git repository, git is not installed, or the diff command
  fails — the worker's reported values are kept unchanged. (2) When the diff is available but
  empty, the worker's reported values are also kept unchanged. (3) When the diff names files but
  the worker reported none, `FilesChangedBeforeStopping` and `SummaryBeforeStopping` are
  synthesized entirely from the diff, giving the caller an authoritative account of what changed
  even when the worker stopped without recording anything. (4) When both the diff and the worker
  report files and they disagree, `FilesChangedBeforeStopping` is replaced with the diff's
  authoritative file list while the worker's own summary text is kept, with a note appended that
  the reported and observed file sets did not match. Additionally, when `FilesChangedBeforeStopping`
  is non-empty after reconciliation, `route` writes a patch file under `.anneal/logs/` named
  `interrupted-<timestamp>.patch` containing `git diff HEAD -- <those files>` and prints
  `route: pre-triage snapshot written to <path>` alongside the before-stopping output. A normal
  Succeeded run never produces a patch file. The snapshot step is silent and non-throwing when
  there is no git repository, git is unavailable, or the diff is empty: the reported outcome is
  unaffected.
  *Verified by:* `InterruptedRouteContractTests.RouteReportsFilesWrittenBeforeStopping`,
  `InterruptedRouteContractTests.RouteWritesSnapshotPatchOnInterruptedOutcome`,
  `InterruptedRouteContractTests.SucceededRunProducesNoSnapshotPatch`,
  `InterruptedRouteContractTests.SnapshotFailureDoesNotMaskReportedOutcome`

- **TOOLKIT-54** — Whenever `TOOLKIT-23` writes a patch file, `route` also writes a companion JSON
  file at the same path stem with a `.json` extension (e.g. `interrupted-<timestamp>.json` alongside
  `interrupted-<timestamp>.patch`), recording the triage narrative — outcome, recommended next step,
  what was tried, files changed before stopping, a summary of the partial work, and any escalation or
  failure reason — so a later human or agent that only sees a dirty working tree (without having
  watched the live console output) can discover why the run stopped and what remains unverified.
  The JSON companion step is silent and non-throwing: companion failure never masks the reported
  outcome or the patch file written by `TOOLKIT-23`.
  *Verified by:* `InterruptedRouteContractTests.RouteWritesTriageContextJsonAlongsidePatch`

- **TOOLKIT-25** — `route` classifies the routed work item's Effort — Small, Medium, Large, or Massive,
  the closed vocabulary `change-classification.md` defines — in the same pass that selects a worker, and
  reports the classified value alongside whatever outcome the run reaches.
  *Verified by:* `EffortContractTests.RouteReportsClassifiedEffort`

- **TOOLKIT-26** — When `route` classifies a work item's Effort as Massive, it does not select a worker
  for that item directly. It decomposes the item into phases and clears a mandatory cumulative check,
  run once over the whole proposed phase set, before any phase is routed; only once that check clears
  does `route` re-route each phase through the same Router. Every generated phase's declared file scope
  is a strict subset of the file scope the original item's own classification already cleared — never
  equal to it or larger.
  *Verified by:* `DecompositionContractTests.CumulativeCheckClearsBeforeAnyPhaseIsRouted`,
  `DecompositionContractTests.GeneratedPhaseScopeIsStrictSubsetOfClearedScope`

- **TOOLKIT-27** — Any phase whose declared file scope touches `README.md`, anything under
  `.anneal/architecture/`, `.anneal/work/constraints.md`, or `.anneal/work/backlog.md` forces the same escalation outcome `TOOLKIT-23`
  already defines, with a recommended next step naming the file, regardless of what the cumulative check
  `TOOLKIT-26` runs concludes for the phase set as a whole.
  *Verified by:* `DecompositionContractTests.PhaseTouchingProtectedFileForcesEscalation`

- **TOOLKIT-28** — Decomposition recurses through the same Router at most once beyond a Massive item's
  own first decomposition: a phase produced by decomposing a Massive item may itself be decomposed again
  only if it too classifies as Massive, and the phases produced by that second decomposition are never
  decomposed further — routing one of them as Massive again reaches the same escalation outcome
  `TOOLKIT-23` defines, with a recommended next step, instead of decomposing it.
  *Verified by:* `DecompositionContractTests.SecondLevelMassivePhaseEscalatesInsteadOfDecomposing`

- **TOOLKIT-56** — After `SmallFixWorker` completes on the small-fix path, `route` runs a
  model-backed verifier pass — separate from the worker that authored the fix — against each
  architecture document covering any file in the actual git diff (grounded on `git diff` against the
  base ref, never on the worker's self-reported changed-file list). The check runs once per matched
  document. A wording-only mismatch outside the document's `## Contract` section is corrected with a
  narrow inline edit; the edit itself is then mechanically re-checked against the actual diff it
  produced — never trusted on the correcting model's own good behavior — and reverted in favor of a
  neutral finding if it touched `## Contract` despite being told not to. If the mismatch cannot be
  confidently classified as wording-only, `route`
  escalates rather than guessing. A disagreement touching `## Contract` substance, or one that cannot
  be confidently classified as wording-only, is recorded as a neutral finding — neither document nor
  code is presumed at fault — and persisted under `.anneal/logs/` so the run cannot present as a
  silent success. This gate does not run on Contract Change or Structural Change paths.
  *Verified by:* `ArchDocAgreementGateContractTests.RouteSmallFixGateSkipsWhenNoArchDocCoversChangedFiles`,
  `ArchDocAgreementGateContractTests.RouteSmallFixGateRunsOncePerMatchedDocument`,
  `ArchDocAgreementGateContractTests.RouteSmallFixGateCorrectsWordingOnlyMismatch`,
  `ArchDocAgreementGateContractTests.RouteSmallFixGatePersistsContractDisagreementFinding`,
  `ArchDocAgreementGateContractTests.RouteSmallFixGateRevertsCorrectionThatTouchesContract`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from, and the escalation outcome this operation reports through.
- **[Model Seam](./model-seam.md)** — every model call the route oracle, any research pass, and the
  selected worker make.
- **[Process](../process.md)** — `Router`, `WorkerDescriptor`/`WorkerCatalogEntry`, and the three
  compiled workers this operation assembles into a production catalog.
- **ArchitectureCoverage** — `MatchingFiles` and `PatchTouchesContractSection`, used by the
  finish-time agreement gate `TOOLKIT-56` runs after `SmallFixWorker` completes.

## Decisions

**The action name, argument shape, charters, and worker catalog keys were this pass's own judgement
call** — `.anneal/work/active-plan.md`'s S10 entry names exactly this and delegates the specifics to whoever lands the
stage. `route` was chosen over `develop` or `work` because it reads plainly as "hand this repository a
real piece of work and let the routing oracle decide", which is the whole of what the action does. The
work item is a single positional argument rather than a flag, matching every other action's own
positional style (`probe-rule-owner <rule>`); changed-file hints follow it positionally because
`Router.RunAsync` treats them as an optional list, not a named option.

**Every charter is authored fresh, not lifted from a prose agent** — unlike `lint-fix`, which duplicated
`lint-fix.agent.md`'s own guidance because a prose equivalent already existed, the Router and its three
workers have no prose predecessor: `dispatch` played a comparable role before `apply` retired, but its
instructions are written for a conversational agent reading a whole standards tree, not for the bounded
typed questions a route oracle and a worker's own primitives answer. The route charter names each
catalog worker by its exact key so the oracle's answer and `Router`'s own catalog lookup agree by
construction.

**The `DocumentAuthorCharter`'s pruning clause means retiring an obsolete file, never trimming unrelated prose** — the charter
instructs `DocumentAuthor` to prune a subsystem document that no longer earns its place; this means
deleting an entire obsolete file from the repository, not removing or rewriting unrelated sections
(Decisions, Invariants, or other clauses) inside a document that remains in scope. Unrelated
pre-existing content must survive verbatim unless the declared task explicitly requires revising it.
The charter also instructs `DocumentAuthor` to prefer the smallest targeted edit over a whole-file
rewrite, so a `replace_file` call on a file with pre-existing content is a scope violation unless the
entire previous content is itself the target of the declared task.

**`ContractChangeWorker` and `StructuralChangeWorker` pass `scopeDriftCheckInterval: 1` to `DocumentAuthor`** — the default
interval of 5 means the existing diff-grounded Light-role scope-drift oracle (`CheckScopeAsync`) only
runs after five successful edit calls, which lets a single large `replace_file` call complete without
ever crossing the threshold. Passing 1 closes that gap: the scope-drift oracle runs after every
successful edit, so a whole-file overwrite that deletes unrelated content is caught immediately rather
than only after several edits accumulate.

**The production catalog registers workers under the same keys their own interior tests already use** —
`small-fix`, `contract-change`, `structural-change` — so a worker's own test fixtures, this operation's
catalog, and the route charter's own prose all name the same three strings. No fourth key was invented
and none was renamed.

**This operation declares `OperationCategory.Authoring` unconditionally** — including on a run that ends
up only researching, refusing to route, or escalating — because the action as a whole is capable of
writing to the repository, matching `lint-fix`'s own reasoning: a caller must not have to know which
path a given invocation happened to take before it can know whether a failure of this action gates a
build.

**Interrupted-change data is carried in a new dedicated record, not in `WorkerRunResult` or
`ProcessNote`** — `WorkerRunResult.Completed` carries the finding of a worker that reached a typed
answer; an interrupted run never reached one, so reusing or extending that union would contradict its
documented invariant. `ProcessNote` is an append-only diagnostic log, not a structured result carrier;
overloading it for file lists would conflate two different concerns and force callers to parse notes to
recover structured data. A separate `ChangeSetBeforeStopping` record makes the interrupted-change path
explicit and structurally distinct from the completion path.

**Effort is folded into the existing route oracle question, not a second oracle pass** — the router
already asks one question with a closed set of shapes to its answer: select a worker, ask for bounded
research, or report no route. Adding the classified Effort as another field of that same answer, present
whenever the answer selects a worker or reports no route directly, is the same shape scaled by one more
axis, not a second question — the pass still asks "what should happen with this work", and Effort is
part of that answer the same way `RouteResearchScope` is already part of the `NeedResearch` answer
rather than a question of its own. `process.md` § Decisions ("the router asks one narrow typed question
per pass") is the design principle this follows: a second sequential oracle call — classify Effort, then
separately select a worker — would double the per-routing model-call cost on every invocation, including
every Small item that never needed it, and would let the two calls disagree with each other about the
same work item, which is exactly the hazard the Router's two independent budgets (research iterations,
worker reroutes) already exist to keep from compounding, this time between two answers about one call
rather than between counters.

**Decomposition recurses through `Router` itself, with a depth parameter, rather than a separate
decomposer type** — a standalone decomposer would have to re-implement the oracle call, the worker
catalog lookup, the research and reroute budgets, and the `NoRoute`/escalation reporting a second time,
becoming a second place those rules have to be kept in sync with the Router's own, the same
duplication-of-machinery cost this document's other decisions already decline to pay elsewhere. Routing
a decomposed phase back through the same entry point a top-level work item uses means every phase gets
the cumulative check, the tripwire, and the escalation shape by construction, not by two code paths
staying manually consistent. The depth cap of two is carried the same way the existing budgets already
are — as a bound threaded on the call — rather than as state the Router itself has to remember across a
run it does not otherwise track recursion through.
