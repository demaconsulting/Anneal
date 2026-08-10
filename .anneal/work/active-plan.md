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
and is doing useful work throughout it: `PROCESS-01` through `PROCESS-04`, `PROCESS-07` and
`PROCESS-09` keep passing across a rename and are precisely what catches a botched one, and
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

### S22 — Retire `dispatch.agent.md` by compiling Intake and moving Mode selection into `helper`

`route`, `maintain`, and `stage-contract` already absorb every real job `dispatch.agent.md` still does
*after* Mode is known. What remains in prose is exactly the part that does not earn a second oracle:
read the conversation, decide whether the work is Intake, Change, Maintenance, or Migration, and then
pick the correct compiled path. The backlog entry retiring a compiled `WorkTypeRouter` records why this
stays in the conversational layer: Scope needs repository investigation to resolve real ambiguity, so
`route` earns a model call; Mode does not, because the agent already holding the request either heard it
stated or can clarify it directly. The one missing compiled path is Intake itself, whose current prose
implementation still appends directly to the registers. This stage closes that gap and then removes the
now-empty `dispatch.agent.md`.

**This stage, in order:**

1. Add a compiled `intake` action to the Toolkit and wire it into `dotnet anneal help`, using the same
   operation/report registration pattern the existing `file-skill`, `maintain`, and `stage-contract`
   actions already follow.
2. Rewrite `helper.agent.md` so Mode classification is its own job and direct CLI invocation replaces the
   `dispatch` sub-agent hop: `intake` for filed work, `route` for Change, `maintain` for bounded
   Maintenance, `stage-contract` for caller-declared staged clauses, and an inline read of this file for
   Migration.
3. Delete `.github/agents/dispatch.agent.md` and update the current-state references its removal makes
   false: `AGENTS.md` and `.github/template/AGENTS.pristine.md`, `README.md`,
   `.anneal/architecture/process.md`, `.anneal/architecture/toolkit.md`,
   `.anneal/architecture/toolkit/intake.md`, `.anneal/architecture/toolkit/maintain.md`,
   `.github/standards/change-classification.md`, `.github/template/repository-map.md`,
   `.github/agents/architecture-design.agent.md`, `.github/skills/check-contracts/SKILL.md`, the
   dispatch-routing repository skill(s), and any other file that currently instructs a reader to use
   `dispatch` for present-tense behavior.
4. Add and verify the tests that hold both the new action and the prompt/process changes to their stated
   behavior, then close the stage by deleting this entry back to `None open`.

**`intake` action design:**

- **Surface and argument shape:** `dotnet anneal intake "<work item>"`, one positional work item, named
  plainly alongside `route`, `maintain`, and `stage-contract`. Missing or blank input is a usage error
  under `TOOLKIT-10`.
- **Decision shape:** one `Oracle<TDecision>` pass over a closed typed answer, not a free-form classifier.
  The answer carries `Kind` (`Backlog`, `Assumption`, `Constraint`), `Why`, the bullet text to write or
  propose, the intended constraint section (`Satisfied` or `Not Yet Satisfied`, meaningful only for the
  constraint case), and `HasSufficientEvidence`.
- **Safety bias:** the oracle charter applies `change-classification.md`'s admission test explicitly and,
  when the work item could plausibly be a standing condition rather than a discrete completion, prefers
  the `Constraint` kind over silently filing a backlog or assumption entry. A false-positive constraint
  escalates and asks for admission; a false-negative constraint silently weakens the ratchet, so the
  bias belongs on the safer side.
- **Effects by kind:** `Backlog` appends one bullet to `.anneal/work/backlog.md`; `Assumption` appends
  one bullet to `.anneal/governance/assumptions.md`; `Constraint` never writes
  `.anneal/work/constraints.md` and instead returns `Escalated` with the proposed bullet and target
  section carried in a structured report and rendered in stdout.
- **Unexpected missing registers:** this stage targets the `.anneal/` register layout Anneal itself and the
  existing compiled catalog already read — `.anneal/work/backlog.md` and
  `.anneal/governance/assumptions.md`. Unlike `dispatch.agent.md`, `intake` will not scaffold a missing
  register from the template: restoring shipped layout is `template-sync`'s job, while silently
  recreating governance files inside an Intake write path hides a broken layout under unrelated work. The
  operation escalates, names the missing register, and points at explicit layout repair instead of
  quietly recreating the file. Reconciling the older root-register template shape is a separate change,
  not hidden inside this stage.
