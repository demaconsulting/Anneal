---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Process/Workers/GeneralWorker.cs
---

[← Process](../process.md)

# GeneralWorker

`GeneralWorker` is the one production worker in Anneal's compiled process. `route` always selects this
worker and supplies an Effort classification; `maintain` calls the same worker directly at
`Effort.Small`.

The worker is capability-complete. It is not a safer sidecar beside narrower worker identities. Instead,
it takes the same role trio every time — optional planner, document author, developer — and uses
deterministic preflight and postflight selectors to decide which heavier obligations actually fire for the
specific request and resulting diff.

Effort controls depth, not capability:

- **Small** — narrow, low-risk work; no planner by default and the smallest repair budget.
- **Medium** — typical changes; still no planner by default, but allows one documentation-linked repair
  cycle before completion.
- **Large** — structural or multi-system work; enables planner-first work and the deepest repair budget.

## Contract

### Provides

- **TOOLKIT-64** — `GeneralWorker` is capability-complete on one pipeline across Small, Medium, and
  Large Effort: one run may plan, author contract clauses, update architecture documents, and
  implement code and tests, returning one merged change summary rather than forcing the work onto
  separate worker identities for those capabilities. Effort tunes depth; it does not select a different
  worker or a different top-level control-flow shape.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerCanAuthorContractArchitectureAndCodeInOneRun`,
  `GeneralWorkerContractTests.GeneralWorkerRunsSamePipelineAcrossAllSupportedEfforts`

- **TOOLKIT-65** — Before any file is touched, `GeneralWorker` runs a deterministic preflight selector
  over the request framing and changed-file hints. Contract-clause framing runs `DocumentAuthor` before
  `Developer`; it also runs `Planner` only when the changed-file hints name at least three distinct
  files, so one-file and two-file contract edits stay on the cheap document-first path unless another
  branch applies. Architecture-document-only framing keeps the same document-first, no-planner behavior.
  Multi-system or architecture-shaping framing still runs `Planner` before both authoring roles.
  A request whose framing implies none of those surfaces begins directly at `Developer`.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerPreflightRunsPlannerAndDocumentAuthorBeforeDeveloperWhenFramingImpliesStructuralShape`,
  `GeneralWorkerContractTests.GeneralWorkerRunsSamePipelineAcrossAllSupportedEfforts`,
  `GeneralWorkerContractTests.GeneralWorkerContractClausePreflightRunsPlannerForThreeOrMoreChangedFileHints`,
  `GeneralWorkerContractTests.GeneralWorkerContractClausePreflightSkipsPlannerForOneOrTwoChangedFileHints`

- **TOOLKIT-66** — After authoring, `GeneralWorker` reads the actual git diff and fires only the heavier
  obligations that diff proves were needed: a touched `.anneal/architecture/` `## Contract` section runs
  the contract check, and a touched or `covers:`-matched architecture document runs the absorbed
  architecture-agreement obligation. A public-signature diff widens the verifier question into a tenet
  check only for production paths and only when the changed line looks like a declaration: either a
  recognized public type declaration keyword or a member declaration with a return/type token immediately
  before the identifier and parameter list. Public test-method additions do not fire the tenet check.
  Untouched surfaces do not trigger those checks speculatively.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerPostflightFiresOnlyTriggeredChecks`,
  `GeneralWorkerContractTests.GeneralWorkerPostflightSkipsChecksForUntouchedSurfaces`,
  `GeneralWorkerContractTests.GeneralWorkerPostflightIgnoresPublicTestMethodAdditionForTenetCheck`,
  `GeneralWorkerContractTests.GeneralWorkerPostflightRunsTenetCheckForProductionPublicMemberOrTypeDeclaration`

- **TOOLKIT-67** — `GeneralWorker` skips `Verifier` only when, after excluding Anneal's own
  `.anneal/logs/` bookkeeping, every changed path is documentation-only (`docs/`, Markdown, or
  `.anneal/architecture/`) and no `.anneal/architecture/` `## Contract` section was touched. A
  documentation-only change that *does* touch `## Contract`, any code file, any test file, or any mixed
  or unclassified surface still runs `Verifier`.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerDocsOnlyMarkdownWithoutContractTouchSkipsVerifier`,
  `GeneralWorkerContractTests.GeneralWorkerDocsOnlyContractTouchStillRunsVerifier`,
  `GeneralWorkerContractTests.GeneralWorkerCodeOrTestDiffAlwaysRunsVerifier`,
  `GeneralWorkerContractTests.GeneralWorkerMixedOrAmbiguousSurfaceStillRunsVerifier`

- **TOOLKIT-68** — `GeneralWorker` fails closed on postflight classification. If the diff cannot be read,
  or carries patch content but no parseable changed-file headers, the worker escalates rather than
  silently concluding that no heavier obligation applied.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerAmbiguousDiffAnalysisFailsClosed`

