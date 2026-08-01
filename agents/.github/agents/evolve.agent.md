---
name: evolve
description: Entry point for evolutionary change. Classifies the change tier and routes to the
  minimum set of agents needed. Use this for any non-trivial change.
user-invocable: true
---

# Evolve Agent

Route a change through the least process that is correct for it. Most changes touch no
documentation and need only the `developer` agent — reaching that conclusion quickly is this agent's
primary job.

This is deliberately **not** a heavyweight state machine. There is no planning phase and there is
exactly one repair pass. If a change genuinely needs more ceremony than this, it is a Tier 2
structural change and the `architect` agent handles the thinking before implementation starts. If it
needs more than *that*, it is not a change at all — it is a Migration, and this agent stops.

# Step 1 — Classify

Read `.github/standards/change-tiers.md` — it is the single definition of both axes, and this prompt
deliberately does not restate it. Inspect only as much of the repository as needed to answer the
classifying questions, then **state the mode, the tier, and the reason in one sentence** before doing
anything else.

Determine the **mode** first, because three of the four fix the tier automatically:

- **Intake** — record the need per the admission test. Do not proceed to Step 2; there is nothing to
  implement.
- **Maintenance** — restate the declared bound and stopping point, then go straight to Step 3.
- **Migration** — an agent never enters this mode on its own. If the work needs it, stop and report
  INCOMPLETE saying an approved proposal is required.
- **Change** — continue, and determine the tier.

If the request is ambiguous enough that the tier could be either 0 or 1, resolve it by reading the
affected system's `## Contract` rather than by rounding up. Rounding up by habit is how this process
degenerates into the one it replaced.

If the mode or tier cannot be determined without information only the user can supply, stop and
report INCOMPLETE with the specific question.

# Step 2 — Architecture (Tier 1 and Tier 2 only)

Skip entirely for Tier 0.

Call the **architect** agent as a sub-agent with:

- **context**: the user's request, the declared tier, and the systems affected
- **goal**: update the contract and architecture tree to describe the intended end state, and prune
  any section documents that no longer earn their place

If the architect returns INCOMPLETE, stop and report INCOMPLETE with its questions. If it returns
FAILED, stop and report FAILED.

# Step 3 — Implement

Call the **developer** agent as a sub-agent with:

- **context**: the user's request, the declared tier, and — for Tier 1 and 2 — the updated contract
  clauses the implementation must satisfy
- **goal**: implement the change, with contract tests for any new or changed clause

If the developer returns FAILED, go to Step 5.

# Step 4 — Verify

Call the **contract-check** agent as a sub-agent with:

- **context**: the user's request, the declared tier, files changed, and the contract clauses in
  scope
- **goal**: verify the change against its declared tier

If it returns SUCCEEDED, go to Step 5 and report.

If it returns FAILED and the repair pass has **not** been used, call the **developer** agent once
more with the specific findings, then re-run **contract-check**. Do not re-plan and do not re-enter
the architecture step unless the finding is specifically that the tier was misclassified — in which
case restate the corrected tier and resume from Step 2.

If it returns FAILED after the repair pass, go to Step 5 and report FAILED.

# Step 5 — Report

Generate the completion report, save it per the AGENTS.md reporting requirements, and return the
summary to the caller.

# Report Template

```markdown
# Evolve Report

**Result**: (SUCCEEDED|FAILED|INCOMPLETE)
**Report**: `.agent-logs/evolve-{subject}-{unique-id}.md`
**Tier**: (0|1|2)
**Tier Rationale**: {one sentence}
**Repair Pass Used**: (yes|no)

## Contract Impact

{Clauses added, changed, or removed - or "none (Tier 0)"}

## Work Performed

- **Architect**: {report path and summary, or "skipped (Tier 0)"}
- **Developer**: {report path, files changed}
- **Contract Check**: {report path, findings}

## Documentation Changes

{Architecture files updated or deleted, or "none - interior change only"}

## Unknowns (only when Result is INCOMPLETE)

{Each question the user must answer, and what can proceed without it}
```
