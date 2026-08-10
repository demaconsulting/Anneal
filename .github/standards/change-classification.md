---
name: Change Classification
description: Follow these standards to classify work by mode and scope, and determine which documentation must move with it.
---

# Principle

Documentation work is triggered by **contract change**, never by file change. Classifying the work
first is what keeps routine evolution cheap: most changes touch no documentation at all, and that is
the correct outcome, not a gap.

**This document is the single definition of classification.** No other file restates the modes or
the scopes; they link here.

# Classification Has Three Dimensions

Work is classified along three independent axes:

- **Mode** — what kind of work this is. Decides what an agent may touch and what "done" means.
- **Scope** — how far the change reaches into published contracts. Decides how much documentation
  moves.
- **Effort** — how much work this takes: lines, files, modules touched. Decides planning and
  verification rigor, and whether the work must be decomposed into phases before it executes.

Mode is answered first, because three of the four modes fix Scope automatically. Scope is answered
before Effort, because Scope depends only on what a change promises, never on how large it turns out
to be. **Effort never substitutes for Scope**: a large mechanical rename can be Massive Effort at
Small Fix scope, and a one-line edit to a public signature can be Small Effort at Contract Change
scope.

# Work Modes

| Mode | Triggered by | May touch | Scope |
| --- | --- | --- | --- |
| **Intake** | someone raises a need or an idea | `.anneal/work/backlog.md`; a `.anneal/governance/assumptions.md` or `.anneal/work/constraints.md` proposal | n/a |
| **Change** | a requested behavior change | code, tests, contracts | Small Fix / Contract Change / Structural Change |
| **Maintenance** | available capacity, no requested outcome | interior code and interior tests only | Small Fix |
| **Migration** | an approved architecture restructure | everything, in declared stages | n/a |

## Intake

Recording that something is wanted. **No code, no tests, no contract change.** Filing to
`.anneal/work/backlog.md` is the cheapest step in the process, deliberately — if filing a need costs
anything, needs stop being filed and that register goes empty.

Apply the admission test: *does it hold, or does it complete?*

Something that **completes** is a discrete piece of work, finished and stays finished — "add a
`--version` flag". Append one bullet to `.anneal/work/backlog.md`, the one destination Intake writes
directly.

Something that **holds** is a standing statement, and splits again on who could change it. If it
could only change by a decision, it is a **constraint** — "runs on Windows", "supports .NET
Standard 2.0", "starts in under a second". It belongs in `.anneal/work/constraints.md` as one bullet:
**Satisfied** if the current design already meets it, **Not Yet Satisfied** if it does not. A
constraint that needs work before it holds is still a constraint, not backlog. **Propose it; do not
append it** — see below.

If instead reality could prove it wrong without anyone changing their mind, it is an **assumption** —
"our users have outbound internet access". Assumptions are carefully researched, load-bearing beliefs,
and `.anneal/governance/` is the most protected content in the repository. **Propose it; do not
append it** — see below.

### Only the User Admits a Constraint or Assumption

**No agent, in any mode, appends an entry to `.anneal/work/constraints.md` or any file under
`.anneal/governance/`.** Only the user admits one. This binds Change, Maintenance and
Migration exactly as Intake; a rule scoped to Intake alone would leave every other path open.

**To propose:** state the exact bullet in the completion report — for a constraint, its section,
**Satisfied** or **Not Yet Satisfied** — and stop. Silence, plausibility, or a general instruction to
improve the repository is never admission; only an explicit yes to *that* item is.

**To admit a constraint**, once the user has confirmed the exact wording, run the deterministic action —
no classification, no model call:

- `dotnet anneal admit-constraint "<exact bullet text>" <satisfied|not-yet-satisfied>` appends
  verbatim to the named section of `.anneal/work/constraints.md`.

**To admit an assumption** (or any entry under `.anneal/governance/` — vision, tenets, or assumptions),
there is no admit action. The agent proposes exact wording and escalates; the user edits the file by
hand. `.anneal/governance/` holds load-bearing narrative prose where placement is a human judgement
call. `.anneal/work/constraints.md` alone keeps the confirm-then-machine-writes pattern because it sits
outside `.anneal/governance/`, is a plain append-only bullet list, and a mechanical append is the right
shape for it — every entry either blocks or gates future work.

**Promotion is still an agent action.** Moving an already-admitted constraint from **Not Yet
Satisfied** to **Satisfied** is not admission — the user already said yes to the condition, and the
move only records the current shape meets it. `route`'s Structural Change worker does this.

