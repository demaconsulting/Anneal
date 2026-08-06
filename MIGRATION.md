# Migration: from prose agents to compiled processes

This file is the approved proposal every Migration commit references. It exists only while the
migration is in flight; the commit landing the final stage deletes it.

## Destination

Anneal becomes its own agent CLI. Work arrives at any point on the complexity spectrum, a router
classifies it and selects one of a catalog of processes, and each process runs as C# state-flow
logic — models do the work, and oracles, meaning narrow typed questions with no side effects, decide
its branches. The prose agents under `.github/agents/` are the bootstrap harness that made this
reachable, and they are dismantled into that catalog. `helper` and `architecture-design` are
absorbed last, because a conversation is the hardest control flow to encode — not because they are
exempt.

The dividing line in [README.md](README.md) § Direction holds for the whole journey: control flow
and context assembly become code, judgement stays data. Absorbing an agent means compiling its loop,
never its opinions; its prose becomes content a model is shown.

**Nothing below this altitude is scheduled, and no system documents are written for the
destination.** Contracts for systems that do not exist yet are the speculative documentation this
process refuses, and a tree grows a node only when the node is earned.

## How this migration is planned

**One stage at a time, written the morning it starts.** A stage is one day's work, chosen from the
state of the repository at that moment rather than from a plan made before the work began. When it
lands, its entry moves to the log below and the next stage is written against what the day actually
produced.

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
  and it is registered in [CONSTRAINTS.md](CONSTRAINTS.md) rather than owned here.
- **One-way** — a responsibility that has moved from prose into code does not move back. The ratchet
  is what makes an unscheduled migration safe: with no plan to measure against, monotonic direction
  is the only guarantee that a day's work is progress.
- **No silent loss** — behavior a prose agent had that its replacement does not carry is written to
  the log below in the same commit that drops it. Deliberately narrowing scope is legitimate;
  discovering months later that something was quietly lost is not.
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
needed.

**`TOOLKIT-I1`** — model tool grants are read-only.

- *Cannot hold because* a process that writes code cannot be granted read-only tools.
- *What holds instead*: grants stay an explicit allowlist, never absent, and read-only holds for
  every operation that does not write.
- *Retired* the day the first writing process lands, replaced by a clause that keeps the allowlist
  requirement without the read-only one.

**`TOOLKIT-I3`** — a verdict is reproducible from repository inputs.

- *Cannot hold because* model-backed judgement is not a pure function of the repository.
- *What holds instead*: it holds unchanged for every deterministic operation, which is all that
  gates today.
- *Restored, scoped* to deterministic operations when the first model-backed operation gates.

**[overview.md](docs/architecture/overview.md)** — every edge is a file, never a call.

- *Cannot hold because* prose agents invoke `dotnet anneal`, and the catalog is reached by calling
  it.
- *What holds instead*: the edge is recorded as a call and confined to the Toolkit boundary.
- *Rewritten, not restored*, when Process is dissolved and the rule has nothing left to describe.

One clause is not suspended but is a live trip-wire worth naming: `PROCESS-03` requires every
standard to be reachable by an agent, so deleting a prose agent that was the only file naming a
standard fails the build. That is the check working. The repair is to relocate the standard or
retire it with the agent — never to silence the clause.

## Current stage

### S5 — Re-planning the migration

The destination changed, so the plan describing it is rewritten. The previous S5 — *mechanizing
stable rules* — is superseded: it treated encoding rules in C# as the migration's endpoint, whereas
the destination above makes the whole process catalog compiled, which is a larger claim that its
cautions no longer bound.

No code moves. This stage removes claims that are now known to be false and installs the frame every
later stage runs inside. It carries no risk that a later stage can inherit, which is why it is
first: every subsequent day is planned against this file, so a wrong frame would be copied forward
into all of them.

**Leaves working:** everything. No source file, script or payload behavior changes.

**Exit conditions:** the destination, invariants and suspension register above exist; the
self-hosting entry is admitted to [CONSTRAINTS.md](CONSTRAINTS.md); `README.md` § Direction no longer
claims two agents are never absorbed; [process.md](docs/architecture/process.md) records Process as
terminal; the stale stage references in [toolkit.md](docs/architecture/toolkit.md) and
[runtime.md](docs/architecture/toolkit/runtime.md) are corrected. `pwsh ./lint.ps1` passes.

## Discovery log

