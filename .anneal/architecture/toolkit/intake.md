---
level: section
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/IntakeOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/IntakeReport.cs
---

[← Toolkit](../toolkit.md)

# Intake

`intake` is the compiled front door for Intake mode — filing something that is wanted, believed, or
constraining without implementing it. `route`, `maintain`, and `stage-contract` already covered every
post-classification path `dispatch` needed once Mode was known; this action fills the last gap by
compiling the admission test itself. It asks one narrow typed question — backlog, assumption, or
constraint — and then applies the one hard boundary Intake has mechanically: a constraint is proposed,
never auto-admitted.

The action is deliberately narrower than `route`. It does not inspect the repository, construct a
`Router`, or author code or architecture documents. The repository-root reads it does make are fixed and
mechanical: append to one existing register file when the answer is backlog or assumption, or report the
proposed bullet and target section when the answer is constraint. If the expected register is missing,
the action escalates rather than silently recreating it; restoring shipped layout is a separate repair.

## Contract

### Provides

- **TOOLKIT-42** — `intake` takes one Intake work item, asks one narrow typed oracle question applying the
  admission test from `change-classification.md`, and on a `Backlog` or `Assumption` answer appends
  exactly one bullet to `.anneal/work/backlog.md` or `.anneal/governance/assumptions.md` respectively. A
  missing or blank work item is a usage error under `TOOLKIT-10`.
  *Verified by:* `IntakeWritesBacklogAndAssumptionEntriesFromOneOracleClassification`

- **TOOLKIT-43** — when that Intake decision classifies the item as a constraint, `intake` never appends
  `.anneal/work/constraints.md`. It escalates instead, carrying the proposed bullet text and its intended
  section — **Satisfied** or **Not Yet Satisfied** — and leaves the file unchanged.
  *Verified by:* `IntakeEscalatesConstraintInsteadOfWritingIt`

- **TOOLKIT-44** — when the selected backlog or assumptions register is missing, `intake` escalates naming
  the missing file instead of recreating it. Layout repair stays explicit rather than hidden inside an
  Intake append.
  *Verified by:* `IntakeEscalatesWhenSelectedRegisterIsMissing`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome, and finding machinery every action is built from,
  and the escalation outcome this action reports through.
- **[Model Seam](./model-seam.md)** — the single narrow oracle pass answering Intake's classification
  question.
- **[Process](../process.md)** — the user-admitted-constraint rule and the Intake admission test this
  action applies mechanically rather than leaving in a prose agent.

## Decisions

**One narrow oracle pass, not a free-form filer** — the only judgement Intake needs is the admission
test itself: does the item complete, or does it hold, and if it holds is it disprovable or chosen?
That fits the same `Oracle<TDecision>` pattern `route` already uses for its own closed typed decisions,
and does not justify a second research or writing pass.

**Bias toward constraint when the wording could plausibly be one** — a false-positive constraint is
recoverable: the action escalates and a person declines or rewrites it. A false-negative constraint
silently writes the wrong register and bypasses the one ratchet `change-classification.md` protects. The
safer-side bias therefore belongs inside the charter, not in a human review that would arrive after the
wrong write already landed.

**Missing registers escalate rather than being scaffolded from template** — writing a new register file is
not the same responsibility as filing a line into one that already exists. A hidden repair here would
bury repository-layout drift under unrelated Intake work and would recreate governance files without an
explicit layout-repair step. The action reports the missing path and stops instead.
