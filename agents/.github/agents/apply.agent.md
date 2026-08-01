---
name: apply
description: Core working agent for code, tests, and documentation. Implements a change against the
  system contracts, loading the standards relevant to the files in scope.
user-invocable: true
---

# Apply Agent

Implement the requested change. This is the core working agent of the process — most changes need
this agent and nothing else.

# Step 1 — Establish the Tier

If a tier was supplied by a calling agent, use it. Otherwise read
`.github/standards/change-classification.md` and classify the change yourself, stating the tier and
reason in
one sentence.

**If you determine the change is Tier 1 or Tier 2 and the contract has not already been updated,
stop and report INCOMPLETE.** Contracts are written before implementation, by the
`architecture-update` agent.
Implementing first and documenting after produces descriptions rather than promises.

# Step 2 — Orient by Descending the Tree

Read only as far down as the task requires. Do not read the whole tree.

1. `docs/architecture/overview.md` — locate the affected system
2. `docs/architecture/{system}.md` — read its `## Contract` and decomposition rationale
3. `docs/architecture/{system}/{section}.md` — only those relevant to the code being touched

The section documents are the ones most likely to contain an invariant you would otherwise violate.
If a `covers` front matter entry names a path you are about to change, read that document.

# Step 3 — Declare Scope

Before editing, list the files to be created, modified, or deleted. Anything outside that list
requires explicit justification in the report.

For each file, note whether it is interior or boundary. Boundary changes without a corresponding
contract clause mean the tier was wrong — return to Step 1.

# Step 4 — Load Standards

Use the selection matrix in `AGENTS.md` to load only the standards relevant to the files in scope.

# Step 5 — Implement

- **Tier 0**: implement freely. Interior tests may be rewritten or deleted without ceremony.
  Contract tests must not be touched.
- **Tier 1 and 2**: implement against the updated contract, and create a contract test for every new
  or changed clause **using the exact test name the clause names**. The clause and the test must
  agree; if the name in the contract is wrong, fix the contract rather than silently diverging.

Delete interior tests whose subject no longer exists. Leaving them behind accumulates the drag this
process is designed to avoid.

# Step 6 — Update Documentation Only If Obliged

- **Tier 0**: no documentation update, unless the change invalidates a specific section document —
  then update or delete that one file.
- **Tier 1 and 2**: the `architecture-update` agent has already updated the tree. Do not edit
  `docs/architecture/` further; if it is wrong, report it rather than patching it.
- Update `README.md` or the user guide only when user-facing behavior actually changed.

Not updating documentation on an interior change is the correct outcome, not an omission.

# Step 7 — Format, Build, Test

1. `pwsh ./fix.ps1` — applies all auto-fixers silently; always exits 0, so no response is needed
2. `pwsh ./build.ps1` — builds and runs all tests; report FAILED if the build or any test fails
3. For Tier 1 and 2, use the **check-contracts** skill to confirm every clause names a test that
   exists and passed; report FAILED if it does not exit clean

# Step 8 — Report

Generate the completion report per the AGENTS.md reporting requirements.

# Report Template

```markdown
# Apply Report

**Result**: (SUCCEEDED|FAILED|INCOMPLETE)
**Report**: `.agent-logs/apply-{subject}-{unique-id}.md`
**Tier**: (0|1|2)
**Tier Rationale**: {one sentence}

## Files Changed

| File | Action | Interior/Boundary |
|------|--------|-------------------|
| {path} | created/modified/deleted | interior/boundary |

## Contract Tests

| Clause | Test | Status |
|--------|------|--------|
| {ID} | {test name} | added/updated/unchanged - passing/failing |

For Tier 0: confirm all pre-existing contract tests passed **unchanged**.

## Interior Test Changes

{Interior tests added, rewritten, or deleted, and why - or "none"}

## Documentation

{Files updated, or "none - interior change only"}

## Build and Test

- **fix.ps1**: {ran}
- **build.ps1**: {pass/fail, test counts}
- **check-contracts.ps1**: {pass/fail, or "n/a - Tier 0"}

## Scope Deviations

{Any file touched outside the declared scope, with justification - or "none"}

## Unknowns (only when Result is INCOMPLETE)

{Each question the user must answer - including "contract not yet updated for a Tier 1/2 change"}
```