Append-only, newest last. Each daily stage begins cold, so this is the only memory between them —
what was tried, what it cost, and which judgement calls were made in flight. An entry graduates into
a Decisions section once it has stopped moving; until then it lives here, where being provisional is
expected rather than a defect.

`check-contracts.ps1` runs **without** `-Strict` until the final stage lands, because planned clauses
close stage by stage and unfulfilled obligations are expected in between.

### S1a — Foundation and the deterministic operation — landed

Anneal acquired `src/`, `test/`, a solution and a working `build.ps1`, which `AGENTS.md` and the
check-contracts skill had both been instructing agents to run despite it not existing. The operation
was `verify-evidence`: deterministic, consulting no model, reporting whether each evidence locator
cited in an agent report is really present at the file and line named.

**S1 was split into S1a and S1b by amendment**, after implementation found the two halves carry
unrelated risk — scaffolding that cannot fail in an unfamiliar way, versus the model seam where every
unknown lives. Bundled, an SDK failure would have blocked the build scaffolding every later stage
needed.

The stage also forced a payload change. `check-contracts.ps1` modeled one repository as having one
test framework, and once Anneal had two no combination of its parameters expressed the layout. The
alternative — emitting TRX from PowerShell purely to impersonate a C# result — was rejected because
hand-written result parsing in PowerShell is the cost this system exists to stop paying.

**Four documentation claims became false as it landed, and only three were predicted.** That ratio is
the reason the log exists.

### S1b — The model seam — landed

The provider, the capability roles, and the schema-last probe: a conversation whose response schema
is presented after the reasoning rather than before it. The operation was `probe-rule-owner`.

**The schema-last bet survived its first falsifier.** Parse-failure rate measured 0/16 on SDK 1.0.8,
all decoded on the first reply, none rescued by retry. Re-measure when the response record grows past
three properties — all three are required, and a missing member is what would most likely trip it.

**Pinning a dependency from a reference implementation inherits its rot.** The SDK version was copied
from an earlier project and was two stable releases behind. Correcting it revealed a real output-token
ceiling that had already been reported as "the SDK has no such knob", reversing that finding.

### S2 — Shipping it — landed

Template gained `.config/dotnet-tools.json`, roles became configurable per repository, invocations
began appending structured records, and every model interaction began capturing a transcript.

**The absorption of the agent-report corpus was dropped from this stage by amendment.** No correct
implementation could have satisfied it: `TOOLKIT-08` records the Toolkit's own invocations and says
nothing about agent behavior. Widening it would have admitted a promise the user had not admitted.
`agent-metrics.ps1` therefore **survives**, because it reads a corpus the structured records do not
replace.

### S4a and S4b — ContractCheck ported, then cut over — landed

`check-contracts.ps1` was reimplemented as a Toolkit operation, proven at parity against the script's
own 43-case suite, and only then made the gate. **S4 was split by amendment** so that a defect in the
port could not arrive in the same change as the removal of the thing it was checked against.

**S4b's merge became a descent, not a flattening**, after the tree was generalized to carry contracts
at any depth: `contract-check.md` became a section document beneath Toolkit, keeping its own contract.

### S3 — Auditing verdicts — planned, never scheduled

An operation sampling reported SUCCEEDED verdicts and re-checking them against their evidence,
targeting the failure the report corpus shows to be both real and silent: a false FAILED is loud and
gets fixed, while a false SUCCEEDED ships unnoticed and is invisible to any metric an agent produces
about itself. It remains a **candidate**, not a stage, and the reasoning is retained here because the
failure it targets has not gone away.

Its own exit condition remains the right one whenever it is picked up: a sample of historical reports
audited with the result recorded, **including how often the audit itself is wrong**, since an
unreliable auditor of verdicts is worse than none.

### S5 — Re-planning the migration — in flight

Recorded here as it lands.

**A green report is not a verified one.** An independent review by a *different* model caught a
contract test that had been widened beyond the clause it verifies — a defect a same-model second pass
had already passed. Different-model review is worth repeating periodically, not once.

**Sub-agent claims of "already covered" need spot-checking.** A porting agent's self-report conflated
two similarly-named fixtures and claimed one existing test covered both; it covered one.

**The self-hosting invariant immediately rejected its own author's first design.** A stage-less
`MIGRATION.md` was drafted before `apply.agent.md` and `change-classification.md` were found to read
a stage and an exit condition from this file. Removing stages would have broken Migration mode for
every prose agent still doing the work.
