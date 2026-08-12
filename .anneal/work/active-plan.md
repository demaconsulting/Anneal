---
description: The approved Migration proposal in flight, and its current stage.
maintenance: Written once during boundary work when a Migration proposal is approved; updated as stages land; deleted by the commit landing the final stage.
---

# Migration: from prose agents to compiled processes

This file is the approved proposal every Migration commit references. It exists only while the
migration is in flight; the commit landing the final stage deletes it. It lived at root
`MIGRATION.md` through stage S20; S21 relocated it here without changing its content, as the last
step of making `.anneal/` the sole authoritative source for this repository's own process documents.

## Destination

Anneal becomes its own agent CLI. Work arrives at any point on the complexity spectrum, a router
classifies it and selects one of a catalog of processes, and each process runs as C# state-flow
logic — models do the work, and oracles, meaning narrow typed questions with no side effects, decide
its branches. The prose agents under `.github/agents/` are the bootstrap harness that made this
reachable, and they are dismantled into that catalog. `helper` and `architecture-design` are
absorbed last, because a conversation is the hardest control flow to encode — not because they are
exempt.

The dividing line in [`../governance/vision.md`](../governance/vision.md) holds for the
whole journey: control flow and context assembly become code, judgement stays data. Absorbing an
agent means compiling its loop, never its opinions; its prose becomes content a model is shown.

**Nothing below this altitude is scheduled, and no system documents are written for the
destination.** Contracts for systems that do not exist yet are the speculative documentation this
process refuses, and a tree grows a node only when the node is earned.

## How this migration is planned

**One stage at a time, written the morning it starts.** A stage is one day's work, chosen from the
state of the repository at that moment rather than from a plan made before the work began. Once it
lands, its entry is **deleted from this file**, not archived here — the commit that landed it is the
permanent record, and this file holds only the current or next stage plus the invariants that
constrain every stage regardless of content.

A forward schedule was rejected outright rather than written and amended. The restructuring is
exploratory: prose agents are split, merged and renamed on the way into the catalog, and the shape
of the catalog is decided from work done and success rates observed, not predicted. A sequence
written now would be fiction that later stages would be measured against, and the deeper hazard is
that a plan carries authority once it exists — a discovered better route reads as a deviation from
it rather than as the finding it is.

What replaces the schedule is the invariants below. They constrain every step whatever the step
turns out to be, which a stage list cannot do, because a surprise invalidates a list and cannot
invalidate an invariant.

Stages remain, and the vocabulary is unchanged, because `apply` reads a stage and its exit condition
from this file and `change-classification.md` requires one per stage. Only the forward schedule is
gone.

## Step invariants

These hold after **every** commit, not merely at a stage boundary.

- **Self-hosting** — every commit leaves Anneal able to develop Anneal. Each generation of the
  process builds the next one, so a change that breaks the agents currently doing the work stops the
  migration rather than advancing it. This is the constraint that decides what a stage may contain,
  and it is registered in [constraints.md](constraints.md) rather than owned here.
- **One-way** — a responsibility that has moved from prose into code does not move back. The ratchet
  is what makes an unscheduled migration safe: with no plan to measure against, monotonic direction
  is the only guarantee that a day's work is progress.
- **No silent loss** — behavior a prose agent had that its replacement does not carry is written into
  the commit message that drops it. Deliberately narrowing scope is legitimate; discovering months
  later that something was quietly lost is not.
- **Suspensions are named** — a promise the migration is not keeping appears in the register below,
  with the condition that restores or retires it. There is no unrecorded relaxation, because a
  silently weakened check is worse than no check.

## Suspension register

Contract clauses this migration cannot keep as written, and what holds in their place. Each is keyed
to a **condition**, never to a stage number, so that replanning cannot strand one.

The register is deliberately short. Most of the structural contract is unaffected by this migration
and is doing useful work throughout it: `PROCESS-01` through `PROCESS-03`, `PROCESS-06`, `PROCESS-07`
and `PROCESS-09` keep passing across a rename and are precisely what catches a botched one, and
`PROCESS-I1` is untouched because the mechanical agents were never entry points. They are not
suspended, and suspending them would remove the migration's safety net at the moment it is most
needed. `TOOLKIT-I1` is no longer listed here either: [model-seam.md](../architecture/toolkit/model-seam.md)
retired it when the first writing workers landed and replaced it with `TOOLKIT-I6`, whose explicit
allowlist and path-containment guarantees are the current invariant.

