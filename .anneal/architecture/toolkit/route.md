---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/RouteReport.cs
---

[← Toolkit](../toolkit.md)

# Route

`route` is the compiled front door for Change work whose execution shape is not yet fixed. It constructs
one `Process.Router`, asks one routing oracle question per pass, and runs the single production catalog
entry the oracle can select: `general`. The oracle still classifies Effort — Small, Medium, Large, or
Massive — but Effort now chooses depth within one capability-complete worker rather than choosing among
separate worker identities.

Its arguments stay the same: the work item first, then any changed-file hints. What comes back is a
projection of `RouterOutcome` into `RouteReport`: completed files and summary on success, or the run's
tried steps, learned facts, recommendations, and any interrupted-change snapshot on failure or
escalation.

## Contract

### Provides

- **TOOLKIT-23** — `route` routes a real work item through this repository's compiled worker catalog,
  whose one production entry is `general`, and runs that worker at the Effort the route oracle classified.
  It succeeds when the selected worker completes the work, escalates when the routing oracle or the worker
  names a step only a person can take, and fails when no route exists, a routing budget is exhausted, or
  the selected worker could not complete the work. On an Escalated or Failed outcome,
  `Router.GroundInterruptedAsync` reconciles the worker's self-reported interrupted data against a
  freshly-read git diff before populating `RouteReport.FilesChangedBeforeStopping` and
  `RouteReport.SummaryBeforeStopping`. Both fields are never null. The reconciliation follows four rules:
  unavailable diff keeps the worker's report unchanged; empty diff also keeps it unchanged; a diff with
  files and no worker report synthesizes the fields from the diff; and disagreement between worker and diff
  replaces the file list with the diff's authoritative list while preserving the worker's summary with a
  reconciliation note. When the reconciled file list is non-empty, `route` also writes
  `interrupted-<timestamp>.patch` under `.anneal/logs/` containing `git diff HEAD -- <those files>` and
  reports the path. A normal Succeeded run never produces a patch file. Snapshot failure is silent and
  non-throwing when git is unavailable or the diff is empty.
  *Verified by:* `InterruptedRouteContractTests.RouteReportsFilesWrittenBeforeStopping`,
  `InterruptedRouteContractTests.RouteWritesSnapshotPatchOnInterruptedOutcome`,
  `InterruptedRouteContractTests.SucceededRunProducesNoSnapshotPatch`,
  `InterruptedRouteContractTests.SnapshotFailureDoesNotMaskReportedOutcome`,
  `RouteCatalogContractTests.RouteCatalogCanSelectGeneralWorker`

- **TOOLKIT-54** — Whenever `TOOLKIT-23` writes a patch file, `route` also writes a companion JSON file at
  the same path stem with a `.json` extension, carrying the triage narrative — outcome, recommended next
  step, what was tried, files changed before stopping, a summary of the partial work, and any escalation
  or failure reason. Companion-write failure is silent and never masks the reported outcome or the patch.
  *Verified by:* `InterruptedRouteContractTests.RouteWritesTriageContextJsonAlongsidePatch`

- **TOOLKIT-25** — `route` classifies the routed work item's Effort — Small, Medium, Large, or Massive —
  in the same pass that selects `general` or reports no route, and reports the classified value alongside
  whatever outcome the run reaches.
  *Verified by:* `EffortContractTests.RouteReportsClassifiedEffort`

- **TOOLKIT-26** — When `route` classifies a work item's Effort as Massive, it does not run `general`
  directly. It decomposes the item into phases and clears one cumulative check over the whole proposed
  phase set before any phase is routed; only once that check clears does `route` route each phase through
  the same Router. Every generated phase's declared file scope is a strict subset of the original item's
  already-cleared scope — never equal to it or larger.
  *Verified by:* `DecompositionContractTests.CumulativeCheckClearsBeforeAnyPhaseIsRouted`,
  `DecompositionContractTests.GeneratedPhaseScopeIsStrictSubsetOfClearedScope`

- **TOOLKIT-27** — Any phase whose declared file scope touches `README.md`, anything under
  `.anneal/architecture/`, `.anneal/work/constraints.md`, or `.anneal/work/backlog.md` forces the same
  escalation outcome `TOOLKIT-23` defines, with a recommended next step naming the file, regardless of
  what the cumulative check `TOOLKIT-26` concludes for the phase set.
  *Verified by:* `DecompositionContractTests.PhaseTouchingProtectedFileForcesEscalation`

- **TOOLKIT-28** — Decomposition recurses through the same Router at most once beyond a Massive item's own
  first decomposition: a phase produced by decomposing a Massive item may itself be decomposed again only
  if it too classifies as Massive, and the phases produced by that second decomposition are never
  decomposed further. Routing one of them as Massive again reaches the same escalation outcome
  `TOOLKIT-23` defines instead of decomposing it a third time.
  *Verified by:* `DecompositionContractTests.SecondLevelMassivePhaseEscalatesInsteadOfDecomposing`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome, and finding machinery every operation is built from.
- **[Model Seam](./model-seam.md)** — every model call the route oracle, any research pass, and
  `GeneralWorker` make.
- **[Process](../process.md)** — `Router`, decomposition, and the worker brief/catalog machinery.

## Decisions

**One catalog entry, Effort-selected** — S26 collapses the production catalog to one worker key,
`general`. Capability is no longer statically walled off by worker identity; the routing oracle's live
choice is Effort, and `GeneralWorker`'s own deterministic preflight and postflight selectors decide which
heavier obligations actually fire.

**One oracle question, not two** — the route oracle still answers one narrow typed question per pass:
select `general`, ask for research, or report no route. Effort is part of that answer, not a second pass.

**Massive still decomposes through Router itself** — decomposition stays recursive through the same Router
so every phase inherits the same research, cumulative-check, and escalation rules by construction rather
than by a second decomposition engine staying manually aligned.

**Route no longer owns a separate finish-time architecture gate** — the old route-specific post-run gate
is retired from this operation. Architecture doc/code agreement for routed work now belongs to
`GeneralWorker`'s absorbed obligation, which fires whenever the actual diff proves it is needed.