- **Category and role:** `OperationCategory.Authoring`, because backlog and assumption writes change the
  repository; `ModelRole.Light`, because the only judgement is the narrow intake decision.

**`helper.agent.md` design:**

- Helper now owns **Mode** classification itself, after reading `change-classification.md`, exactly as
  `dispatch` Step 1 did. For Change mode it still does **not** decide Scope; it states that `route`
  classifies Scope/Effort and performs the implementation.
- Helper carries forward `dispatch`'s full Intake admission test rather than treating "record this for
  later" as a vague future-tense shortcut: it asks whether the item **completes** or **holds**, and for
  a holding statement whether reality could disprove it. That keeps standing conditions out of `route`
  and funnels the user-admitted-constraint rule through the same front door that now owns Mode.
- Intake work is routed straight to `dotnet anneal intake`; bounded tidy-up work to
  `dotnet anneal maintain`; declared staging to `dotnet anneal stage-contract`; ordinary Change to
  `dotnet anneal route`; verification, lint-fix, stats, and evidence spot-checking keep their existing
  direct-command paths.
- Migration gets no new Toolkit action. Helper checks whether `.anneal/work/active-plan.md` exists and,
  when it does, reads the current-stage section using the same rule the compiled `RepositoryFacts`
  helper already uses — the first `###` heading under `## Current stage`, or none when the section says
  `None open.` — and reports whether staged implementation is already in flight or a migration exists
  with no stage currently opened; when the file does not exist, it reports that `architecture-design` is
  required to produce an approved proposal.
- The report shape moves from helper's current lightweight routing summary to the same metadata-and-work
  structure `dispatch` used, so the direct-command replacement does not silently shed contract-impact,
  register-change, or residual-state reporting.
- One direct-command detail must be carried forward honestly: `AnnealTool` maps non-gating failures from
  authoring/advisory actions to process exit `0`, with the failure rendered in stdout and the
  `InvocationRecord`. Helper's instructions therefore need to read the matching
  `.anneal/logs/records/invocations.jsonl` row after each direct `dotnet anneal ...` call and treat its
  `Outcome` as authoritative, using the shell exit code as a cross-check and stdout only for summaries,
  changed-file lists, and quoted reasons. That is not new behavior in the tool; it is the existing
  dispatcher contract helper must now consume directly because the `dispatch` hop is gone.

**Reference and contract updates this stage must carry:**

- `AGENTS.md` and `.github/template/AGENTS.pristine.md` routing/delegation text must point at helper plus
  the compiled actions, not a retired prose agent.
- `README.md` and `.anneal/architecture/process.md` must remove `dispatch` from the current Process
  inventory and describe helper's direct compiled-action calls instead; `process.md`'s diagram and
  decisions have to land in the same change, because deleting the node without redrawing the current
  composition would violate the one-file ownership rule in the opposite direction.
- `.anneal/architecture/toolkit/intake.md` must be the new action's own contract node; `toolkit.md` only
  absorbs the inventory-level mention that one more shipped action exists.
- `.github/standards/change-classification.md` must name the compiled actions (and helper's direct staged
  path) in its current-state agent guidance, since it is the authoritative classification standard.
- `.github/template/repository-map.md` and any current agent prompt that still says "use `dispatch` for
  ordinary change" need the same mechanical correction.
- `test-process-contract.ps1` must continue to pass with one fewer agent prompt; if a check fails, repair
  the process contract or the payload it proves rather than weakening the check.

**Deliberately not part of this stage:**

- No compiled replacement for helper's own conversation loop.
- No new Migration action; the stage check stays a file read unless review finds a concrete failure that a
  compiled helper cannot avoid.
- No template-scaffolding fallback inside `intake`; if that behavior is judged worth keeping, it belongs
  to an explicit template/layout repair path, not to an Intake append.

**No-silent-loss note to carry into the commit that deletes `dispatch.agent.md`:** the one behavior not
coming forward unchanged is its opportunistic "recreate the missing backlog register from template"
fallback. This stage deliberately drops that repair from the Intake path and treats a missing register as
layout drift that must be repaired explicitly, because hidden scaffolding in an append operation is a
different responsibility than filing work.

**Exit conditions:** `dotnet anneal intake` is shipped, documented, and contract-tested; helper classifies
Mode and invokes `intake`/`route`/`maintain`/`stage-contract` directly; `dispatch.agent.md` is deleted;
all current-state references to it are repaired; `pwsh ./build.ps1` and `pwsh ./lint.ps1` pass.
