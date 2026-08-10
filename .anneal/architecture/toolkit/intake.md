---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/IntakeOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/IntakeReport.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/AdmitAssumptionOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/AdmitConstraintOperation.cs
---

[← Toolkit](../toolkit.md)

# Intake

`intake` is the compiled front door for Intake mode — filing something that is wanted, believed, or
constraining without implementing it. `route`, `maintain`, and `stage-contract` already covered every
post-classification path `dispatch` needed once Mode was known; this action fills the last gap by
compiling the admission test itself. It asks one narrow typed question — backlog, assumption, or
constraint — and then applies the one hard boundary Intake has mechanically: a constraint or assumption
is proposed, never auto-admitted.

The action is deliberately narrower than `route`. It does not inspect the repository, construct a
`Router`, or author code or architecture documents. The repository-root read it does make is fixed and
mechanical: append to one existing register file when the answer is backlog, or report the proposed
bullet and target file or section when the answer is assumption or constraint. If the expected backlog
register is missing, the action escalates rather than silently recreating it; restoring shipped layout
is a separate repair.

`admit-assumption` and `admit-constraint` are the deterministic companions that perform the actual
write once a human has approved exact wording. They make no model call and perform no classification.

## Contract

### Provides

- **TOOLKIT-42** — `intake` takes one Intake work item, asks one narrow typed oracle question applying the
  admission test from `change-classification.md`, and on a `Backlog` answer appends exactly one bullet
  to `.anneal/work/backlog.md`. On an `Assumption` or `Constraint` answer it escalates with the proposed
  bullet text — and, for a constraint, the target section — and leaves all governance and constraints
  files unchanged. A missing or blank work item is a usage error under `TOOLKIT-10`.
  *Verified by:* `IntakeWritesBacklogEntryAndEscalatesAssumptionAndConstraint`

- **TOOLKIT-43** — when that Intake decision classifies the item as a constraint, `intake` never appends
  `.anneal/work/constraints.md`. It escalates instead, carrying the proposed bullet text and its intended
  section — **Satisfied** or **Not Yet Satisfied** — and leaves the file unchanged.
  *Verified by:* `IntakeEscalatesConstraintInsteadOfWritingIt`

- **TOOLKIT-44** — when the backlog register is missing, `intake` escalates naming the missing file
  instead of recreating it. Layout repair stays explicit rather than hidden inside an Intake append.
  *Verified by:* `IntakeEscalatesWhenSelectedRegisterIsMissing`

- **TOOLKIT-45** — when that Intake decision classifies the item as an assumption, `intake` never appends
  `.anneal/governance/assumptions.md`. It escalates instead, carrying the proposed bullet text, and
  leaves the file unchanged.
  *Verified by:* `IntakeEscalatesAssumptionInsteadOfWritingIt`

- **TOOLKIT-46** — `admit-assumption` takes one argument — the exact bullet text — and appends it
  verbatim as a new bullet to `.anneal/governance/assumptions.md`. It makes no model call and performs
  no classification. A missing or blank argument is a usage error under `TOOLKIT-10`.
  *Verified by:* `AdmitAssumptionAppendsBulletVerbatim`

- **TOOLKIT-47** — `admit-constraint` takes two arguments — the exact bullet text and a section
  designator (`satisfied` or `not-yet-satisfied`) — and appends the bullet verbatim under the named
  section of `.anneal/work/constraints.md`. It makes no model call and performs no classification. A
  missing, blank, or unrecognized argument is a usage error under `TOOLKIT-10`.
  *Verified by:* `AdmitConstraintAppendsBulletUnderNamedSection`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome, and finding machinery every action is built from,
  and the escalation outcome this action reports through.
- **[Model Seam](./model-seam.md)** — the single narrow oracle pass answering Intake's classification
  question (used by `intake` only; `admit-assumption` and `admit-constraint` do not use it).
- **[Process](../process.md)** — the user-admitted-constraint-and-assumption rule and the Intake admission
  test this action applies mechanically rather than leaving in a prose agent.

## Decisions

**One narrow oracle pass, not a free-form filer** — the only judgement Intake needs is the admission
test itself: does the item complete, or does it hold, and if it holds is it disprovable or chosen?
That fits the same `Oracle<TDecision>` pattern `route` already uses for its own closed typed decisions,
and does not justify a second research or writing pass.

**Assumptions escalate, for the same reason constraints do** — `.anneal/governance/` is the most
protected content in the repository. A wrong assumption silently written is a false premise the
entire decomposition below it may rest on, not a stale bullet somebody skips. The confirm-before-write
tier the constraint register already uses is the right cost for any file in that directory.

**Bias toward constraint when the wording could plausibly be one** — a false-positive constraint is
recoverable: the action escalates and a person declines or rewrites it. A false-negative constraint
silently writes the wrong register and bypasses the one ratchet `change-classification.md` protects. The
safer-side bias therefore belongs inside the charter, not in a human review that would arrive after the
wrong write already landed.

**`admit-assumption` and `admit-constraint` are separate actions, not flags on `intake`** — splitting
them keeps `intake`'s model-backed classification path fully separate from the deterministic write path.
A calling agent that receives an escalation from `intake` shows the proposal to the user, receives
sign-off on the exact wording, and only then invokes the matching admit action. The two phases — propose
and admit — are never collapsed into one.

**Missing registers escalate rather than being scaffolded from template** — writing a new register file is
not the same responsibility as filing a line into one that already exists. A hidden repair here would
bury repository-layout drift under unrelated Intake work and would recreate governance files without an
explicit layout-repair step. The action reports the missing path and stops instead. This applies to the
backlog register only; `admit-assumption` and `admit-constraint` may similarly escalate on a missing
target file rather than recreating it.
