---
name: Change Classification
description: Follow these standards to classify work by mode and tier, and determine which documentation must move with it.
---

# Principle

Documentation work is triggered by **contract change**, never by file change. Classifying the work
first is what keeps routine evolution cheap: most changes touch no documentation at all, and that is
the correct outcome, not a gap.

**This document is the single definition of classification.** No other file restates the modes or
the tiers; they link here.

# Classification Has Two Dimensions

Work is classified twice before it starts, along two independent axes:

- **Mode** — what kind of work this is. Decides what an agent may touch and what "done" means.
- **Tier** — how far the change reaches into published contracts. Decides how much documentation
  moves.

They are orthogonal. Mode is answered first, because three of the four modes fix the tier
automatically.

# Work Modes

| Mode | Triggered by | May touch | Tier |
| --- | --- | --- | --- |
| **Intake** | someone raises a need or an idea | `CONSTRAINTS.md`, `BACKLOG.md`, README assumptions | n/a |
| **Change** | a requested behavior change | code, tests, contracts per tier | 0, 1, or 2 |
| **Maintenance** | available capacity, no requested outcome | interior code and interior tests only | always 0 |
| **Migration** | an approved architecture restructure | everything, in declared stages | n/a |

## Intake

Recording that something is wanted. **No code, no tests, no contract change.** The cheapest
operation in the process, deliberately — if filing a need costs anything, needs stop being filed and
the register goes empty.

Apply the admission test: *does it hold, or does it complete?*

Something that **completes** is a discrete piece of work, finished and stays finished — "add a
`--version` flag". Append one bullet to `BACKLOG.md`.

Something that **holds** is a standing statement, and splits again on who could change it. If it
could only change by a decision, it is a **constraint** — "runs on Windows", "supports .NET
Standard 2.0", "starts in under a second". Append one bullet to `CONSTRAINTS.md`: **Satisfied** if
the current design already meets it, **Not Yet Satisfied** if it does not. A constraint that needs
work before it holds is still a constraint, not backlog.

If instead reality could prove it wrong without anyone changing their mind, it is an **assumption** —
"our users have outbound internet access". Append one bullet to the **Assumptions** section of
`README.md`, per `architecture-documentation.md`. Recording it is all that happens here; judging
whether it is load-bearing belongs to `architecture-design` at the next re-cut.

Whichever file it lands in, the item is recorded and nothing else happens.

## Change

The default mode, and the one the tiers below describe. Something observable must become different
because someone asked for it.

## Maintenance

Improving what is already there without changing what it promises: renaming for clarity, extracting
helpers, deleting dead code, tidying interior tests, bumping a dependency.

- **Maintenance is Tier 0 by definition.** If the work would change a contract, it has left
  maintenance and must be re-classified as Change and re-approved.
- **Maintenance may never edit the architecture tree**, `CONSTRAINTS.md`, or `BACKLOG.md`.
  Discovering an architectural problem during maintenance is a *finding to report*, never a licence
  to act on it.
- **Bounded before it starts.** Declare the file set, the categories of edit permitted, and a
  stopping point. Open-ended "improve the code" work with no bound is not a task.

## Migration

A large, approved restructure landing in stages. Migration is not "a bigger Tier 2" — it differs in
kind, not degree, because it is the only mode permitted to span multiple commits by design.

- Requires an **approved proposal** before any file changes. The proposal is the output of a
  `architecture-design` session — the target decomposition, the stages, and what each stage leaves
  working — and the user approves it.
- The approved proposal lives in **`MIGRATION.md`** at the repository root. It must be tracked,
  because every commit below points at it and agent reports in `.agent-logs/` are not kept. It holds
  the stages and their exit conditions only; the target tree it approves lives in
  `docs/architecture/` and is not restated here.
- Every commit declares Migration mode and references `MIGRATION.md`; splitting work is required
  here, not forbidden.
- Contract clauses that describe systems not yet built are marked planned and carry an exit
  condition (see `system-contracts.md`).
- Ends when every planned clause is satisfied and the exit conditions are met. **Delete
  `MIGRATION.md` in the final commit.** The file existing is what says a migration is in flight, so
  one left behind claims a migration that never ends.

# The Classifying Question (Change Mode)

Within Change mode, answer one question:

> **Does this change what the contract promises?**

If **no**, the change is Tier 0. Stop classifying and start working. Correcting an implementation so
it finally does what the contract already promised is Tier 0, even though the observable output
changes — the promise did not move.

If **yes**, ask the follow-up:

> **Does the set of systems, or the interaction between them, change?**