**Why these two registers, and not backlog.** The asymmetry is the cost of being wrong. A wrong
backlog line is one stale bullet somebody skips, so that register stays frictionless. A wrong
constraint is a barrier every later change routes around, and removal is a decision, never
bookkeeping — entries are never deleted for being met. A wrong assumption is worse: a silently added
false premise the whole decomposition below it may rest on, not a stale note. Requiring the user's
admission buys back the only exit each ratchet otherwise lacks.

### A Citation Is Not a Derivation

A sentence that binds future work must carry its own reasoning inline. Naming another rule or
invariant as the reason is not the same as showing that the named rule actually implies the claim —
a citation can be wrong, and once written it reads with that rule's full authority whether or not
anyone checked. This applies wherever a binding-sounding claim is written, not only inside
`constraints.md` — an `active-plan.md` stage entry, a Decisions paragraph, a routing table row. Before
writing "per the X invariant," reread X and confirm the claim actually follows; if it doesn't
obviously follow, that is evidence the claim doesn't belong, not that it needs a better citation.

### A Constraint Says What, Not How

**A constraint states what the architecture is held to, never the mechanism that achieves it.** A
body that explains a mechanism is describing rather than constraining, and it must be amended every
time that mechanism changes — so it rots, while the condition it was meant to protect stays true.
Write the shorter, blunter statement; if a sentence explains how something works, cut it.

*"Installation is by a provided script"* is a **what**: it holds no matter whether the script copies
directories, restores a .NET tool, or both. *"The payload installs by file copy alone"* and *"The
process is enforceable by one mechanical check"* are **how** — both were removed because each named a
mechanism (a file copy, a single check) that later work outgrew, forcing an amendment that a **what**
would never have needed.

Actionable form: a constraint whose body explains a mechanism is describing, not constraining. State
the condition and stop.

## Change

The default mode, and the one the scopes below describe. Something observable must become different
because someone asked for it.

## Maintenance

Improving what is already there without changing what it promises: renaming for clarity, extracting
helpers, deleting dead code, tidying interior tests, bumping a dependency.

- **Maintenance is Small Fix by definition.** If the work would change a contract, it has left
  maintenance and must be re-classified as Change and re-approved.
- **Maintenance may never edit the architecture tree**, `.anneal/work/constraints.md`, or `.anneal/work/backlog.md`.
  Discovering an architectural problem during maintenance is a *finding to report*, never a license
  to act on it.
- **Bounded before it starts.** Declare the file set, the categories of edit permitted, and a
  stopping point. Open-ended "improve the code" work with no bound is not a task.
- **A periodic, eventually self-triggered structural-cohesion sweep is ordinary Maintenance**, bounded
  as: whole repository, read-only, stopping at a findings report. A sweep that writes nothing has no
  unbounded scope to forbid. Its report states only what was observed, never a proposed remediation —
  any fix is separately-bounded work. This is a second, complementary defense against drift
  accumulating across separate requests over time, a gap the Massive Effort cumulative check (below)
  cannot see because it only evaluates one request's own phase set. Neither layer replaces the other.

## Migration

A large, approved restructure landing in stages. Migration is not "a bigger Structural Change" — it
differs in kind, not degree, because it is the only mode permitted to span multiple commits by
design.

- Requires an **approved proposal** before any file changes. The proposal is the output of
  `helper`'s boundary-work interview — the target decomposition, the stages, and what each stage
  leaves working — and the user approves it.
- The approved proposal lives in **`.anneal/work/active-plan.md`**. It must be tracked,
  because every commit below points at it and agent reports in `.agent-logs/` are not kept. It holds
  the stages and their exit conditions only; the target tree it approves lives in
  `.anneal/architecture/` and is not restated here.
- Every commit declares Migration mode and references `.anneal/work/active-plan.md`; splitting work is required
  here, not forbidden.
- Contract clauses describing systems not yet built are **planned**: written now, and verified by a
  placeholder that `dotnet anneal check-contracts` reports as an unfulfilled obligation until the stage that
  builds them (see `system-contracts.md`). `.anneal/work/active-plan.md` carries the exit condition for each.
- Ends when every planned clause is satisfied and the exit conditions are met. **Delete
  `.anneal/work/active-plan.md` in the final commit.** The file existing is what says a migration is in flight, so
  one left behind claims a migration that never ends.
