---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/MaintainOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/MaintainReport.cs
---

[← Toolkit](../toolkit.md)

# Maintain

`maintain` is the compiled front door for Maintenance mode: bounded, interior improvement work whose
scope the caller has already fixed before the action begins. It therefore does not construct a `Router`
or ask a routing oracle to classify the work again. Instead it runs `GeneralWorker` directly at
`Effort.Small`, with Maintenance-specific mechanical checks wrapped around the run.

What `maintain` adds is not a second authoring capability wall but two enforcement steps the worker alone
does not own for this front door: declared-bound containment over the actual changed files, and the
protected-path tripwire over those same actual changes. Maintenance also keeps an explicit finish-time
architecture-agreement pass after completion; unlike routed work, that wording-only correction remains a
front-door exception rather than part of the worker's general absorbed path.

## Contract

### Provides

- **TOOLKIT-29** — `maintain` takes a Maintenance work item and a declared file-scope bound and runs it
  directly against `GeneralWorker` fixed to `Effort.Small`, asking no routing oracle. A caller naming no
  file-scope entries reaches a usage error under `TOOLKIT-10`: unbounded Maintenance work has no bound to
  declare. It succeeds when the worker completes the work within its declared bound; escalates when the
  worker names a reroute, when a protected-path write is refused, or when either post-run check below
  (`TOOLKIT-30`, `TOOLKIT-31`) trips; and fails when the worker's repair budget is exhausted or no model
  could be reached.
  *Verified by:* `MaintainRunsDeclaredBoundDirectlyThroughGeneralWorker`

- **TOOLKIT-30** — After the worker's run, `maintain` checks the actual files the worker reported having
  changed against the declared file-scope bound by strict-subset containment. Every changed file must be
  contained by an entry the bound declared; a changed file the bound did not cover forces the same
  escalation outcome `TOOLKIT-29` defines, naming the offending file, rather than being reported as an
  unqualified success.
  *Verified by:* `MaintainEscalatesWhenActualChangesExceedTheDeclaredBound`

- **TOOLKIT-31** — After the worker's run, `maintain` runs `ProtectedPathTripwire` against the worker's
  actual changed-file list rather than only the declared bound, and forces the same escalation outcome
  whenever it trips, naming the tripped path, regardless of what the containment check (`TOOLKIT-30`)
  concludes for the same run. This is the mechanical enforcement of Maintenance's prohibition on editing
  protected work/governance paths, and it also backstops any dangerous edit the worker itself surfaced.
  *Verified by:* `MaintainEscalatesWhenActualChangesTripTheProtectedPathCheck`

- **TOOLKIT-49** — When `maintain` escalates or fails and the worker had already written files to disk
  before stopping, it writes `interrupted-<timestamp>.patch` under `.anneal/logs/` via the shared
  `InterruptedDiffSnapshot` helper and reports the path. A completed run never produces a patch file.
  Snapshot failure is silent and non-throwing when there is no git repository, git is unavailable, or the
  diff is empty.
  *Verified by:* `InterruptedMaintainContractTests.MaintainWritesSnapshotPatchOnInterruptedOutcome`,
  `InterruptedMaintainContractTests.SucceededMaintainRunProducesNoSnapshotPatch`,
  `InterruptedMaintainContractTests.MaintainSnapshotFailureDoesNotMaskReportedOutcome`

- **TOOLKIT-55** — Whenever `TOOLKIT-49` writes a patch file, `maintain` also writes a companion JSON file
  at the same path stem with a `.json` extension, recording the triage narrative. Companion-write failure
  is silent and never masks the reported outcome or the patch file.
  *Verified by:* `InterruptedMaintainContractTests.MaintainWritesTriageContextJsonAlongsidePatch`

- **TOOLKIT-57** — After `GeneralWorker` completes on the Maintenance path, `maintain` runs an explicit
  finish-time architecture doc/code agreement gate. The gate runs once per matched architecture document,
  may apply only the wording-only-outside-`## Contract` correction, mechanically re-checks that
  correction's actual diff, and reverts it if it touched `## Contract` despite being told not to.
  Contract-level or unclassifiable disagreements are persisted as neutral findings under `.anneal/logs/`
  rather than silently treated as agreement.
  *Verified by:* `ArchDocAgreementGateContractTests.MaintainArchGateSkipsWhenNoArchDocCoversChangedFiles`,
  `ArchDocAgreementGateContractTests.MaintainArchGateRunsOncePerMatchedDocument`,
  `ArchDocAgreementGateContractTests.MaintainArchGateCorrectsWordingOnlyMismatchOutsideContract`,
  `ArchDocAgreementGateContractTests.MaintainArchGatePersistsContractDisagreementFindingWithoutEditing`,
  `ArchDocAgreementGateContractTests.MaintainArchGateRevertsCorrectionThatTouchesContract`

### Requires

- **[Runtime](./runtime.md)** — the outcome and finding machinery every operation reports through.
- **[Model Seam](./model-seam.md)** — every model call `GeneralWorker` and the explicit architecture gate
  make.
- **[Process](../process.md)** — `GeneralWorker`, `ProtectedPathTripwire`, and interrupted-diff grounding.
- **ArchitectureCoverage** — coverage matching and contract-section detection used by the explicit
  architecture-agreement gate.

## Decisions

**Direct Small-effort execution, no routing pass** — Maintenance mode already fixed the work shape before
this action was invoked, so `maintain` runs `GeneralWorker` directly at `Effort.Small` rather than paying
for a second classifier.

**Bound and protected-path checks stay mechanical** — containment and protected-path enforcement read the
actual changed-file list after the run, never a model's self-report of what it intended to touch.

**Maintenance keeps an explicit architecture gate** — routed work absorbed architecture agreement into
`GeneralWorker`, but Maintenance intentionally preserves a separate post-run wording-only exception rather
than widening the direct Maintenance code path into general architecture editing.