If no, it is Tier 1. If yes, it is Tier 2.

# Tier 0 — Interior Change

The contract is unchanged. Refactors, performance work, internal restructuring, bug fixes that
restore already-promised behavior, dependency bumps, and test additions.

- **Documentation**: none — unless the change invalidates an existing section document, in which
  case update or delete that one file.
- **Agents**: `developer` alone.
- **Tests**: interior tests may be freely rewritten or deleted. Contract tests must still pass
  untouched — that is the proof the tier is correct.

This should be the large majority of changes. A process where Tier 0 is rare has a contract pitched
at the wrong altitude.

# Tier 1 — Contract Change

A clause is added, narrowed, removed, or given different meaning; or the system's internal
decomposition changes enough that the rationale in its architecture document is now wrong.

- **Documentation**: `docs/architecture/{system}.md` only.
- **Agents**: `architecture-update` updates the contract **first**, then `developer` implements
  against it,
  then `tier-check` verifies.
- **Tests**: every added or changed clause needs a boundary test named in the clause.
- **Pruning**: `architecture-update` performs the section-document prune check for the affected
  system.

# Tier 2 — Structural Change

A system is added, removed, renamed, split, or merged; or the interaction, data flow, or process
boundary between systems changes.

- **Documentation**: `docs/architecture/overview.md` **and** every affected `{system}.md`. Update
  `README.md` only if the product's purpose or audience actually changed — usually it has not.
- **Agents**: `architecture-update` updates `overview.md` and the affected system documents, then
  `developer`,
  then `tier-check`.
- **Pruning**: prune section documents across every affected system; a removed system's directory is
  deleted entirely.

# Discipline (MANDATORY)

- **Classify before working.** Mode and tier decide the workflow; discovering either afterwards means
  the contract was edited to match the code.
- **Modes may be raised mid-flight, never silently lowered.** Maintenance that turns out to need a
  contract change stops and becomes Change. Change that turns out to need a re-cut stops and becomes
  a proposal — an agent never promotes itself into Migration.
- **Tiers may be raised mid-flight, never silently lowered.** If implementation reveals that the
  contract must move, stop, raise the tier, and update the contract before continuing.
- **Never split a change to stay at a lower tier.** Landing a contract change as two Tier 0 commits
  produces an undocumented breaking change. This prohibits *evasion*, not staging: an approved
  Migration is required to land in stages, and every one of its commits declares Migration mode.
- **When genuinely uncertain between two tiers, choose the higher one** — but do not reflexively
  round up. Habitually treating Tier 0 work as Tier 1 rebuilds exactly the inertia this process
  removes.
- **An agent never widens its own authority.** Hitting a boundary that forbids the work is a stop
  condition and a report, never an invitation to edit the boundary.

# Worked Examples

| Work | Mode | Tier | Why |
| --- | --- | --- | --- |
| Extract a helper class from a large one | Change | 0 | No consumer can tell |
| Replace a sort with a faster algorithm | Change | 0 | Same promise |
| Fix a bug so behavior matches an existing clause | Change | 0 | Contract already promised it |
| Add a defensive regression test | Change | 0 | No behavior change |
| Add an optional field to an API response | Change | 1 | New consumer-observable promise |
| Tighten input validation | Change | 1 | Narrows a clause; breaking |
| Change an error code | Change | 1 | Consumers branch on it |
| Split a system into two | Change | 2 | System inventory changes |
| Move a subsystem to a background process | Change | 2 | Process boundary changes |
| Add a cache between two systems | Change | 2 | Interaction changes |
| Record "we will need multi-user eventually" | Intake | n/a | Shapes decomposition; no code |
| Record "we want a dark theme" | Intake | n/a | Ordinary backlog; not architecture-shaping |
| Spend spare capacity tidying a package | Maintenance | 0 | Bounded, interior, no promise moves |
| Rename an unclear private method | Maintenance | 0 | Interior only |
| Re-cut four systems into six for cross-platform | Migration | n/a | Approved restructure, staged |

# Quality Gates

- [ ] The mode and tier were declared before work started
- [ ] Maintenance work stayed Tier 0 and touched no architecture document
- [ ] Intake work touched no code, test, or contract
- [ ] Tier 0 changes left contract tests passing untouched
- [ ] Tier 1 and 2 changes updated the contract before implementation
- [ ] No change was split across commits to avoid a higher tier
- [ ] Every Migration commit declared Migration mode and referenced `MIGRATION.md`
- [ ] A completed Migration deleted `MIGRATION.md`
- [ ] Prune check was performed for every Tier 1 and Tier 2 change