**`TOOLKIT-I3`** — a verdict is reproducible from repository inputs.

- *Cannot hold because* model-backed judgement is not a pure function of the repository.
- *What holds instead*: it holds unchanged for every deterministic operation, which is all that
  gates today.
- *Restored, scoped* to deterministic operations when the first model-backed operation gates.

**[overview.md](../architecture/overview.md)** — every edge is a file, never a call.

- *Cannot hold because* prose agents invoke `dotnet anneal`, and the catalog is reached by calling
  it.
- *What holds instead*: the edge is recorded as a call and confined to the Toolkit boundary.
- *Rewritten, not restored*, when Process is dissolved and the rule has nothing left to describe.

One clause is not suspended but is a live trip-wire worth naming: `PROCESS-03` requires every
standard to be reachable by an agent, so deleting a prose agent that was the only file naming a
standard fails the build. That is the check working. The repair is to relocate the standard or
retire it with the agent — never to silence the clause.

## Current stage

### S26 — Prove Medium live, then retire the three legacy workers and stage-contract

S25 made `GeneralWorker` fact-driven across Small/Medium/Large on one pipeline, but only Small and
Large have been live-validated against a real model — Medium's budgets and role defaults are still an
untested guess. Retiring the fallback path (the three original workers) before closing that gap would
leave nothing proven standing behind Medium if it turns out wrong. Once Medium is proven, nothing left
justifies keeping `SmallFixWorker`, `ContractChangeWorker`, and `StructuralChangeWorker` as separate
capability-walled code paths, or `stage-contract` as a separate action: `GeneralWorker` already covers
every case they did, with capability decided by evidence rather than a static wall. Scope finishes its
move to purely descriptive/reported metadata; Effort becomes the one thing routing selects.

**This stage, in order:**

1. Add a Medium-Effort live trial (following the existing `LiveTrialGeneralWorkerTests.cs` pattern)
   proving Medium's specific repair-budget and producing-step role defaults against a real model. This
   must pass before any deletion below begins.
2. Delete `SmallFixWorker`, `ContractChangeWorker`, `StructuralChangeWorker` (source and their
   dedicated test files) and the `stage-contract` action (`StageContractOperation` and its dedicated
   tests) — `GeneralWorker` already covers every case they handled, per S24/S25.
3. Collapse `RouteOperation`'s worker catalog to a single `general` entry. The routing oracle now
   selects **Effort** (Small/Medium/Large — already-closed vocabulary) rather than choosing among
   worker names; Scope is reported for logs/commit messages only, never a routing input.
4. Update every reference/doc naming the old four-worker catalog: `.anneal/architecture/toolkit/route.md`,
   `general-worker.md`, `process.md` (Composition + Decisions), `toolkit.md`, `maintain.md`,
   `verify-change.md`; delete `stage-contract.md`.
5. Record in the landing commit message exactly what is deliberately dropped rather than silently lost
   per this file's own invariant — e.g., `SmallFixWorker`'s structural forbid and the dedicated
   contract-first/plan-first orderings, all superseded by `GeneralWorker`'s preflight/postflight, not
   quietly lost.

**Reference and contract updates this stage must carry:**

- `.anneal/architecture/toolkit/route.md`, `general-worker.md`, `process.md`, `toolkit.md`,
  `maintain.md`, `verify-change.md` as listed above; `stage-contract.md` deleted.
- Any clause naming a test in a file being deleted must be relocated or retired with the file, never
  left dangling (`PROCESS-03`'s reachability check and the clause-to-test link check both catch this
  if missed).

**Exit conditions:** Medium's live trial passes against a real model; `SmallFixWorker`,
`ContractChangeWorker`, `StructuralChangeWorker`, and `stage-contract` no longer exist in source or
docs; `RouteOperation`'s catalog contains only `general`, selected by Effort; no orphaned standards;
`pwsh ./build.ps1` and `pwsh ./lint.ps1` pass.
