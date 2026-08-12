---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Process/Workers/GeneralWorker.cs
---

[← Toolkit](../toolkit.md)

# General Worker

`GeneralWorker` is the additive, capability-complete Effort-parameterized worker in the compiled
catalog. Unlike
`SmallFixWorker`, it is not structurally forbidden from touching `.anneal/architecture/`: one run may
plan, author contract clauses or architecture documents, implement code and tests, and then verify only
the heavier obligations the actual diff proved were needed.

Small, Medium, and Large all run the same pipeline. Effort now tunes the repair budgets and the initial
producing-step model-tier suggestion rather than gating capability or forking the worker into separate
implementations.

## Contract

### Provides

- **TOOLKIT-58** — `GeneralWorker` is capability-complete on one pipeline across Small, Medium, and
  Large Effort: one run may plan, author contract clauses, update architecture documents, and
  implement code and tests, returning one merged change summary rather than forcing the work onto
  separate worker types for those capabilities. Effort tunes budgets and producing-step model-tier
  suggestions; it does not select a different control-flow shape.
  *Verified by:* `GeneralWorkerCanAuthorContractArchitectureAndCodeInOneRun`,
  `GeneralWorkerRunsSamePipelineAcrossAllSupportedEfforts`

- **TOOLKIT-59** — Before any file is touched, `GeneralWorker` runs a deterministic preflight selector
  over the request framing and changed-file hints. When that framing already implies a contract or
  architecture-document change, `DocumentAuthor` runs before `Developer`; when it implies a multi-system
  or architecture-shaping change, `Planner` runs before both. A request whose framing implies neither
  begins directly at `Developer`.
  *Verified by:* `GeneralWorkerPreflightRunsPlannerAndDocumentAuthorBeforeDeveloperWhenFramingImpliesStructuralShape`

- **TOOLKIT-60** — After authoring, `GeneralWorker` reads the actual git diff and fires only the heavier
  obligations that diff proves were needed: a touched `## Contract` section runs the contract check, a
  touched or coverage-matched architecture document runs the absorbed architecture-agreement obligation,
  and a plausibly-public API signature change widens the verifier question into a tenet check.
  `Verifier` itself is skipped only when, after excluding Anneal's own `.anneal/logs/` bookkeeping,
  every changed path is documentation-only (`docs/`, `.anneal/architecture/`, or Markdown) and no
  `.anneal/architecture/` `## Contract` section was touched; any code file, test file, mixed surface,
  or unclassified path still runs `Verifier`.
  *Verified by:* `GeneralWorkerPostflightFiresOnlyTriggeredChecks`,
  `GeneralWorkerPostflightSkipsChecksForUntouchedSurfaces`,
  `GeneralWorkerDocsOnlyMarkdownWithoutContractTouchSkipsVerifier`,
  `GeneralWorkerDocsOnlyContractTouchStillRunsVerifier`,
  `GeneralWorkerCodeOrTestDiffAlwaysRunsVerifier`,
  `GeneralWorkerMixedOrAmbiguousSurfaceStillRunsVerifier`

- **TOOLKIT-61** — `GeneralWorker` fails closed on postflight classification. If the diff cannot be read,
  or carries patch content but no parseable changed-file headers, the worker escalates rather than
  silently concluding no heavier obligation applied. It also re-checks the actual changed-file list
  against the dangerous protected-path tripwire (`.anneal/governance/`, `.anneal/profile/`,
  `.anneal/work/`) before completing, so a dangerous edit still escalates even though this worker may
  legitimately touch `.anneal/architecture/`.
  *Verified by:* `GeneralWorkerAmbiguousDiffAnalysisFailsClosed`,
  `GeneralWorkerProtectedPathBackstopStillEscalatesDangerousEdit`

- **TOOLKIT-62** — The architecture-doc/code agreement check is a built-in `GeneralWorker` obligation
  rather than a separate finish-time pass: when the postflight selector fires it, the worker runs the
  same classify/correct/revert machinery `ArchDocAgreementGate` already provides, captures any wording-
  only correction into the completed change summary, and persists neutral findings for contract-level or
  unclassifiable disagreements without silently treating them as agreement.
  *Verified by:* `GeneralWorkerAbsorbedArchGateCorrectsWordingOnlyMismatch`,
  `GeneralWorkerAbsorbedArchGateRevertsCorrectionThatTouchesContract`

- **TOOLKIT-63** — `GeneralWorker` may lower the initial model tier only for producing steps
  (`Planner`, `DocumentAuthor`, `Developer`), according to its Effort-tuned preflight suggestion. A
  `RepairRequired` verdict may escalate only the next repair attempt for the same owner category, never
  speculatively before a failed attempt exists. `Verifier` keeps its fixed trusted tier and is never
  downgraded by Effort or by any preflight suggestion.
  *Verified by:* `GeneralWorkerVerifierRoleNeverDowngradesAcrossEfforts`,
  `GeneralWorkerRepairEscalatesProducingRoleOnlyAfterRepairRequired`

### Requires

- **[Runtime](./runtime.md)** — the outcome vocabulary and process notes the worker reports through.
- **[Model Seam](./model-seam.md)** — every `Planner`, `DocumentAuthor`, `Developer`, `Verifier`, and
  absorbed architecture-agreement oracle call the worker composes.
- **[Process](../process.md)** — the catalog, worker brief projection, and protected-path tripwire the
  worker sits inside.
- **ArchitectureCoverage** — `PatchTouchesContractSection` and coverage matching used by the postflight
  selector and the absorbed architecture-agreement obligation.

## Decisions

**Preflight is deterministic in this stage** — the stage needs the one capability-complete pipeline
proven, not a second model-backed classification seam to be designed and contracted. The selector
therefore uses conservative text matching over the request framing and changed-file hints to decide
whether documentation-first or plan-first ordering is already obviously required. The rejected
alternative — a fresh oracle question — would have added a new model-backed judgement surface in the
same stage whose main point is to absorb a previously separate finish-time obligation into the worker.

**Effort tunes cost, not control flow** — Small, Medium, and Large all take the same steps, but they
start with different repair budgets and producing-step role suggestions. The defaults are Small
documentation/code/tenet budgets `0/1/0` with suggested Planner/DocumentAuthor/Developer roles
`Light/Medium/Medium`; Medium budgets `1/1/0` with `Medium/Medium/Medium`; Large budgets `1/1/1`
with `Medium/Heavy/Heavy`. The rejected alternative was separate Small/Medium worker designs, which
would have duplicated the deterministic postflight selector that already proves when the expensive
checks are actually needed.

**The dangerous-path backstop excludes `.anneal/architecture/`, and that exception is explicit rather
than implicit** — `GeneralWorker` exists precisely to make architecture and contract edits possible in one
compiled path, so reusing `ProtectedPathTripwire` unchanged over the actual changed-file list would
escalate every successful run that exercised the worker's new capability. The backstop keeps the same
path-matching primitive and the same fail-closed posture for `.anneal/governance/`, `.anneal/profile/`,
and `.anneal/work/`, while leaving the architecture tree governed instead by the absorbed agreement gate
and the contract check.
