---
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/VerifyChangeOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/VerifyChangeReport.cs
  - src/DemaConsulting.Anneal.Toolkit/Primitives/DiffCheck.cs
---

[← Toolkit](../toolkit.md)

# Verify Change

`verify-change` is the compiled equivalent of `scope-check.agent.md`'s standalone review job — the
prose predecessor this operation replaced and whose retirement it made possible — for judging a change
already made against its declared scope, without authoring anything. `route`'s Contract Change and
Structural Change paths always run their strict contract check against a change they just made
themselves, so a failing check is unconditionally a defect of that run. A standalone review is
different — it may be asked to judge a change against a repository that already carries pre-existing,
unrelated gaps elsewhere, and `scope-check.agent.md` deliberately treated one specific gap kind as
advisory rather than blocking: an unfulfilled `-Strict` test obligation in a system the change did not
touch. `verify-change` is that narrower judgement, compiled.

No new worker type is introduced, and no `Router` is constructed. `DiffCheck`, `DeterministicCheck`, and
`Verifier` are the same primitives `ContractChangeWorker` and `StructuralChangeWorker` already use for
their own verification half, run here alone against a change a worker did not itself just make. `DiffCheck`
is the one genuinely new primitive this operation needed: every existing "changed file" signal in this
Toolkit (`DevelopmentEnvelope.FilesChanged`, `RepositoryFacts.ChangedFileHints`) is a caller- or
model-supplied hint, never one read from the repository, and judging which system a change touched needs
ground truth, not a hint.

## Contract

### Provides

- **TOOLKIT-35** — `verify-change` reads the working tree's diff against `HEAD` (or an optional declared
  base reference), runs `build.ps1` and a strict `check-contracts` pass, classifies the strict pass's
  failures (`TOOLKIT-36`), and asks a verifier to judge contract conformance, scope honesty, and
  architecture-tree accuracy from the diff as evidence. It succeeds when both checks and the verifier
  pass; refuses when the verifier judges its evidence insufficient; escalates when the verifier concludes
  the change's classification itself needs to change; fails when a check did not pass, the verifier
  reports a concern, or no model could be reached. More than one argument is a usage error under
  `TOOLKIT-10`.
  *Verified by:* `VerifyChangeRunsBuildAndStrictContractCheckThenAsksAVerifier`

- **TOOLKIT-36** — `verify-change` sets aside, as advisory rather than blocking, exactly the failure kind
  `scope-check.agent.md` treated as pre-existing: a strict `check-contracts` "unfulfilled test obligation"
  error naming a document whose file is not among the diff's changed files. Every other `check-contracts`
  failure — a malformed clause, a duplicate ID, a stale or missing test result — remains blocking
  regardless of which system it names. When the diff could not be read, no exception is applied at all:
  every unfulfilled obligation blocks, the same as an unmodified strict `check-contracts` run would
  report, since the change's actual scope cannot be established.
  *Verified by:* `VerifyChangeSetsAsideAnUnfulfilledObligationInAnUntouchedSystem`

- **TOOLKIT-37** — `verify-change` declares `OperationCategory.Advisory`: a `Failed` outcome reports
  concerns to whichever agent or person invoked it and never gates a build, matching exactly how
  `scope-check.agent.md` was used before its retirement. It edits nothing in the repository.
  *Verified by:* `VerifyChangeNeverGatesRegardlessOfOutcome`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome and finding machinery every operation is built
  from.
- **[Model Seam](./model-seam.md)** — every model call `Verifier`'s own judgement pass makes.
- **[Process](../process.md)** — `ContractCheckRunner`, reused unchanged from the machinery `route`'s
  workers already built.

## Decisions

**No new worker type, and no routing-oracle pass** — `DiffCheck`, `DeterministicCheck`, and `Verifier`
are already proven inside `ContractChangeWorker`/`StructuralChangeWorker`; a standalone review is a
narrower use of the same primitives, not a second `route`. `verify-change` composes them directly, the
same "one oracle pass, not two" reasoning `maintain.md` and `stage-contract.md` already apply to their
own fronts.

**The advisory exception is resolved before any `CheckFinding` is constructed, not taught to `Verifier`**
— `Verifier` hard-fails on any failing deterministic finding before a model is ever consulted, which is
exactly right for a worker judging a change it just authored itself. Teaching `Verifier` a severity
concept so one caller could set aside a specific failure kind would give every other caller a concept
it would have to reason about and never use. Judging "did this diff touch that system" is itself a
deterministic, mechanical fact, so `verify-change` resolves it itself, constructing a single `CheckFinding`
already classified as passed or failed by the time `Verifier` ever sees it.

**`diffEvidence` is shared `Verifier` plumbing, not something `verify-change` introduced** — all three
callers (`ContractChangeWorker`, `StructuralChangeWorker`, and `VerifyChangeOperation`) pass a
`DiffFinding` as the `diffEvidence` parameter to `Verifier.VerifyAsync`, so the model always sees the
actual patch text alongside the changed-file list rather than a caller-supplied hint. `verify-change`'s
own distinguishing need is narrower: it is the only caller that reads the diff against a non-`HEAD` base
reference, because it judges a change a worker did not itself just make and the caller must be able to
name any declared base ref.

**Declared `OperationCategory.Advisory`, not `Enforcement`** — matching how `scope-check.agent.md` was
used before its retirement: it reports back and its caller decides. Making this operation `Enforcement`
would let a model-backed verdict fail a build for the first time in this Toolkit, directly implicating
`TOOLKIT-I3`'s suspension — a separate, deliberate design decision this operation does not make.

**Conservative fallback when the diff is unavailable** — if `git diff` cannot be read (not a repository,
`git` missing, a timeout), `verify-change` does not guess at scope from a stale or absent hint. Every
unfulfilled obligation reports as blocking, the same as an unmodified strict `check-contracts` run,
because a permissive default here would be silently more forgiving than running the check by hand.
