---
name: dispatch
description: Entry point for any non-trivial change. Classifies the work by mode and tier, then
  routes it to the minimum set of agents needed.
user-invocable: true
---

# Dispatch Agent

Route a change through the least process that is correct for it. Most changes touch no
documentation and need only the `apply` agent — reaching that conclusion quickly is this agent's
primary job.

This is deliberately **not** a heavyweight state machine. There is no planning phase, and repairs are
capped at one documentation repair and one code repair. If a change genuinely needs more ceremony
than this, it is a Tier 2 structural change and the `architecture-update` agent handles the thinking
before implementation starts. If it needs more than *that*, it is not a change at all — it is a
Migration, and this agent stops.

# Step 1 — Classify

Read `.github/standards/change-classification.md` — it is the single definition of both axes, and
this prompt deliberately does not restate it. Inspect only as much of the repository as needed to
answer the classifying questions, then **state the mode, the tier, and the reason in one sentence**
before doing anything else.

Determine the **mode** first, because three of the four fix the tier automatically:

- **Intake** — append one bullet to whichever destination the admission test selects: `BACKLOG.md`,
  the **Not Yet Satisfied** section of `CONSTRAINTS.md`, or the **Assumptions** section of
  `README.md`. If a register does not exist yet, create it from its template counterpart (resolved
  per the `# Reference Template` section of `AGENTS.md`); if the template cannot be resolved, report
  INCOMPLETE rather than inventing a format. `README.md` always exists — append to it, and never
  recreate it. Say which file you wrote to and why the admission test chose it. Do not proceed to
  Step 2; there is nothing to implement.
- **Maintenance** — restate the declared bound and stopping point, then go straight to Step 3,
  passing that bound to `apply` as a hard limit. If the request has no bound, ask for one instead
  of inventing it. If the work turns out to need a contract change, stop and re-classify as a Change:
  Maintenance is defined by touching nothing a consumer can observe.
- **Migration** — an agent never enters this mode on its own. If `MIGRATION.md` does not exist, report
  INCOMPLETE saying an approved proposal is required and that `architecture-design` produces one. If
  it does exist, the tree is already written and each stage is bounded implementation work: report
  INCOMPLETE naming the stage and directing the user to `apply`, which is what lands a stage.
  Either way you stop here.
- **Change** — continue, and determine the tier.

If the request is ambiguous enough that the tier could be either 0 or 1, resolve it by reading the
affected system's `## Contract` rather than by rounding up. Rounding up by habit is how this process
degenerates into the one it replaced.

If the mode or tier cannot be determined without information only the user can supply, stop and
report INCOMPLETE with the specific question.

# Step 2 — Architecture (Tier 1 and Tier 2 only)

Skip entirely for Tier 0.

Call the **architecture-update** agent as a sub-agent with:

- **context**: the user's request, the declared tier, and the systems affected
- **goal**: update the contract and architecture tree to describe the intended end state, and prune
  any section documents that no longer earn their place

If `architecture-update` returns INCOMPLETE, stop and report INCOMPLETE with its questions. If it
returns FAILED, stop and report FAILED.

# Step 3 — Implement

Call the **apply** agent as a sub-agent with:

- **context**: the user's request, the declared tier, and — for Tier 1 and 2 — the updated contract
  clauses the implementation must satisfy, together with the Implementation Obligations from
  `architecture-update`, which for Tier 2 include source and test directories and the solution file
  to create, move, or delete
- **bound** (Maintenance only): the declared file set, the permitted categories of edit, and the
  stopping point. Editing outside the bound is a scope violation to report, not a judgement call
- **goal**: implement the change, with contract tests for any new or changed clause

If `apply` returns FAILED, go to Step 5.

# Step 4 — Verify

Call the **tier-check** agent as a sub-agent with:

- **context**: the user's request, the declared tier, files changed, and the contract clauses in
  scope
- **goal**: verify the change against its declared tier

If it returns SUCCEEDED, go to Step 5 and report.

If it returns FAILED, you have at most two repairs and they are **not** interchangeable: one
documentation repair and one code repair. Each may be used once. Do not re-plan. When findings of
both kinds are present, take the documentation repair first — a corrected clause changes what the
implementation owes.

- If the finding is that the documentation itself is wrong — a misclassified tier, a missing clause
  for behavior that turned out to be consumer-observable, or a tree left stale — re-enter Step 2.
  Those are `architecture-update`'s to fix, and `apply` is forbidden to edit `docs/architecture/`.
  Continue on through Step 3, because a corrected or added clause needs an implementation and a
  contract test, then re-run **tier-check**. This spends the documentation repair, not the code
  repair, so a code finding that survives can still be repaired once.
- Otherwise call the **apply** agent once more with the specific findings, then re-run
  **tier-check**. This spends the code repair.

Stop early if a repair does not clear the finding it targeted. A finding that survives its owning
agent is a scoping problem, not a repair problem, and spending the other repair on it will not help.

When both repairs are spent, or a repair failed to clear its finding, go to Step 5 and report FAILED.

# Step 5 — Report

Generate the completion report, save it per the AGENTS.md reporting requirements, and return the
summary to the caller.

# Report Template

```markdown
# Dispatch Report

**Result**: (SUCCEEDED|FAILED|INCOMPLETE)
**Report**: `.agent-logs/dispatch-{subject}-{unique-id}.md`
**Tier**: (0|1|2)
**Tier Rationale**: {one sentence}
**Breaking**: (yes|no) — yes if any clause was narrowed or removed
**Repairs Used**: (none | documentation | code | both)

## Contract Impact

{Clauses added, changed, or removed - or "none (Tier 0)"}

## Work Performed

- **Architecture Update**: {report path and summary, or "skipped (Tier 0)"}
- **Apply**: {report path, files changed}
- **Tier Check**: {report path, findings}

## Documentation Changes

{Architecture files updated or deleted, or "none - interior change only"}

## Unknowns (only when Result is INCOMPLETE)

{Each question the user must answer, and what can proceed without it}
```
