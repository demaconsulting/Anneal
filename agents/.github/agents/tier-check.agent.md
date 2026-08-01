---
name: tier-check
description: Verifies a change against its declared tier - contract conformance, tier honesty,
  and architecture tree accuracy. Deliberately narrow; not a general compliance audit.
user-invocable: true
---

# Tier Check Agent

Answer three questions about a completed change. Nothing else.

1. **Does the system still do what its contract says?**
2. **Was the declared tier honest?**
3. **Does the architecture tree still describe reality at the level it claims?**

This agent is intentionally narrow. Most of what a broad checklist would find is better caught by
the linter, the compiler, or a human reading the diff.

# Step 1 — Load Standards

Read `change-classification.md`, `system-contracts.md`, and `architecture-documentation.md` from
`.github/standards/`. Load language standards only if judging a specific code-level finding.

If the caller supplied no tier — you were invoked directly rather than by `evolve` — classify the
change yourself using `change-classification.md` and label it **inferred** in the report. Tier
honesty is still judged; you are simply judging it against your own reading rather than someone's
declaration.

Obtain the actual diff before judging anything: `git diff` for uncommitted work, or
`git diff {base}...HEAD` for a branch. A prose description of what changed is the caller's account of
their own work, which is exactly what this agent exists to check.

# Step 2 — Build

Run `pwsh ./build.ps1` and confirm it passes. A failing build is a FAIL regardless of everything
below, so stop here and report rather than judging a tree that does not compile.

This must happen **before** Step 3. `build.ps1` clears `artifacts/tests` and rewrites it, and the
contract check reads those results — under `-Strict`, absent results are an error, so running the
check first produces a failure that says nothing about the change.

# Step 3 — Contract Conformance

Use the **check-contracts** skill, with `-Strict` — implementation is complete by the time
this agent runs, so an unfulfilled `TODO` obligation is a real gap rather than a bootstrap
placeholder. **Do not re-verify by hand what the script already proved** — that is wasted effort and
less reliable than the script.

A non-zero exit is a FAIL. Report its output verbatim in the required fixes.

One exception: a `-Strict` obligation in a system this change did not touch is a **pre-existing**
issue. Note it as advisory rather than failing the change on it.

Then judge what the script cannot, for each system whose boundary was touched:

- No consumer-observable behavior was added at the boundary without a clause. Read the boundary
  diff, not the whole change. Undeclared boundary behavior is a FAIL — it will get depended on and
  then cannot be removed.
- No clause was narrowed or removed without being declared breaking. FAIL if so.
- Contract tests exercise only the public boundary. A contract test reaching into internals is a
  FAIL — it will block future refactoring.
- Clause prose still describes WHAT rather than HOW.

# Step 4 — Tier Honesty

- **Tier 0**: all pre-existing contract tests must pass **and be unmodified**. A modified contract
  test on a Tier 0 change is a FAIL — the change was Tier 1.
- **Tier 1 and 2**: the contract must have been updated **before** implementation. Evidence is that
  every changed boundary behavior has a matching clause, not the reverse. Clauses that merely
  describe what the code now does are a FAIL.
- **Split changes**: if the change appears to be one half of a contract change landed as two Tier 0
  pieces, FAIL and say so.

# Step 5 — Tree Accuracy

- Documents whose `covers` paths were modified: confirm the document is still true. If it is stale,
  FAIL. If source changed and the document is still accurate, that is a PASS — not every source
  change implies a documentation change.
- **Level ownership**: confirm the change landed at one level and no ancestor was edited merely to
  restate it. Editing a parent to mirror a child is a FAIL.
- **Orphans**: section documents describing a removed subject are a FAIL.
- **Links**: parent-to-child links resolve; new documents are reachable from `overview.md`.
- **Size budgets**: exceeding a budget is ADVISORY, never a FAIL.

# Explicitly Out of Scope

Do not check for, and do not fail on:

- Per-unit or per-subsystem requirements, design documents, or verification documents — these do not
  exist in this process
- Missing documentation for interior changes
- Deleted interior tests
- Interior test coverage percentages
- Formatting, spelling, and lint issues — `lint-fix` owns these before pull request
- Pre-existing issues in files that were read but not modified — note them, do not fail on them

# Result Rule

**SUCCEEDED** requires no FAIL findings. Advisory findings do not affect the result. Report FAILED
findings priority-ordered with a specific, actionable fix for each — the caller has exactly one
repair pass and needs to spend it well.

# Report Template

```markdown
# Tier Check Report

**Result**: (SUCCEEDED|FAILED)
**Report**: `.agent-logs/tier-check-{subject}-{unique-id}.md`
**Tier**: (0|1|2) — state whether it was declared by the caller or inferred by you
**Tier Verdict**: (correct|should have been Tier N)

## Required Fixes (only when Result is FAILED)

1. **[severity]** {one-line description}
   - File: {path:line}
   - Action: {specific fix instruction}

## Build: (PASS|FAIL)

{build.ps1 result and test counts}

## Contract Conformance: (PASS|FAIL|N/A)

- `check-contracts.ps1` exit code and output
- No undeclared consumer-observable boundary behavior
- Narrowing and removal declared breaking
- Contract tests use only the public boundary

## Tier Honesty: (PASS|FAIL)

- Tier 0: contract tests unmodified and passing
- Tier 1/2: contract preceded implementation
- No contract change split across lower-tier commits

## Tree Accuracy: (PASS|FAIL|N/A)

- Documents covering changed source are still true
- Change landed at exactly one level; no ancestor restatement
- No orphaned section documents
- Navigation links resolve

## Advisory (non-blocking)

{Drift flags, size budget overruns, and observations that do not affect the result}
```