- A single stage's own implementation still classifies Scope and Effort like any other work, once a
  human has written the stage and its exit condition. A stage whose implementation turns out Massive
  Effort follows the Massive Effort rules below, with one addition: because a stage's exit condition
  is normally an outcome claim, not a file-scope declaration, its author additionally declares an
  explicit file-scope bound at the point decomposition is first needed — the same declaration
  Maintenance already makes, asked for only when actually needed.

# Effort

Effort is pure magnitude — lines, files, modules touched — independent of what the change promises.
It sets verification rigor, never what documentation moves (Scope, below, sets that). Classify
Effort within Change mode, once Scope is known:

| Effort | Rough size | Rigor |
| --- | --- | --- |
| Small | A few lines; obviously correct | No plan. |
| Medium | Multiple files, one system; ~50-200 lines | Lightweight plan. |
| Large | Interiors of multiple systems | Full plan plus a Tenet Check against `.anneal/work/constraints.md` and affected contracts. |
| Massive | Cannot execute as one unit | Decompose into phases first — see below. |

Effort and Scope never imply one another. A 300-file mechanical rename is Massive Effort at Small
Fix scope and needs no human — it crosses no contract. A one-line public signature change is Small
Effort at Contract Change scope and still moves `{system}.md`.

## Massive Effort Must Be Decomposed

Split into phases, each classified by Scope and Effort, before any phase executes, bounded by two
checks that apply together:

- **A mandatory cumulative check** — the whole proposed phase set, evaluated together: does the
  union cross a boundary no single phase crosses alone? Individually low-scope phases that together
  move a boundary are a higher-scope change hiding in the decomposition. The periodic Maintenance
  sweep above is a second, complementary layer for drift across separate requests over time; it does
  not substitute for this per-request check, which sees phases the sweep cannot yet.
- **A deterministic tripwire** — any phase touching `.anneal/governance/`, `.anneal/profile/`,
  `.anneal/work/`, or `.anneal/architecture/` escalates to the highest scope and a human,
  unconditionally,
  regardless of the cumulative check's verdict — the same files Maintenance is forbidden from
  touching. The `overview.md` stale-sentence exception below still applies.

Generated phases are a **strict subset of already-cleared scope** — file set and edit category
contained within what classification already cleared, never larger; needing more means stop and
re-classify. Decomposition recurses **at most once** (depth cap two): a phase may be decomposed
again only if it too proves Massive, and the result of that second pass may not be decomposed
further — it stops for a human instead.

# The Classifying Question (Change Mode)

Within Change mode, answer one question:

> **Does this change what the contract promises?**

If **no**, the change is Small Fix. Stop classifying and start working. Correcting an implementation
so it finally does what the contract already promised is Small Fix, even though the observable output
changes — the promise did not move.

If **yes**, ask the follow-up:

> **Does the set of systems, or the interaction between them, change?**

If no, it is Contract Change. If yes, it is Structural Change.

# Small Fix

The contract is unchanged. Refactors, performance work, internal restructuring, bug fixes that
restore already-promised behavior, dependency bumps, and test additions.

- **Documentation**: none — unless the change invalidates an existing section document, in which
  case update or delete that one file. A narrow exception: correcting a sentence in
  `.anneal/architecture/overview.md` that is factually stale but states or implies no contract-relevant
  fact — one whose correction does not add, remove, or rename a system, or change a system's stated
  relationship to another system — is Small Fix, not Structural Change. It must not touch the systems
  list, the mermaid diagram, or any sentence a Structural Change would otherwise need to update.
- **Agents**: the agent holding the request runs the compiled toolkit's `route` action directly;
  `helper` does this conversationally for user-invoked work.
- **Tests**: interior tests may be freely rewritten or deleted. Contract tests must still pass
  untouched — that is the proof the scope is correct.

# Contract Change

A clause is added, narrowed, removed, or given different meaning; or the system's internal
decomposition changes enough that the rationale in its architecture document is now wrong.

- **Documentation**: `.anneal/architecture/{system}.md` only.
- **Agents**: the agent holding the request runs `route` directly — one worker updates the contract,
  implements, and verifies in a single pass. Run `dotnet anneal stage-contract` directly, instead,
  only when the caller explicitly asks to stage the contract ahead of implementation, as a deliberate
  planned obligation.
- **Tests**: every added or changed clause needs a boundary test named in the clause.
- **Pruning**: whichever pass authors the change — `route`'s worker, or `stage-contract` — performs
  the section-document prune check for the affected system.

