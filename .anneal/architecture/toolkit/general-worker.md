---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Process/Workers/GeneralWorker.cs
---

[← Toolkit](../toolkit.md)

# General Worker

`GeneralWorker` is the additive, capability-complete Large worker in the compiled catalog. Unlike
`SmallFixWorker`, it is not structurally forbidden from touching `.anneal/architecture/`: one run may
plan, author contract clauses or architecture documents, implement code and tests, and then verify only
the heavier obligations the actual diff proved were needed.

In this stage only the **Large** tier is implemented. The worker is still Effort-parameterized so later
stages can scale the same pipeline down to Medium and Small rather than redesigning it, but any tier
other than Large fails loudly instead of pretending support exists.

## Contract

### Provides

- **TOOLKIT-58** — `GeneralWorker`'s implemented Large tier is capability-complete: one run may plan,
  author contract clauses, update architecture documents, and implement code and tests, returning one
  merged change summary rather than forcing the work onto separate worker types for those capabilities.
  Any Effort tier other than Large is rejected loudly as unsupported in this stage.
  *Verified by:* `GeneralWorkerCanAuthorContractArchitectureAndCodeInOneRun`

- **TOOLKIT-59** — Before any file is touched, `GeneralWorker` runs a deterministic preflight selector
  over the request framing and changed-file hints. When that framing already implies a contract or
  architecture-document change, `DocumentAuthor` runs before `Developer`; when it implies a multi-system
  or architecture-shaping change, `Planner` runs before both. A request whose framing implies neither
  begins directly at `Developer`.
  *Verified by:* `GeneralWorkerPreflightRunsPlannerAndDocumentAuthorBeforeDeveloperWhenFramingImpliesStructuralShape`

- **TOOLKIT-60** — After authoring, `GeneralWorker` reads the actual git diff and fires only the heavier
  obligations that diff proves were needed: a touched `## Contract` section runs the contract check, a
  touched or coverage-matched architecture document runs the absorbed architecture-agreement obligation,
  and a plausibly-public API signature change widens the verifier question into a tenet check. When the
  diff touches none of those surfaces, those heavier obligations are not run.
  *Verified by:* `GeneralWorkerPostflightFiresOnlyTriggeredChecks`,
  `GeneralWorkerPostflightSkipsChecksForUntouchedSurfaces`

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

### Requires

- **[Runtime](./runtime.md)** — the outcome vocabulary and process notes the worker reports through.
- **[Model Seam](./model-seam.md)** — every `Planner`, `DocumentAuthor`, `Developer`, `Verifier`, and
  absorbed architecture-agreement oracle call the worker composes.
- **[Process](../process.md)** — the catalog, worker brief projection, and protected-path tripwire the
  worker sits inside.
- **ArchitectureCoverage** — `PatchTouchesContractSection` and coverage matching used by the postflight
  selector and the absorbed architecture-agreement obligation.

## Decisions

**Preflight is deterministic in this stage** — the stage only needs the Large, capability-complete
pipeline proven, not a second model-backed classification seam to be designed and contracted. The
selector therefore uses conservative text matching over the request framing and changed-file hints to
decide whether documentation-first or plan-first ordering is already obviously required. The rejected
alternative — a fresh oracle question — would have added a new model-backed judgement surface in the same
stage whose main point is to absorb a previously separate finish-time obligation into the worker.

**The dangerous-path backstop excludes `.anneal/architecture/`, and that exception is explicit rather
than implicit** — `GeneralWorker` exists precisely to make architecture and contract edits possible in one
compiled path, so reusing `ProtectedPathTripwire` unchanged over the actual changed-file list would
escalate every successful run that exercised the worker's new capability. The backstop keeps the same
path-matching primitive and the same fail-closed posture for `.anneal/governance/`, `.anneal/profile/`,
and `.anneal/work/`, while leaving the architecture tree governed instead by the absorbed agreement gate
and the contract check.
