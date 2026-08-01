---
name: Change Tiers
description: Follow these standards to classify a change and determine which documentation must move with it.
---

# Principle

Documentation work is triggered by **contract change**, never by file change. Classifying the change
first is what keeps routine evolution cheap: most changes touch no documentation at all, and that is
the correct outcome, not a gap.

# The Classifying Question

Before starting work, answer one question:

> **Does anything outside this system observe a difference?**

If **no**, the change is Tier 0. Stop classifying and start working.

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
- **Agents**: `architect` updates the contract **first**, then `developer` implements against it,
  then `contract-check` verifies.
- **Tests**: every added or changed clause needs a boundary test named in the clause.
- **Pruning**: the `architect` performs the section-document prune check for the affected system.

# Tier 2 — Structural Change

A system is added, removed, renamed, split, or merged; or the interaction, data flow, or process
boundary between systems changes.

- **Documentation**: `docs/architecture/overview.md` **and** every affected `{system}.md`. Update
  `README.md` only if the product's purpose or audience actually changed — usually it has not.
- **Agents**: `architect` updates `overview.md` and the affected system documents, then `developer`,
  then `contract-check`.
- **Pruning**: prune section documents across every affected system; a removed system's directory is
  deleted entirely.

# Tier Discipline (MANDATORY)

- **Classify before working.** The tier decides the workflow; discovering it afterwards means the
  contract was edited to match the code.
- **Tiers may be raised mid-flight, never silently lowered.** If implementation reveals that a
  consumer would observe a difference, stop, raise the tier, and update the contract before
  continuing.
- **Never split a change to stay at a lower tier.** Landing a contract change as two Tier 0 commits
  produces an undocumented breaking change.
- **When genuinely uncertain between two tiers, choose the higher one** — but do not reflexively
  round up. Habitually treating Tier 0 work as Tier 1 rebuilds exactly the inertia this process
  removes.

# Worked Examples

| Change | Tier | Why |
| --- | --- | --- |
| Extract a helper class from a large one | 0 | No consumer can tell |
| Replace a sort with a faster algorithm | 0 | Same observable results |
| Fix a bug so behavior matches an existing clause | 0 | Contract already promised it |
| Add a defensive regression test | 0 | No behavior change |
| Add an optional field to an API response | 1 | New consumer-observable behavior |
| Tighten input validation | 1 | Narrows a clause; breaking |
| Change an error code | 1 | Consumers branch on it |
| Split a system into two | 2 | System inventory changes |
| Move a subsystem to a background process | 2 | Process boundary changes |
| Add a cache between two systems | 2 | Interaction changes |

# Quality Gates

- [ ] The tier was declared before work started
- [ ] Tier 0 changes left contract tests passing untouched
- [ ] Tier 1 and 2 changes updated the contract before implementation
- [ ] No change was split across commits to avoid a higher tier
- [ ] Prune check was performed for every Tier 1 and Tier 2 change
