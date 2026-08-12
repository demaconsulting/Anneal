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

### S24 — Build a capability-complete general worker, proven alongside the existing three

`SmallFixWorker`, `ContractChangeWorker`, and `StructuralChangeWorker` are gated by a pre-classified
Scope that decides not just documentation obligations but worker **capability**: `SmallFixWorker` is
structurally forbidden from ever touching `.anneal/architecture/` (`ProtectedPathTripwire`,
`TOOLKIT-31`), even for a trivially safe, already-verified wording fix, because it alone has no
oracle-backed verification of its own edits. Two independently-run reviewers agreed this is a real
defect — Scope should decide what gets *reported and which obligations apply*, not which capabilities
a worker structurally lacks — and converged (after correction) on a capability-complete alternative:
general-purpose workers, sized by Effort (Small/Medium/Large), each able to touch any file or contract
a request needs; which
verification obligations actually fire is decided by deterministic preflight (from the request as
stated, before any edit) and postflight (from the actual diff/path/section touched) analysis, never
by a static per-worker capability wall. `ArchDocAgreementGate`'s oracle-classify-and-mechanically-
revert machinery is the proof this is safe; this stage generalizes it into a built-in obligation
rather than a bolted-on finish-time pass.

This stage builds the **Large tier only** — the capability superset — and proves it end to end
against real requests, running *alongside* the existing three workers (which keep serving live
routing unchanged). Deriving Medium and Small tiers by scaling down the same pipeline, and retiring
the three old workers plus `stage-contract` once the new path covers their cases, are separate future
stages — not scheduled here, per this file's own rule against writing a forward schedule.

**This stage, in order:**

1. Add a new worker (`GeneralWorker`, Effort-parameterized) to
   `src/DemaConsulting.Anneal.Toolkit/Process/Workers/`. It always has full capability: it may author
   contract clauses, architecture docs, and code in the same pass. It runs an obligation-selector
   before and after editing:
   - **Preflight** (from the request/brief, before any edit): does the request's own text look like it
     needs a contract update or a multi-system plan? If so, run `DocumentAuthor`/`Planner` first, the
     same ordering `ContractChangeWorker`/`StructuralChangeWorker` already use — this is not
     diff-detectable after the fact.
   - **Postflight** (from the actual diff, after editing): mechanical checks derived from real git
     diff/path/section parsing — did the diff touch a `## Contract` section? touch
     `.anneal/architecture/`? change a public signature? — decide which additional obligations
     (contract-clause verification, `ArchDocAgreementGate`-style doc-agreement checking, tenet check)
     fire. Every trigger must fail closed on ambiguity (run the stronger check, or escalate — never
     silently skip), and the deterministic path tripwires (`ProtectedPathTripwire`'s
     `.anneal/governance/`, `.anneal/work/`, `.anneal/architecture/` set) remain a backstop under any
     oracle-driven trigger, not replaced by one.
   - `ArchDocAgreementGate`'s existing classify/correct/revert logic becomes this worker's built-in
     architecture-doc obligation, invoked whenever the postflight check fires it, rather than staying
     a separate pass `RouteOperation`/`MaintainOperation` bolt on afterward.
2. Wire `GeneralWorker` into `Router.cs`'s catalog as a new, independently selectable entry (its own
   `WorkerCatalogEntry` key) alongside the existing three — it does not replace them yet.
3. Add this worker's own contract node (new clauses in `process.md` and/or a new
   `.anneal/architecture/toolkit/general-worker.md`, decided during implementation) describing its
   provided guarantees, and boundary tests proving: capability-complete editing, preflight/postflight
   obligation firing on real diffs, fail-closed behavior under an ambiguous/ungradeable diff, and that
   the protected-path tripwire still holds as a backstop.
4. Live-validate it end to end against real requests of at least Small-Fix shape (today's cheapest,
   most common case) — the same live-trial bar every prior stage in this migration has cleared before
   being treated as proven, per this file's own precedent (`lint-fix`, `stage-contract`,
   `verify-change` above).

**Reference and contract updates this stage must carry:**

- `process.md`'s Composition/Decisions sections gain an entry recording that a fourth, capability-
  complete worker now exists in the catalog, and why Scope no longer gates capability for it — without
  yet retracting the Scope-name-equals-worker-name decision recorded there for the three existing
  workers, since they are unchanged this stage.
- `.anneal/architecture/toolkit/route.md` gains a note that `GeneralWorker` is a selectable catalog
  entry, without changing today's default routing behavior.
- Any new obligation-selector logic must be covered by `.anneal/architecture/toolkit/model-seam.md` if
  it introduces a new model-backed judgement, per that document's ownership of model-touching
  promises.

**Exit conditions:** `GeneralWorker` exists, is reachable through `Router`'s catalog, is contract-
tested (preflight/postflight obligation firing, fail-closed ambiguity handling, protected-path
backstop, `ArchDocAgreementGate` absorption), and has been live-validated against at least one real
Small-Fix-shaped request without regressing the existing three workers' behavior; `pwsh ./build.ps1`
and `pwsh ./lint.ps1` pass.