- **TOOLKIT-69** — `GeneralWorker` re-checks the actual changed-file list against the dangerous
  protected-path tripwire (`.anneal/governance/`, `.anneal/profile/`, `.anneal/work/`) before
  completing, so a dangerous edit still escalates even though this worker may legitimately touch
  `.anneal/architecture/`.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerProtectedPathBackstopStillEscalatesDangerousEdit`

- **TOOLKIT-70** — Architecture doc/code agreement is a built-in `GeneralWorker` obligation rather than a
  separate finish-time pass: when the postflight selector fires it, the worker runs the same
  classify/correct/revert machinery `ArchDocAgreementGate` provides. Wording-only drift may be corrected
  inline and merged into the completed summary; a correction that touches `## Contract` is reverted; and
  contract-level or unclassifiable disagreements persist as neutral findings rather than being silently
  treated as agreement.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerAbsorbedArchGateCorrectsWordingOnlyMismatch`,
  `ArchDocAgreementGateContractTests.MaintainArchGateCorrectsWordingOnlyMismatchOutsideContract`,
  `ArchDocAgreementGateContractTests.MaintainArchGateRevertsCorrectionThatTouchesContract`,
  `ArchDocAgreementGateContractTests.MaintainArchGatePersistsContractDisagreementFindingWithoutEditing`

- **TOOLKIT-71** — `GeneralWorker` may lower the initial model tier only for producing steps
  (`Planner`, `DocumentAuthor`, `Developer`), according to its Effort-tuned suggestion. `Verifier`
  keeps its fixed trusted tier and is never downgraded by Effort, and a `RepairRequired` verdict may
  escalate only the next repair attempt for the same owner category, never speculatively before a failed
  attempt exists.
  *Verified by:* `GeneralWorkerContractTests.GeneralWorkerVerifierRoleNeverDowngradesAcrossEfforts`,
  `GeneralWorkerContractTests.GeneralWorkerRepairEscalatesProducingRoleOnlyAfterRepairRequired`

### Requires

- **[Process](../process.md)** — worker briefs, accumulated facts, and Router ownership.
- **[Model Seam](./model-seam.md)** — planner, document author, developer, and verifier model calls.
- **ArchitectureCoverage** — matching changed files to governing architecture documents.

## Decisions

**Capability and depth are separate choices** — S26 keeps one capability-complete worker and moves the
live choice to Effort. This avoids the old static wall where the wrong worker key could block a needed
repair step even after the run proved that step was necessary.

**Deterministic selectors, model-authored content** — the worker does not ask the model which obligations
it is allowed to run. Models author plans, docs, code, and verification responses; the compiled worker
decides when those passes are required.

**Maintenance is an explicit exception around the same worker** — the worker stays reusable enough for
`maintain`, which wraps Small-effort execution with different front-door checks instead of forking worker
identity again.

**TOOLKIT-58 through TOOLKIT-63 are retired here and superseded explicitly, not silently** — the
intermediate S26 collapse into `PROCESS-12`/`PROCESS-13` lost too much contract altitude. The current
mapping restores that precision in-place: `TOOLKIT-58` → `TOOLKIT-64`, `TOOLKIT-59` → `TOOLKIT-65`,
`TOOLKIT-60` → `TOOLKIT-66` + `TOOLKIT-67`, `TOOLKIT-61` → `TOOLKIT-68` + `TOOLKIT-69`,
`TOOLKIT-62` → `TOOLKIT-70`, and `TOOLKIT-63` → `TOOLKIT-71`. The old IDs are no longer listed as
active clauses because these new clauses are the current invariant set.