# Structural Change

A system is added, removed, renamed, split, or merged; or the interaction, data flow, or process
boundary between systems changes.

- **Documentation**: `.anneal/architecture/overview.md` **and** every affected `{system}.md`. Update
  `README.md` only if the product's purpose or audience changed — usually it has not.
- **Agents**: the agent holding the request runs the compiled toolkit's `route` action directly;
  `helper` does this conversationally for user-invoked work.
- **Pruning**: prune section documents across every affected system; a removed system's directory is
  deleted entirely.

# Discipline (MANDATORY)

- **Classify before working.** Mode and scope decide the workflow; discovering either afterwards
  means the contract was edited to match the code.
- **Modes may be raised mid-flight, never silently lowered.** Maintenance that turns out to need a
  contract change stops and becomes Change. Change that turns out to need a re-cut stops and becomes
  a proposal — an agent never promotes itself into Migration.
- **Scope may be raised mid-flight, never silently lowered.** If implementation reveals that the
  contract must move, stop, raise the scope, and update the contract before continuing.
- **Never split a change to stay at a lower scope.** Landing a contract change as two Small Fix
  commits produces an undocumented breaking change. This prohibits *evasion*, not staging: an approved
  Migration is required to land in stages, and every one of its commits declares Migration mode.
- **When uncertain between two scopes, choose the higher one** — but do not reflexively
  round up. Habitually treating Small Fix work as Contract Change rebuilds exactly the inertia this
  process removes.
- **An agent never widens its own authority.** Hitting a boundary that forbids the work is a stop
  condition and a report, never an invitation to edit the boundary.
- **Effort never substitutes for Scope, either direction.** A large Effort is not evidence Scope
  must be higher, and a Massive item's own phase-level Small Fix classifications never license
  skipping the cumulative check across the whole phase set.
- **Never decompose to dodge the mandatory cumulative check.** Splitting a Massive item into phases
  that individually dodge the tripwire while collectively crossing it is the same evasion the
  split-to-stay-at-a-lower-scope rule already forbids, applied across phases instead of commits.

# Worked Examples

| Work | Mode | Scope | Why |
| --- | --- | --- | --- |
| Extract a helper class from a large one | Change | Small Fix | No consumer can tell |
| Replace a sort with a faster algorithm | Change | Small Fix | Same promise |
| Fix a bug so behavior matches an existing clause | Change | Small Fix | Contract already promised it |
| Add a defensive regression test | Change | Small Fix | No behavior change |
| Add an optional field to an API response | Change | Contract Change | New consumer-observable promise |
| Tighten input validation | Change | Contract Change | Narrows a clause; breaking |
| Change an error code | Change | Contract Change | Consumers branch on it |
| Split a system into two | Change | Structural Change | System inventory changes |
| Move a subsystem to a background process | Change | Structural Change | Process boundary changes |
| Add a cache between two systems | Change | Structural Change | Interaction changes |
| Record "we will need multi-user eventually" | Intake | n/a | Shapes decomposition; no code |
| Record "we want a dark theme" | Intake | n/a | Ordinary backlog; not architecture-shaping |
| Spend spare capacity tidying a package | Maintenance | Small Fix | Bounded, interior, no promise moves |
| Rename an unclear private method | Maintenance | Small Fix | Interior only |
| Re-cut four systems into six for cross-platform | Migration | n/a | Approved restructure, staged |
| Rename a symbol across 300 files, mechanically | Change | Small Fix | Massive Effort, but crosses no contract |
| Add one optional field, touching one file | Change | Contract Change | Small Effort, but moves a promise |

# Quality Gates

- [ ] The mode and scope were declared before work started
- [ ] Maintenance work stayed Small Fix and touched no architecture document
- [ ] Intake work touched no code, test, or contract
- [ ] Small Fix changes left contract tests passing untouched
- [ ] Contract Change and Structural Change changes updated the contract before implementation
- [ ] No change was split across commits to avoid a higher scope
- [ ] Every Migration commit declared Migration mode and referenced `.anneal/work/active-plan.md`
- [ ] A completed Migration deleted `.anneal/work/active-plan.md`
- [ ] Prune check was performed for every Contract Change and Structural Change
- [ ] Effort was classified independently of Scope, neither inferred from the other
- [ ] Massive Effort was decomposed only after the cumulative check cleared the whole phase set, and
      no phase touched a tripwire path without escalating
