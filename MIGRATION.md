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
lands, its entry moves to the log below and the next stage is written against what the day produced.

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

### S18 — Compile a standalone verify path, retiring `scope-check.agent.md` — landed

**Why now.** S17 named this as the last named retirement target: `scope-check.agent.md`'s one
remaining job — verifying a diff that never went through `route` or `maintain` at all (a hand-written
change, an externally contributed one, or anything predating this migration) — was framed as buildable
from `Verifier` and the existing `DeterministicCheck` pair, the exact primitives
`ContractChangeWorker`/`StructuralChangeWorker` already compose for their own verification half. The
new operation, `verify-change` (`VerifyChangeOperation`/`VerifyChangeReport`), was built, unit- and
contract-tested (three clauses, `TOOLKIT-35`/`36`/`37` in `docs/architecture/toolkit/verify-change.md`),
and live-trial validated before this stage's wiring work began — recorded in an earlier commit this
same day, ahead of the retirement itself, mirroring the order S14 and S17 both used (build and validate
the compiled equivalent first; wire and retire second).

**Live-trial validation.** `LiveTrialFixture` gained `RunVerifyChangeAsync`, deliberately the first of
the fixture's `Run*` methods to leave `git diff` un-substituted rather than stubbing it, since
`verify-change`'s whole job is reading a real diff. `LiveTrialVerifyChangeTests` seeded a committed
baseline carrying a real contract clause, made an honest uncommitted Small-Fix-shaped change on top,
ran `verify-change` against a real model through the real `Verifier`, and graded the result with a
real model-backed oracle. The trial passed on the first run. It skips by default under the normal gate,
the same `ANNEAL_LIVE_TRIALS=1` opt-in every other live trial uses.

**What landed.** `helper.agent.md`'s Step 3 table gained one row: verifying a finished change now reads
"run `dotnet anneal verify-change [<base-ref>]` directly, not as a sub-agent," replacing the row that
routed to `scope-check`. Unlike `stage-contract`'s retirement of `architecture-update` at S17, this one
does **not** route through `dispatch`: `verify-change` declares `OperationCategory.Advisory`, edits
nothing, and produces no change for `dispatch` to classify or register, so there is nothing for
`dispatch`'s per-change tracking to do. The relationship is instead the one `lint-fix` already has to
`process.md`'s diagram — a developer or `helper` runs a compiled command directly, and it never appears
in the diagram as a node, because the diagram's solid edges are reserved for sub-agent invocation with
a consumed report. `AGENTS.md` and its pristine template counterpart both gained the matching
delegation bullet, kept byte-identical outside the Template Stewardship section as
`AgentsFileMatchesPristine` requires. `README.md`'s agents-inventory paragraph was reworded from three
agents invoked by the process to two, with a sentence added noting `verify-change` is a compiled
Toolkit action run directly rather than through an agent. `docs/architecture/toolkit/contract-check.md`,
`.github/standards/system-contracts.md`, and `.github/standards/architecture-documentation.md` each had
their one `scope-check` mention reworded to name `verify-change` or its model-backed verifier in its
place — `system-contracts.md`'s change is substantive, not cosmetic: closing an unfulfilled obligation
was previously attributed to "that agent" (`scope-check`, an authoring agent), and is now attributed to
"the change's own responsibility," since `verify-change` only verifies and reports a finding, never
closes an obligation itself. `CONSTRAINTS.md`'s one bullet citing `scope-check.agent.md`'s report
template as a live example of evidence-before-verdict ordering was rewritten to cite
`dispatch.agent.md`'s own report template instead, since the cited file is deleted by this same stage.
`docs/architecture/process.md`'s diagram, Composition section, and Decisions section were updated to
remove the `ScopeCheck` node and its edge from `Helper`, following the `lint-fix`/`apply`/
`architecture-update` retirement precedent exactly, and a closing Decisions bullet records the
retirement itself. `.github/agents/scope-check.agent.md` was deleted.

One thing did **not** change, and is recorded here for the same reason S17 recorded its own reduction:
`docs/architecture/toolkit.md`'s historical measurement data, comparing `tier-check`'s and `scope-check`'s
observed pass/fail rates from before this migration began, was left untouched — it is a record of what
was measured at the time, not a live pointer to a still-existing agent, and rewriting history to erase
a retired name would falsify the record rather than clarify it. `CONSTRAINTS.md`'s S11-incident bullet
was left for the identical reason.

`PROCESS-03`/`NoOrphanedStandards` and `PROCESS-02`/`AgentReferencesResolve` both still pass:
`.github/standards/system-contracts.md`, `.github/standards/architecture-documentation.md`, and every
other standard `scope-check.agent.md` used to name are still named by at least one surviving agent, and
no surviving agent references the deleted file's path. `AGENTS.md` still matches its pristine template
counterpart exactly outside the exempt Template Stewardship section.

This was the last named retirement target this file tracked. No further prose agent is queued behind
it as of this stage; `helper` and `architecture-design` remain, absorbed last per this file's own
Destination section, because a conversation is the hardest control flow to encode.

### S17 — Compile a design-only path, giving `architecture-update` a retirable equivalent — landed

**Why now.** S16 named this as the next stage, not a permanent exception: `architecture-update.agent.md`'s
one remaining job — staging a contract clause ahead of implementation, in the `TODO.` placeholder form,
as a deliberate planned obligation — was framed as buildable from `DocumentAuthor` alone, the exact
primitive `ContractChangeWorker`/`StructuralChangeWorker` already use for their own documentation half.
Before implementing, an independent reviewer agent (background, separate context) was asked to vet the
plan against `MIGRATION.md`'s own destination and to search hard for whether this capability already
existed under a different name — the prior discipline of "check ground truth, not stale prose" this
Migration's own S16 entry records having briefly forgotten. Verdict: PROCEED WITH CHANGES — the primitive
was standalone and already unit-tested but never invoked outside the two atomic workers; no CLI action
or worker attempted this before; reuse the primitive and its standards list exactly; require `TODO.`
placeholders explicitly; convert a `DocumentAuthor` reroute into `Escalated` the same way `maintain`
does (no `Router` exists here to hand a reroute onward to); do not expect `route`'s three-worker catalog
to auto-select this action; and avoid the name `design`, which collides with the interview-only
`architecture-design` agent.

**What landed.** A new operation, `stage-contract` (`StageContractOperation`/`StageContractReport`),
runs a work item directly against `DocumentAuthor` alone — no `Router`, no `Developer`, no `Verifier` —
composed the same "Scope already fixed before this action is reached" way `maintain` composes
`SmallFixWorker`. Two mechanical, post-run checks enforce what `change-classification.md` and
`system-contracts.md` require, against `DocumentAuthor`'s reported output (no ledger of its real tool
calls exists yet, so its report is the only evidence available — the same evidence `maintain`'s own
equivalent check reasons from): every changed file must fall under `docs/architecture/` (the mirror
image of `ProtectedPathTripwire`'s rule for Maintenance — `TOOLKIT-33`), and a non-strict, repository-
wide `check-contracts` run must exit clean afterward (`TOOLKIT-34`) — non-strict because the unfulfilled
obligation this action deliberately produces is exactly what `-Strict` would otherwise promote to an
error. `Process.ContractCheckRunner` gained an optional `strict` parameter (defaulting to its existing
behavior) rather than a second implementation, so both callers still share one "run the repository's
configured contract check" seam. Documented in `docs/architecture/toolkit/stage-contract.md`
(`TOOLKIT-32`/`33`/`34`) and listed in `docs/architecture/toolkit.md`. Three boundary tests cover the
new clauses; the whole suite and `lint.ps1` pass.

A second independent-reviewer pass, run before live-trial validation per the user's standing
instruction, found five defects: `AGENTS.md` and its pristine template counterpart still routed
Maintenance and Migration to the deleted `apply` agent (a live self-hosting defect S16 had missed);
`process.md`'s Composition prose duplicated and drifted from the corrected Decisions account of why
`architecture-update`/`scope-check` keep their edges (a `PROCESS-I2` violation), and falsely claimed
`scope-check` has an edge to `Tree` the diagram never drew; `IsUnderArchitectureTree` used a brittle
string-prefix check a real model's own output (an absolute path, a `./` or `../` segment) could defeat;
and both the clause text and the operation's own messages overstated what the mechanical checks prove —
claiming a check against "the actual changed-file list" when only `DocumentAuthor`'s self-report exists,
and claiming a failure means "the staged clause is not well-formed" when the check runs repository-wide
and can fail on an unrelated pre-existing defect. All five were fixed and re-verified (356/356 tests,
84/84 clauses linked, lint exit 0) before proceeding.

**Live-trial validation landed.** `LiveTrialFixture` gained `RunStageContractAsync`, and
`LiveTrialStageContractTests` ran a real, in-process `stage-contract` invocation against a real model:
a two-document fixture repository whose prose already named a capability its `## Contract` section did
not yet promise, and a work item asking for exactly the clause `stage-contract` exists to stage. The
first run surfaced a mismatched *test* expectation, not an operation defect — the model correctly
wrote `TODO.ConfigurationInvalidityNamesFirstReason`, a real `TODO.`-prefixed placeholder, but the
grading oracle's stated expectation had implied the suffix itself should look unlike a real test name,
which is not what `system-contracts.md` requires. The expectation was corrected to state the actual
rule (the prefix is what matters, not the suffix), and the trial passed: the real model staged a
well-formed `TODO.` clause, touched only `docs/architecture/` and the Toolkit's own `.anneal/`
bookkeeping, and `stage-contract` reported completing rather than escalating or failing.

**Retirement landed.** A third independent-reviewer pass (background, separate context), asked to vet
wiring `stage-contract` into `dispatch`/`helper` and retiring `architecture-update.agent.md`, confirmed
staging is already within `dispatch`'s Change-mode remit per `change-classification.md`'s own Contract
Change section, confirmed `helper` must route rather than execute directly (`stage-contract` is
`OperationCategory.Authoring`, unlike the deterministic `lint-fix` precedent), and produced a 22-item
disposition list of every file mentioning `architecture-update` plus a 14-step ordered edit list. That
list was executed in full: `dispatch.agent.md` gained a caller-declared Step 2a (reached only when the
user explicitly asks to stage a clause, never inferred) that runs `dotnet anneal stage-contract` and
shares the same four-way exit-code contract as `route` and `maintain`; `helper.agent.md` gained one new
delegation row routing staging requests to `dispatch`; `change-classification.md`,
`architecture-documentation.md`, `.github/skills/check-contracts/SKILL.md`, `CONSTRAINTS.md` (and its
template counterpart), `.github/template/repository-map.md`, and `README.md` had every `architecture-
update` mention reworded to name `dispatch`, `stage-contract`, or `route`'s Structural Change worker, as
the disposition list required; `.github/agents/architecture-update.agent.md` was deleted; and
`docs/architecture/process.md`'s diagram, Composition section, and Decisions section were updated to
remove the `ArchUpdate` node and its edge, matching the `apply.agent.md` retirement's own precedent. The
same pass also caught and fixed two stale S16 leftovers the reviewer found while re-reading ground
truth rather than trusting this file's prose: `README.md` still named the already-deleted `apply` agent
in its inventory sentence, and `process.md`'s Decisions section still claimed `architecture-update` had
not yet been live-validated, which S17's own live-trial subsection above already contradicted.

One thing did not carry forward, and is recorded here rather than silently dropped:
`architecture-update.agent.md`'s Step 5 produced an explicit `Prune Results` table, naming every
section document it examined and its keep/delete verdict with reasoning. `StageContractReport` has no
equivalent structured field — it reports only the files `DocumentAuthor` changed and a summary string.
`stage-contract.md`'s own charter still instructs pruning judgement the same way the retired agent's
prompt did; only the itemized per-document report of that judgement did not survive the compiled
equivalent. This is an accepted reduction in reporting granularity, not an accidental one.

After this change, `WorstCaseInvocationWithinBudget` reads 19990 of 20000 tokens —
`dispatch.agent.md` grew substantially to hold the new Step 2a and had to be trimmed in the same pass
(consolidating three near-duplicate exit-code tables into one shared section) to stay inside budget,
following the exact precedent S15 and S16 both set for paying for growth out of `dispatch.agent.md`'s
own prose rather than any other file. `PROCESS-03`/`NoOrphanedStandards` and `PROCESS-02`/
`AgentReferencesResolve` both still pass, as the reviewer predicted: `scope-check.agent.md` and
`architecture-design.agent.md` still name every standard `architecture-update.agent.md` used to name,
and no surviving agent references the deleted file's path. `AGENTS.md` still matches its pristine
template counterpart exactly outside the exempt Template Stewardship section. 356/356 tests pass,
84/84 contract clauses link, and `lint.ps1` exits 0.

`scope-check`'s standalone-verify path (`Verifier` + `DeterministicCheck`, no preceding authoring
pass) remains the one stage after this — the same named future target this file has stated since S16,
now the last one outstanding.

### S16 — Retire `apply.agent.md` — landed

**Why now.** Every caller `apply.agent.md` used to have is gone: S11 moved `dispatch`'s Change-mode
edge to `route`; S15 moved its Maintenance-mode edge to `maintain`; this session's own pass fixed the
last two stragglers — `helper.agent.md`'s "a specific fix already reported" row (now `dispatch`) and
`dispatch.agent.md`'s own stale Migration-mode line (now points back at `dispatch`, since a stage's
implementation is ordinary Change/Maintenance work). Nothing left in this repository names `apply` as
the agent to call. `architecture-update.agent.md`'s two remaining `apply` mentions were reworded to
"the implementing pass, routed through `dispatch`" in the same pass, since `apply` is not that pass
for anything `dispatch` reaches.

**What does not retire alongside it, and why — this is not a loophole, it is two named future
stages.** `architecture-update.agent.md` and `scope-check.agent.md` keep a real, distinct job neither
`route` nor `maintain` covers today:

- **`architecture-update`** writes a contract clause *ahead of* implementation, as a deliberate planned
  obligation (the `TODO.` form `system-contracts.md` defines) — this Migration itself depends on that
  shape (`TOOLKIT-29/30/31` landed staged this way, implemented later). `route`'s Contract Change and
  Structural Change workers always compose `DocumentAuthor` and `Developer` (and `Verifier`) together
  in one atomic pass; there is no "write the promise, implement it later" mode today.
- **`scope-check`** verifies a diff that never went through `route`/`maintain` at all — a hand-written
  change, an externally contributed one, or anything predating this migration. `route`'s `Verifier`
  only ever judges what its own pass just authored.

Both gaps are buildable from primitives that already exist and are already proven inside
`ContractChangeWorker`/`StructuralChangeWorker` — `DocumentAuthor` alone (a design-only path) and
`Verifier` plus the existing `DeterministicCheck` pair (a standalone verify path) — not a new kind of
machinery. They are named as the next two stages after this one, not as permanent exceptions: the
destination this file states is that every prose agent is eventually absorbed, and these two are two
more items on that list, in the order their own compiled equivalent gets built.

**Step 1 — delete `.github/agents/apply.agent.md`.**

**Step 2 — update `docs/architecture/process.md`**, mirroring exactly how `lint-fix.agent.md`'s own
retirement was recorded (its Decisions entry, referenced above): remove the `Apply` node and its edges
from the Composition diagram, and add a Decisions entry stating the retirement and why, replacing the
stale "Maintenance, Migration, and any Change-mode invocation run through the prose path directly"
reasoning for `architecture-update`/`scope-check`'s remaining edges with the precise two-job reasoning
above (Maintenance is no longer one of those jobs — S15 closed it).

**Step 3 — confirm `PROCESS-03`'s tripwire does not trip**: no standard is named only by
`apply.agent.md` (`change-classification.md` is named directly by every other mechanical agent; the
product-code standards are reached only through `AGENTS.md`'s Standards Application matrix, never by
any single agent prompt, so removing one agent cannot orphan them).

**Exit conditions:** `apply.agent.md` deleted; `process.md`'s diagram and Decisions section describe
the resulting shape accurately, with no stale reasoning left; `pwsh ./build.ps1` and `pwsh ./lint.ps1`
both pass, including `PROCESS-03`'s `NoOrphanedStandards` check.

**Landed.** All three steps completed: `apply.agent.md` deleted; `process.md`'s diagram and Decisions
section rewritten (also fixing a pre-existing inconsistency — a `ScopeCheck` node with no edges despite
adjacent prose claiming `helper` still calls it); `change-classification.md`'s three "Agents:" lines
renamed from the old `architecture-update` → `apply` → `scope-check` chain to `dispatch`/`route`, with
`architecture-update`'s standalone staging use kept explicit; `route.md`'s one remaining present-tense
"`apply` play[s] a comparable role today" line corrected to past tense. `PROCESS-03`'s
`NoOrphanedStandards` passed on the first run — the predicted safety held. Trimming
`change-classification.md` was needed to keep `WorstCaseInvocationWithinBudget` under 20000 (peaked at
20028, landed at 19983). 353/355 tests, `lint.ps1` exit 0.

### S15 — Dispatch hands Maintenance-mode work to `maintain` — landed

**Why:** S14 closed both of its steps: the Massive-decomposition mechanism was proven live against a
real model (finding and fixing a real defect), and a compiled Maintenance path (`maintain`, backed by
`TOOLKIT-29/30/31`) landed and was itself live-validated. The one piece still standing between that
shape and retiring `apply.agent.md` was that `dispatch.agent.md`'s own Step 3 still handed
Maintenance-mode work to prose `apply` directly — exactly the shape S10 → S11 already closed once for
Change mode. This stage was that same rewiring, one mode later.

**Step 1 — rewired `dispatch.agent.md`'s Maintenance path.** Its Step 3 now runs
`dotnet anneal maintain "<work item>" <file-scope-hint>...` as a real shell command instead of calling
`apply` as a sub-agent, passing the declared bound's file scope through as the required hint list and
interpreting the exit code the same way Step 2 already interprets `route`'s. The Report Template's
`Work Performed` section now names `Maintain` in place of `Apply`. Landed in `fda7e1e`, with a small
prose trim to keep `WorstCaseInvocationWithinBudget` under its 20000-token ceiling (19980/20000 after).

**Step 2 — live-validated the rewritten agent.** The real `dispatch` custom agent (invoked through
this environment's own agent mechanism, not a nested CLI process) was given a genuine bounded
Maintenance request against a throwaway `Fixture.Lib`/`Fixture.Lib.Tests` fixture outside this
repository (its own local `dotnet-tools.json` installing this repository's current locally-built
Toolkit package, plus a `build.ps1`, mirroring S11's own fixture pattern): rename an unclear private
method (`DoTheThing`) to a name describing what it does, update its one call site, touch nothing else
— bound declared as `Fixture.Lib/Calculator.cs` only. `dispatch` classified Maintenance mode correctly,
ran `dotnet anneal maintain` as a real shell command, and reported `SUCCEEDED` on exit 0. Independent
verification by hand: `git status --short` and `git diff --stat` in the fixture (filtered to source,
ignoring `bin`/`obj` build-artifact churn from `maintain`'s own build/verify pass) showed exactly one
source file changed, `Fixture.Lib/Calculator.cs`, containing exactly the declared rename
(`DoTheThing` → `AddThenDouble`) and its one call-site update — `AddAndDouble`'s public signature and
XML doc comment untouched, matching the declared bound precisely. No defect was found; the rewiring
worked correctly on the first live trial.

**What this stage did not do.** It did not delete any prose agent file — `apply` keeps a live job this
stage did not remove (Migration-mode work, and any Change/Maintenance invocation run through the prose
path directly rather than through `dispatch`). Retiring `apply.agent.md`, `architecture-update.agent.md`,
and `scope-check.agent.md` is the next stage, now that every mode `dispatch` reaches has a validated
compiled equivalent.

**Exit conditions met in full:** `dispatch.agent.md`'s Maintenance path calls `maintain` instead of
`apply` — done; the rewritten agent was live-validated against a real fixture with no defect found —
done; `pwsh ./build.ps1` (353 passed, 2 correctly skipped) and `pwsh ./lint.ps1` (81/81 clauses, exit
0) both pass.

### S14 — Live trial of Massive-Effort decomposition, then a compiled Maintenance path — landed

Both steps landed and were independently live-validated this session.

**Step 1 (live Massive-decomposition trial):** a throwaway `BatchFlow` fixture (five-project .NET
solution) was handed a genuine cross-system work item real enough to force Massive Effort. Two defect
-finding pre-fix runs (empty hints, then populated hints) both found the same real bug: `Router.DecomposeAsync`
built its decomposition probe from generic route-facts context with no honest way for the model to
distinguish "no scope declared, containment vacuously clears" from "these hints are the authoritative
already-cleared boundary." **Fixed in `bf47c32`**: `Router` now composes a decomposition-specific
instruction stating both cases explicitly, locked in by two new `RouterTests`. Post-fix, both containment
branches ran correctly and escalated on a genuine cross-phase boundary crossing, and a dedicated
protected-path probe (adding `README.md` to the work item) independently confirmed the tripwire fires
before the cumulative check even runs — `process-steps.jsonl` showed `RouteOracle` → `Decomposition`
with no `CumulativeCheck` record. Logged in `1e05b56`.

**A reusable live-trial harness landed in parallel** (`11ca533`): `test/DemaConsulting.Anneal.Toolkit.Tests/LiveTrial/LiveTrialFixture.cs`
replaces this Migration's own repeated "build a throwaway fixture by hand, delete it after" pattern with
an in-repo, `InternalsVisibleTo`-backed harness — a real temp-folder git repository, an in-process
`AnnealTool.RunAsync` call against a real Copilot endpoint, and a model-backed grading oracle mirroring
`Router`'s own `Oracle<T>` shape — gated behind `ANNEAL_LIVE_TRIALS=1` and skipped by default so it never
runs in ordinary CI. Documented in a new "Live Trial Tests" section of `csharp-testing.md`.

**Step 2 (compiled Maintenance path)** landed as `MaintainOperation`/`MaintainReport`
(`e432a56`), implementing `TOOLKIT-29/30/31` exactly as `maintain.md`'s Decisions entry designed: no new
worker type (`SmallFixWorker` reused directly, no routing-oracle reclassification since Maintenance's
Scope is already fixed), with `ProtectedPathTripwire` and a strict-subset containment check enforced
mechanically, in that order, after the worker runs — overriding its outcome to Escalated if either trips,
regardless of what the worker itself reported. Validated live with the new `LiveTrialFixture` harness:
the first two live runs genuinely failed (the model misjudged a seeded file as nonexistent because it used
a content-search tool instead of reading the path directly), root-caused to an ambiguous charter, fixed by
rewording it, then re-verified passing twice.

**Exit conditions met in full:** both live trials ran against a real endpoint and were independently
verified; both real defects found were root-caused, fixed at the smallest correct scope, and re-verified;
`pwsh ./build.ps1` (353 passed, 2 correctly skipped live-trial tests, 355 total) and `pwsh ./lint.ps1`
(81/81 clauses, exit 0) both pass.

**Step 1 is done: the Massive-decomposition live trial ran live, found a real defect in the
decomposition boundary prompt, fixed it, and then re-ran both containment branches plus a protected-path
probe.** The throwaway fixture was `BatchFlow`, outside this repository, using the same harness pattern as
S12: a standalone console project referencing this repository's Toolkit project directly and calling
`AnnealTool.RunAsync(["route", workItem, ...], ..., [new RouteOperation(repositoryRoot)], ...)` against a
real Copilot endpoint. The fixture was a small multi-project `.NET` solution (`BatchFlow.Gateway`,
`BatchFlow.Inventory`, `BatchFlow.Pricing`, `BatchFlow.Fulfillment`, `BatchFlow.Notifications`) with a
real cross-solution work item: introduce an internal shared `BatchFlow.Context` helper library with
`WorkflowContext`/clocking, thread it through every system, rewire project references and solution wiring,
and replace the old smoke coverage with focused regression/propagation tests. That is an interior-only
change, but across enough systems and files that it cannot honestly execute as one unit.

*Pre-fix run 1 — empty hints.* The routing oracle selected a worker and classified the work as
`Massive`, but the decomposition pass itself refused: with no changed-file hints declared, the prompt
still told it every phase had to be a strict subset of an already-cleared scope while never stating what
that meant when no scope existed. Independently verified by hand: `process-steps.jsonl` recorded only
`RouteOracle` then `Decomposition`, and the captured decomposition transcript explicitly said it could not
honestly prove a strict subset against a missing boundary.

*Pre-fix run 2 — populated hints.* The same live work item was re-run with changed-file hints naming the
expected scope. This surfaced the second half of the same defect: the decomposition pass still refused,
now because the prompt presented those entries only as "changed-file hints," not as the authoritative
already-cleared boundary it was supposed to stay within. Again independently verified by direct record and
transcript inspection: `RouteOracle` then `Decomposition`, no cumulative check, no phase routing, and no
tracked-file edits in the fixture's own `git status --short` / `git diff --stat`.

**A real defect was found and fixed.** `Router.DecomposeAsync` always built the decomposition probe from
the generic route-facts context (`Changed-file hints: ...`) plus a one-size-fits-all ask string
("strict subset of the file scope already cleared"), which is enough for fake-endpoint tests that
hard-code the reply but not for a real model asked to reason about the boundary honestly. The prompt gave
the model no truthful way to distinguish the two live cases Anneal needed to prove: when no hints were
declared, that the strict-subset check is vacuously skipped; when hints were declared, that this exact
list is the authoritative already-cleared scope for decomposition. **Fix:** `Router` now composes a
decomposition-specific instruction/context pair that states those two cases explicitly, and two new
interior tests in `RouterTests` lock both prompt shapes in place. Commit `bf47c32`.

*Post-fix run 1 — empty hints, re-verified live.* The same work item now reached the intended path:
`RouteOracle` selected `structural-change` at `Massive`, `Decomposition` succeeded with four proposed
phases, and `CumulativeCheck` then ran and escalated because the combined Gateway/downstream threading
crossed a higher-scope boundary no single phase crossed alone. Independent verification was again by hand,
not from the tool's own summary: `process-steps.jsonl` showed `RouteOracle`, `Decomposition`, then
`CumulativeCheck`; the model transcript contained the four proposed phases; and fixture
`git status --short` / `git diff --stat` showed no tracked-file edits, consistent with an escalation
before any phase worker ran.

*Post-fix run 2 — populated hints, re-verified live.* Re-running with explicit changed-file hints now
exercised the populated containment branch correctly. `Decomposition` again succeeded, this time with
phase scopes named as strict subsets of the exact hint list (`src/BatchFlow.Context` + `BatchFlow.slnx`,
`src/BatchFlow.Gateway`, the four downstream `src/` directories, and `test/BatchFlow.Workflow.Tests`);
`CumulativeCheck` ran and again escalated on the combined boundary shift before any phase worker ran.
Independent verification was by direct transcript reading of the proposed phase scopes against the hint
list, plus fixture `git status --short` / `git diff --stat` confirming no tracked-file edits.

**A third, targeted protected-path probe confirmed the tripwire itself ran live, not just the cumulative
check.** An auxiliary re-run added `README.md` to the same work item and hint set. `Decomposition`
succeeded and proposed a dedicated README phase; `route` escalated immediately naming `README.md`; and
`process-steps.jsonl` stopped at `RouteOracle` and `Decomposition`, with no `CumulativeCheck` record at
all. That proves `ProtectedPathTripwire` fired before the cumulative-check oracle could run, exactly as
`TOOLKIT-27` requires.

The fixture and its harness were deleted afterward. `pwsh ./build.ps1` and `pwsh ./lint.ps1` were
re-run fresh once all of this stage's commits had landed (including the new in-repo live-trial harness
landed in parallel — see below): 350 passed / 1 correctly skipped / 351 total, 10/10 process-contract
cases, 81/81 clauses linked, lint exit 0.

### S13 — Closing the RouteReport gap S11 found, live and self-hosted — landed

**What this closes:** S11's own discovery log (below) named a genuine gap in `RouteReport`: on an
Escalated or Failed outcome, files a worker had already written to disk before stopping were
completely invisible to `route`'s caller, even though the working tree already held them. `RouteReport`
gains two new fields, `FilesChangedBeforeStopping` and `SummaryBeforeStopping` — never null, empty
when nothing was interrupted — threaded from a new `WorkerExecutionResult.Interrupted`
(`ChangeSetBeforeStopping`) through `Router` and `RouteFailureReport`. `WorkerRunResult` itself is
untouched, per its own doc comment reserving it for a worker that reached a typed answer; an
interrupted run never reaches one.

**This is this repository's first live, self-hosted `dotnet anneal route` run against its own
production source** — not a throwaway fixture outside the repository, as every prior trial in this
Migration used, but a real work item handed to `route` to modify Anneal's own Toolkit code while that
same Toolkit build ran it. Full context (root cause, work item, verification) was independently
re-established by hand before the run: `RepairLoop<TState>.RunAsync` returns `Failed` carrying the
*last real state*, not null, on budget exhaustion, so the gap was reachable through Failed as well as
Escalated; `ContractChangeWorker`/`StructuralChangeWorker` had several further discard points beyond
the one S11 found (budget-exhausted repairs, verifier-Refused, an unnamed-verdict fallback), all
confirmed by direct code reading before the work item was composed. An independent `general-purpose`
sub-agent (a different context, not blindly trusted) reviewed the proposed design first and surfaced a
real widening (the Failed-path gap) the original scoping had missed; every one of its claims was then
re-verified against source before acting on it, per this Migration's own discipline of never trusting
a sub-agent's or a worker's self-report.

**Outcome of the live run:** `route` reported `failed` (exit 0 — `Authoring` never gates a Failed
outcome, unrelated to this fix and confirmed pre-existing). The production code the compiled worker
produced — the new record, the widened return type threaded through all three workers, `Router`, and
`RouteOperation` — was correct on inspection, verified by a full line-by-line diff review of every
changed file, not merely a passing build. Two defects were found and fixed by hand, both in the
worker's own new test rather than in the implementation:

1. The new contract test under-sized its queued model replies, assuming a zero repair budget where
   `SmallFixWorker`'s actual default is one — `Developer.DevelopAsync` consumes two replies per
   authoring turn, so the second (repair) call ran out of queued replies and hit
   `ModelUnavailableException` instead of exercising the intended budget-exhaustion path. Fixed by
   queuing two more replies for the second round.
2. The test asserted the exit code would be non-zero on a Failed outcome, which `AnnealTool.cs`
   already deliberately treats as always-`ExitSuccess` for an `Authoring`-category operation — a
   pre-existing, unrelated behavior the worker's own test writer had not accounted for. Fixed to
   assert `ExitSuccess` and check the "route: failed" text instead.

**A further, independent defect was found during review, in the clause itself rather than the code.**
The worker's own `*Verified by:*` line named two test references on two separate markdown lines. That
is not a formatting nicety: `ArchitectureDocument.ReadVerifiers` deliberately stops collecting
verifiers at end-of-line (documented behavior, not a bug — it is what stops a later sentence's own
inline code from being misread as a promised test), so only the *first* line's verifier was ever
linked; the second — which also named a test that did not exist under that name — was silently
dropped, and `check-contracts -Strict` reported a clean pass regardless. The new promise had a real,
passing test, but no clause linked to it. Fixed by folding the two new contract tests into
one and naming it on a single line, matching every other clause in this tree (a survey during this fix
found no existing clause anywhere in `docs/architecture/` names more than one verifier).

**A second, deliberately deferred follow-up landed in the same session:** the self-collision hazard
raised before the live run — `build.ps1`'s Toolkit-refresh step evicts and reinstalls the exact NuGet
cache entry backing the local tool package that was, during the live run, running that very script —
did not manifest this time, but was not proven safe under adversarial timing either. `PowerShellScripts`
now sets `ANNEAL_TOOLKIT=1` in the environment of every script it runs (`TOOLKIT-24`, in
`docs/architecture/toolkit/lint-fix.md`), and this repository's own `build.ps1` checks for it and skips
its self-refresh step when present. A CLI-switch approach was considered and rejected:
`RunRepositoryScript`'s delegate signature has no argument-passing hook, so a switch would have meant
extending that whole seam for one repository's own defense; an environment variable needed no signature
change and works for any repository's own scripts, not only this one's.

**Exit conditions:** `RouteReport` carries the interrupted-change fields, correctly populated on both
Escalated and Failed across all three workers — **done**, verified by a dedicated boundary test
(`InterruptedRouteContractTests.RouteReportsFilesWrittenBeforeStopping`) and five new interior
tests covering each worker's discard points directly. `pwsh ./build.ps1` (284 C# tests, 9
process-contract, 43 check-contracts, 0 failed) and `pwsh ./lint.ps1` (73/73 clauses) both pass.

### S12 — Compiled workers gain baseline standards-loading (S11 step 2's new prerequisite) — landed

**Why this jumped the queue:** while designing S11 step 2 (flip `dispatch`'s Change-mode default to
`route`), a live conversation surfaced a real, previously unexamined gap: prose agents (`apply`,
`architecture-update`) explicitly read `.github/standards/*.md` before touching anything, via
`AGENTS.md`'s own "Standards Application" table — grepping `src/DemaConsulting.Anneal.Toolkit/Process/`
confirms **no compiled worker does this at all**. `WorkerBrief.ConstraintRefs` carries architecture
documents the router judged relevant, but its own doc comment says loading standards is left as "the
worker's own... job," and no worker does that job today. Flipping `dispatch`'s default to `route`
before closing this gap would mean every future Change-mode edit to this repository is authored with
zero awareness of its own coding, testing, and documentation standards — exactly the silent-drift risk
the whole process exists to prevent, and it would compound with every change once `route` is the
default path. S11 step 2 is therefore blocked on this stage landing and being validated, not merely
sequenced after it as a nice-to-have.

**Scope, deliberately minimal for this stage:** a static, per-worker, filesystem-read mechanism —
each worker reads its own small fixed list of standards from `.github/standards/` (mirroring
`AGENTS.md`'s existing table: `coding-principles.md` always; `csharp-language.md` for C# code;
`testing-principles.md`/`csharp-testing.md` for C# tests; `architecture-documentation.md`/
`system-contracts.md`/`technical-documentation.md` for doc changes) and injects the content verbatim
into the relevant `Developer`/`DocumentAuthor` prompt. No oracle call, no dynamic selection, no
embedded-resource question — those are a separate, larger, non-blocking follow-on (see below).

**Built by the reliable path, not the path being fixed:** this stage is designed by
`architecture-design` and implemented by `apply`/`architecture-update` (the prose agents), explicitly
*not* by routing the work through `route`/a compiled worker — a worker that does not yet consult
standards is the wrong tool to build "workers now consult standards." Prose agents remain the default
for all real repository development until this stage lands and is validated; nothing changes
operationally before then.

**Validation, not just landing:** after implementation, run one live trial that would visibly violate
a specific written standard if the worker ignored it (e.g., a naming or test-structure convention
stated in `coding-principles.md`/`csharp-testing.md` that a generic model would not otherwise reliably
follow), and confirm the fixed output honors it. This is the same "prove it, don't assume it" discipline
used for every prior stage's live trials.

**Deliberately deferred, not part of this stage:** the fuller design discussed alongside this one —
`Router`'s own routing-decision oracle call proposing an initial `RelevantStandards` list from the
work item's free text (closing the gap a purely static/glob mapping can't, e.g. "make sure we properly
test XYZ" implying both `testing-principles.md`/`csharp-testing.md` and potentially
`coding-principles.md`/`csharp-language.md`, decided before any file list exists), with workers
amending that list as they go (a static append at repair time keyed off which repair branch fired for
`SmallFixWorker`/`ContractChangeWorker`; `StructuralChangeWorker`'s own `Planner` call refining it
further once the concrete file/system list is known) — and the standalone-tool question of whether
standards should ship as toolkit assembly-embedded resources (a baseline that works with no installed
template at all) with a target repo's own `.github/standards/` as an optional override/extension
layer, versus purely filesystem-read as today. Both remain open, real, and valuable — but they are a
separate stage once this baseline is proven, not a prerequisite for it.

**Exit conditions:** every worker (`SmallFixWorker`, `ContractChangeWorker`, `StructuralChangeWorker`)
reads and injects its fixed standards list before its first `Developer`/`DocumentAuthor` call; the
validation trial is run and independently verified; `pwsh ./build.ps1` and `pwsh ./lint.ps1` pass.

**Landed as designed, with one adjustment made honestly rather than silently.** A new
`WorkerStandards.Render(repositoryRoot, params fileNames)` helper reads `.github/standards/{name}.md`
verbatim and wraps each in a `<standard name="...">` tag; a missing file is skipped, not thrown,
because a repository that has not installed a given standard (or has renamed one) must still get a
worker that runs — the same "best effort over an optional read" posture `RepositoryFacts` already
takes for `README.md` and `MIGRATION.md` itself. Each worker's own `Compose*Instruction` method now
calls this helper and folds the result into a `<standards>` block, so every repair call carries the
same content as the first call (`Compose*Instruction` is re-invoked on every repair, not cached).

**Per-worker split, decided from what each worker's own doc comment and existing tests already say it
does, not guessed:** `SmallFixWorker`'s own remit doc comment and its `DeterministicCheck` (which runs
`build.ps1`'s full test suite) confirm it authors both code and tests, and
`change-classification.md`'s own Small Fix entry names "test additions" explicitly — so it carries all
four: `coding-principles.md`, `csharp-language.md`, `testing-principles.md`, `csharp-testing.md`.
`ContractChangeWorker` and `StructuralChangeWorker` both already state, in their own constructor doc
comments, that `Developer` "implement[s] code and tests" — so both give `DocumentAuthor` the
documentation pair (`architecture-documentation.md`, `system-contracts.md`) and `Developer` the same
four-standard code/testing set `SmallFixWorker` carries. `StructuralChangeWorker`'s `Planner` call
additionally carries `change-classification.md` alone, since `Planner` is the one place this worker
decides scope/plan shape and the plan itself is what a re-plan revises.

**Ten new interior tests** (across `SmallFixWorkerTests`, `ContractChangeWorkerTests`,
`StructuralChangeWorkerTests`) prove the split by installing marker-content standard files under a
temporary repository root, running the worker against a `QueuedEndpoint` extended to capture every
`ChatTurnRequest` it was asked to complete (a new `Requests` property, additive and non-breaking to
every existing caller), and asserting each marker appears in the expected primitive's prompt and not
in the wrong one — plus one "no standards installed" test per worker confirming the worker still
completes normally rather than throwing. `pwsh ./build.ps1` passed 273 C# tests (+10 from this pass,
0 regressions); `pwsh ./lint.ps1` passed clean (only pre-existing planned-obligation warnings for
`installer.md`/`template.md`/`process.md`, unrelated to this stage); no contract clause was touched,
confirmed by `check-contracts` reporting the same 72 clauses/72 test links as before this change.

**Validation trial, run against a real model.** A throwaway fixture (`anneal-s12-trial`, outside this
repository, same harness pattern as every prior live trial: a standalone console project referencing
this repository's Toolkit project directly, calling `AnnealTool.RunAsync(["route", workItem],...,
[new RouteOperation(repositoryRoot)], repositoryRoot,...)` against a real Copilot endpoint) carried a
tiny C# `Calc` solution with one deliberate bug (its `Average` method divided by `values.Count + 1`
and did not handle an empty list) and its own installed `.github/standards/csharp-testing.md` stating
one deliberately unusual, non-default, hard-to-miss rule: every interior test method name in the
repository **must** start with the literal prefix `Regression_` — a convention no generic model would
produce unprompted, and distinct from the real `csharp-testing.md`'s own `{Subject}_{Method}_{Scenario}_
{Expected}` convention, so compliance could not be mistaken for a coincidence. The work item asked
`SmallFixWorker` (the simplest, cheapest path, reached via `route`) to fix the bug and add a
regression test for the empty-list case. **The worker completed successfully on the first attempt**:
the `Average` method was corrected (early-return 0 for an empty list, divide by `values.Count`
otherwise), and the new test was named `Regression_AverageOfEmptyList_ReturnsZero` — honoring the
fixture's own injected standard exactly, while the pre-existing test kept its original, differently-
patterned name untouched. A fresh-shell `pwsh ./build.ps1` in the fixture passed 2/2 tests. The
fixture and its harness were deleted afterward; `git status --short` in this repository was confirmed
clean before landing, and shows only this stage's own files.

**One honest caveat on the trial's own strength as evidence:** `Developer` is granted `Read` tools
over the repository, so a sufficiently agentic model could in principle have discovered
`csharp-testing.md` by reading it directly rather than from the injected `<standards>` block in its
prompt — this stage cannot fully isolate "complied because it was told" from "complied because it
looked it up itself" without disabling read tools, which would break the worker's normal operation.
Both paths are consistent with this stage's own goal (a worker that is aware of and honors repository
standards), so the trial still stands as positive evidence for the exit condition as written, and this
caveat is recorded rather than hidden.

**Deliberately not done here, per this stage's own declared scope:** the Router-seeded dynamic
`RelevantStandards` oracle call, the repair-time static append keyed off which repair branch fired
beyond the fixed per-primitive split above, and the embedded-assembly-resources/standalone-tool
question remain open, real, and valuable — a separate stage once this baseline is proven, not folded
in here.

### S11 — Dispatch hands Change-mode work to the Router — landed

S10 landed in full: Part A (commit `ed50468`) retired "Tier" everywhere in favor of Mode/Scope and
the toolkit's own worker names; Part B (commit `0b28c93`) wired `Router` into a real `route` CLI
action with a production catalog of all three workers. Two further live trials of `route` after Part B
found and fixed a real defect (commit `216368d`: an honest `NeedResearch` reply was being treated as a
hard failure) and confirmed correct Contract Change routing end to end. Full narrative in the S10
discovery-log entries below.

**What live evidence does not yet cover:** across every trial run so far — S9's direct worker calls
and S10's three `route` trials — the routing oracle has never once been asked a work item that should
select **Structural Change**, and no live trial has exercised a worker-initiated reroute or an
escalation path. `StructuralChangeWorker` itself was proven directly in S9; what is unproven is
whether the oracle *recognizes* work that needs it and routes there correctly.

**This stage, in order:**

1. **One live trial of `route` aimed at Structural Change** — a work item requiring
   coordinated changes across more than one document or system, the same shape S9's own smoke test
   used, given to `route` rather than to `StructuralChangeWorker` directly. Independently verify the
   outcome the same way every prior trial did: read the changed files by hand, re-run the fixture's
   own checks fresh, confirm `git diff`/`git status` show only the expected files. If this surfaces a
   defect, fix it following the same discipline as `216368d` — root-caused, smallest correct fix,
   re-verified, committed and pushed separately before continuing. **Landed**: commits `8b04351`
   (`StructuralChangeWorker`'s `Planner` step budget defect, found live and fixed) and `81259ed`
   (discovery-log entry).

2. **Was blocked on S12 landing and being validated; S12 landed and was validated (see its own entry
   above), unblocking this step.** `.github/agents/dispatch.agent.md` is rewritten: Step 1 (Classify)
   still determines Mode itself, but for Change mode no longer resolves Small Fix vs. Contract Change
   vs. Structural Change — that question is handed whole to `route`'s own routing oracle. A new Step 2
   runs `dotnet anneal route "<work item>" [<changed-file-hint>...]` as a real shell command and
   interprets its exit code (0 Succeeded, 4 Escalated, 1/3 Failed/Refused, 2 UsageError) to decide the
   report. Step 3 (Implement via `apply`) is now reached only for Maintenance mode; the old Step 2
   (Architecture Update) and Step 4 (Verify via `scope-check`, with its two-repair budget) are removed
   entirely from `dispatch`'s own flow, since `route`'s selected worker already owns authoring and
   verification internally. The Report Template is updated to match: `Scope` reads as whatever `route`
   reported rather than something `dispatch` decided, `Repairs Used` is dropped (there is no repair
   budget left for `dispatch` to spend), and `Residual` now distinguishes `escalated` from `gate`.
   **Landed**, `pwsh ./build.ps1`/`pwsh ./lint.ps1` both clean (273 C# tests, 72/72 clauses).

**What this stage does not do:** it does not delete `apply.agent.md`, `architecture-update.agent.md`,
or `scope-check.agent.md`. Each keeps a live job `route` does not cover: `apply` still does
Maintenance-mode work directly, `architecture-update`/`scope-check` still apply to Migration-mode
work and to any Change-mode invocation a caller runs through the prose path directly rather than
through `dispatch`. Deleting those files is a later stage, once every mode they still serve has its
own compiled equivalent or is deliberately decided to stay prose. It does not fold `architecture-design`
into `helper` — that remains a separate, later, named stage.

**Exit conditions:** the Structural Change live trial is run and independently verified (or its
defect fixed and re-verified) — **done**; `dispatch.agent.md` is updated and its Change-mode routing
table entry reads `route` rather than `architecture-update` → `apply` → `scope-check` — **done**;
`pwsh ./lint.ps1` passes — **done**.

**Step 1 is done: the Structural Change live trial ran, found and fixed a real defect, then confirmed
correct routing end to end.** A throwaway fixture (`OrderPipeline`, outside this repository, same
harness pattern as every prior trial: a standalone console project referencing this repository's
Toolkit project directly, calling `AnnealTool.RunAsync(["route", workItem],...)` against a real
Copilot endpoint) modeled a two-system `.NET` solution — `Ingest` parsing CSV rows with validation
logic wrongly bundled inline, and `Report` summarizing the parsed records — and was given a work item
asking to split validation out into a new, third `Validator` system with its own contract clause, with
`Ingest`'s own contract narrowed and `Report` updated to depend on `Validator` instead, and
`overview.md` updated to list three systems: the shape `change-classification.md` itself names as
Structural Change (new system boundary, coordinated cross-document changes), not defensible as a
Small Fix or as one file's Contract Change.

*Trial 1* — the oracle selected `contract-change` (**incorrect**: the work item explicitly asked for
a new system, not an edit to one existing document). `ContractChangeWorker` ran anyway and failed on
its own terms: its internal `DocumentAuthor` authored four documents (a new `validator.md` plus edits
to `ingest.md`, `report.md`, and `overview.md`), exceeding `ContractChangeWorker`'s own
`targetFileCountBudget` of 3 with no repair path, reporting `Failed`. This is the wrong worker
behaving as designed once the routing itself went wrong, not itself a defect.

*Trials 2 and 3* — the oracle correctly selected `structural-change` both times. Both times
`Planner.PlanAsync` produced a legitimate 10-step plan for the genuinely-scoped three-system split, and
both times `Planner`'s un-widened generic `maxPlanSteps` default of 8 refused it outright, with
`RunPlannerAsync` mapping the `Refused` decision straight to `Failed` — no re-plan-with-fewer-steps
path exists for a step-count overflow (only a completed plan's own `Verifier`-triggered
`StrategyRevisionRequired` spends the one re-plan budget).

**A real defect was found and fixed.** `StructuralChangeWorker` already exposes and deliberately
raises `documentAuthorTargetFileCountBudget` (to 8, from `DocumentAuthor`'s own default of 3 — S9's own
fix for the same class of problem) but had no equivalent override for `Planner`'s `maxPlanSteps`,
silently inheriting `Planner`'s generic default of 8 even though a genuinely-scoped structural change
naturally decomposes into more steps than a single-system change ever would. **Fix:**
`StructuralChangeWorker` now exposes its own `maxPlanSteps` parameter (default 12, mirroring
`documentAuthorTargetFileCountBudget`'s own reasoning: a deliberately round number comfortably above
the 10 steps observed live twice, without ceasing to act as a budget), threaded into the `Planner` it
constructs, guarded by the same `ArgumentOutOfRangeException.ThrowIfNegativeOrZero` pattern already
used for the file-count budget. Two new interior tests
(`RunAsync_PlanWithTenStepsUnderRaisedDefaultBudget_StillCompletes` and
`RunAsync_MaxPlanStepsOverride_StillFailsClosedWhenPlanExceedsIt`) lock in both halves: the raised
default admits the exact plan size found live, and the budget still fails closed when a smaller
override is exceeded. Fixed in `src/DemaConsulting.Anneal.Toolkit/Process/StructuralChangeWorker.cs`
and `test/DemaConsulting.Anneal.Toolkit.Tests/Process/StructuralChangeWorkerTests.cs`, verified by
`pwsh ./build.ps1` (267 C# tests, +2 from this pass) and `pwsh ./lint.ps1`. Commit `8b04351`.

*Trial 4, after the fix* — the oracle again correctly selected `structural-change`; `Planner` succeeded
on its first attempt (the same 10-step plan now within the raised 12-step budget); `DocumentAuthor`
authored all four documents; `Developer` implemented the split (new `Validator` system, `Ingest`
narrowed, `Report` updated to call it); one code-repair pass fixed a first deterministic-check failure,
after which both checks and `Verifier` passed, reporting `Succeeded`. Independent verification: every
changed file read by hand matched the tool's own summary; a fresh-shell `build.ps1` passed 7/7 tests; a
fresh-shell `check-contracts.ps1 -Strict` reported three clauses (`ING-01`, `REP-01`, `VAL-01`) all
passing; `git status --short` in the fixture (filtered to non-`bin`/`obj` paths) showed exactly the
expected files, matching the tool's report — with one minor exception: the tool's own completion
summary text mentioned a `requirements.yaml` file as changed, but no such file exists or was ever
touched. This is a self-report inaccuracy in the worker's own conversational summary, not a
control-flow defect — nothing in the architecture treats a worker's self-reported summary as ground
truth, which is exactly why every trial in this migration re-verifies by hand instead of trusting it.

Both the fixture and its harness were deleted afterward; `git status --short` in this repository was
confirmed clean before landing.

**Step 2 (updating `dispatch.agent.md`) is done** — see the stage description above for what changed.
A dedicated live trial then validated the rewritten agent end to end, not merely by reading the prompt
file: the real `dispatch` agent was invoked (via this environment's own custom-agent mechanism, not a
nested Copilot CLI process) against a throwaway fixture repository outside this one — a minimal
two-project .NET solution (`Fixture.Lib` + `Fixture.Lib.Tests`) with a genuine, deliberate bug
(`TextUtils.Capitalize` throwing on an empty string) and its own local `dotnet-tools.json` installing
the current build of this repository's Toolkit package. Given a plain-English bug report, `dispatch`
correctly determined Mode (Change) without pre-resolving scope, then ran a real
`dotnet anneal route "<work item>"` shell command from the fixture root.

*First attempt* — the fixture was missing a `build.ps1`, which `SmallFixWorker`'s deterministic check
hardcodes running. The check failed for a reason unrelated to the fix's correctness (no such script),
the one local repair pass composed an instruction back to `Developer` citing that failure, and
`Developer` — recognizing that satisfying it meant writing `build.ps1`, which is on `ProtectedPaths`'
list — correctly refused and escalated (`Developer.RefusedProtectedWrites.Count > 0` →
`OperationOutcome.Escalated`), exactly as designed. This was a fixture-setup gap, not a defect: every
target repository this worker runs against is assumed to have its own `build.ps1`.

**A separate, real finding surfaced while investigating that escalation.** `Developer`'s first
authoring pass, before the repair loop ran at all, had already written a correct fix to disk (verified
by hand: `git diff` after the escalated run showed `TextUtils.cs` correctly guarded against an empty
string, and a matching test addition). But `RouteReport.FilesChanged` — and therefore everything
`dispatch` had to report to the caller — is documented as "empty unless a worker completed the work,"
so the escalated report said nothing about it: a caller reading only the outcome would have no way to
know the working tree already held a real, correct candidate fix. This is a genuine gap in what the
toolkit's outcome contract communicates, not a control-flow defect: authoring writes to disk
immediately, independent of whatever verification or escalation decision follows it, and the current
`RouteReport` shape conflates "did the run succeed" with "is the working tree clean." Left as a named
open item for a future stage (see Open Concerns-style note below) rather than fixed here, since fixing
it well likely means `RouteReport` gaining a field for uncommitted changes left by an incomplete run —
a real design question, not a small patch, and this session's job was validating S11, not opening new
Toolkit surface that had not itself been reviewed.

*Second attempt, after adding a `build.ps1` to the fixture* — clean run, first try: oracle selected
`small-fix`, `Developer` authored the identical guard-clause fix, the deterministic check passed with
no repair needed, `dispatch` reported `SUCCEEDED`. Independent verification: `git status --short` and
`git diff --stat` in the fixture, read directly rather than trusted from the report, showed exactly one
file changed (`Fixture.Lib/TextUtils.cs`), matching `route`'s own summary exactly; a fresh `build.ps1`
run passed 2/2 tests. The fixture and its harness were deleted afterward.

**Exit conditions confirmed met in full**, including the one this entry adds: `dispatch` was proven,
live, to reach `route`, interpret a real exit code, and report accurately — not merely
inspected as a rewritten prompt file.

### S10 — Retiring "Tier", and wiring the Router into a real CLI action — landed

Two pieces of work, decided together in one conversation because neither blocks the other and both
were raised at once; they may land as separate commits, in either order.

**Part A — Mode/Scope vocabulary rename.** "Tier" is retired everywhere. The classification axis it
named is renamed **Scope** (paired with **Mode**, unchanged), and its three values are renamed to
the toolkit's own worker names, so the vocabulary humans read and the vocabulary the code already
runs are the same words:

- Tier 0 (Interior) → **Small Fix**
- Tier 1 (Contract) → **Contract Change**
- Tier 2 (Structural) → **Structural Change**

The prose agent `.github/agents/tier-check.agent.md` is renamed to `.github/agents/scope-check.agent.md`,
with every reference to it elsewhere (routing tables, worked examples, quality gates) updated to the
new name. `scope-check`'s job is unchanged — auditing that `dispatch`→`apply` did not silently
misclassify or narrow scope — but it is understood to be short-lived on its own terms: once Part B
lands and proves out, Change-mode work for this repository routes through `Router` and a compiled
worker instead of the prose pipeline, and a compiled worker's composition already *is* its
classification, the same reasoning that made `VerificationIntent.TierCheck` dead code this morning.
`scope-check` keeps auditing whatever prose-routed work still exists, and retires for good, per the
Migration's one-way invariant, once nothing routes through the prose pipeline any longer — this entry
does not retire it yet.

**Files touched** (rename one, update fifteen — this is a Migration-mode commit and may touch
anything the plan declares, so no `architecture-update`/`tier-check` hop is needed for it):

- Rename: `.github/agents/tier-check.agent.md` → `.github/agents/scope-check.agent.md`
- Update: `.github/standards/change-classification.md`, `.github/standards/architecture-documentation.md`,
  `.github/standards/system-contracts.md`, `.github/agents/apply.agent.md`,
  `.github/agents/architecture-update.agent.md`, `.github/agents/dispatch.agent.md`,
  `.github/agents/helper.agent.md`, `.github/skills/check-contracts/SKILL.md`,
  `docs/architecture/process.md`, `docs/architecture/toolkit.md`,
  `docs/architecture/toolkit/contract-check.md`, `AGENTS.md`, `.github/template/AGENTS.pristine.md`
  (must remain identical to `AGENTS.md` per its own rule), `MIGRATION.md`, `CONSTRAINTS.md`,
  `README.md`

**Part A exit conditions:** no file in the repository contains the word "tier" (case-insensitive,
excluding this discovery-log entry and S8/S9's own landed entries above, which are history and are
never rewritten); `AGENTS.md` and `.github/template/AGENTS.pristine.md` are byte-identical apart from
the one repository-specific section `AGENTS.md` is permitted to carry; `pwsh ./lint.ps1` passes.

**Part B — Router wired into a real CLI action.** Today `AnnealTool` dispatches only among the
standalone operations (`lint-fix`, `check-contracts`, `verify-evidence`, `probe-rule-owner`, `stats`);
nothing in the shipped tool ever constructs a `Router`. Every worker proven so far (Small Fix,
Contract Change, Structural Change) has only ever run inside a throwaway test harness. This part:

- Adds a new `AnnealTool` action that accepts a work item (and optional changed-file hints, mirroring
  `Router.RunAsync`'s own parameters) and runs it through a real `Router`.
- Assembles a production `WorkerCatalogEntry` list wiring in all three landed workers, replacing the
  single-entry catalog every prior worker's own tests used in isolation.
- The exact action name, its argument shape, and how repository configuration reaches `ModelRoles`
  are implementation judgment calls for `apply` to make and document, the same way file-count budgets
  and instruction phrasing were left to `apply` in S9 — there is no existing convention elsewhere in
  `AnnealTool` this must match beyond the pattern the other five actions already establish.

**Part B exit conditions:** the new action exists, is covered by interior tests against the fake
endpoint the same way every prior worker was, `pwsh ./build.ps1` and `pwsh ./lint.ps1` both pass, and
— the one exit condition that cannot be satisfied by interior tests alone — **the routing oracle
itself has been exercised at least once against a real model on a real work item**, with the result
independently verified the same way S8's and S9's live smoke tests were: not merely "the run
completed," but the classification and the resulting change re-checked by hand. This is the one thing
no prior stage has proven; every previous live trial called a worker directly and never exercised
`Router`'s own routing judgement.

**What this stage does not do:** it does not retire `dispatch`, `apply`, `architecture-update`, or
`scope-check`. Retiring any of them is a later stage, gated on Part B landing and being proven live —
not on `template-sync`, which remains deferred as a separate, occasional, cross-repository task
decoupled from this repository's own use of its toolkit. It does not fold `architecture-design` into
`helper`; that remains a named future stage, sequenced after Part B so that folding the two prose
agents together is a change in what `helper` can *do* (call a real `Router`), not merely a rename of
two prompts into one.

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

### S5 — Re-planning the migration — landed

**A green report is not a verified one.** An independent review by a *different* model caught a
contract test that had been widened beyond the clause it verifies — a defect a same-model second pass
had already passed. Different-model review is worth repeating periodically, not once.

**Sub-agent claims of "already covered" need spot-checking.** A porting agent's self-report conflated
two similarly-named fixtures and claimed one existing test covered both; it covered one.

**The self-hosting invariant immediately rejected its own author's first design.** A stage-less
`MIGRATION.md` was drafted before `apply.agent.md` and `change-classification.md` were found to read
a stage and an exit condition from this file. Removing stages would have broken Migration mode for
every prose agent still doing the work.

**The no-silent-loss invariant fired within the hour.** `lint-fix` was first scoped as having no
oracle, on the grounds that every branch is an exit code. That was wrong: the prose agent escalates
when the correct repair is a protected-file change, and a two-outcome compilation would have dropped
that behavior while appearing to succeed. The invariant is what surfaced it; the four-outcome shape
in S6 is the repair.

**S6 was deliberately not split, against the precedent of S1 and S4.** Both of those were split by
amendment after implementation found bundled risk, which argued for splitting the tool surface from
the process that uses it. The counter-argument won: a tool surface with no consumer cannot be known
to be right, and the deny-list in particular is only validated by a process hitting it.
Recorded here because it is a judgement call against precedent, and if S6 turns out to be too large
this entry is where the reason lives.

### S6 — The tool surface and the first compiled process — landed

**A different-model review caught three real defects a same-model pass had already waved through**, a
second instance of the S5 finding. All three were confirmed independently before being sent back:
an alternate-data-stream suffix on Windows (`fix.ps1::$DATA`) let a write reach a protected file's real
content because the deny-list matched text rather than refusing the syntax itself; `lint-fix` treated
any tool refusal as grounds to escalate, including a harmless outside-root read, rather than only a
refused protected write; and cancelling a run left `pwsh` still executing `fix.ps1`, free to keep
editing the repository after the caller had stopped waiting. The repair for the first closes the
alias in the containment primitive itself, `RepositoryPath.TryResolve`, rather than only in the
protected-path check, so every tool is covered at once rather than one call site remembering to check.

**Packaging the Copilot SDK's native runtime into the tool was tried and rejected within the same
session it was raised.** Declaring `RuntimeIdentifiers` on the Toolkit's project does make a packed
tool carry `copilot.exe` — proven by producing the RID-specific packages and installing one — but the
runtime itself is a full Node-based CLI download, well over 100MB compressed per platform, which
makes embedding it in a NuGet package a dead end before a second platform is even considered. The
`RuntimeIdentifiers` change was reverted.

**What replaced it works, and was proven working rather than assumed.** `CopilotEndpoint` now resolves
a system-installed `copilot` executable off `PATH` at construction and hands its path to
`RuntimeConnection.ForStdio`, and the Toolkit's build sets `CopilotSkipCliDownload=true` so it never
downloads or ships the runtime at all — the same dependency posture as requiring `git` on `PATH`
rather than vendoring it. `pwsh ./build.ps1` was re-run clean after the change, and `dotnet anneal
lint-fix`, invoked as the actually-packed and actually-installed tool (not `dotnet run`), was proven
end to end against a real MD013 violation in a throwaway file, which it repaired and reported clean
in one iteration. This closes the packaging gap the first S6 report had left open, and now literally
satisfies the stage's exit condition rather than only its `dotnet run`-equivalent form.

**A packed-tool ordering assumption that was never true came close to being hidden by rebuilding
around it.** `build.ps1` in stage S2 assumed `dotnet pack --no-build` was always safe because one
build output always fed one package. Chasing the runtime-identifier idea above (before it was
rejected) showed this is false the moment a tool declares more than one RID: each RID is its own
build, and only `dotnet pack` itself, without `--no-build`, knows how to produce them. The
`RuntimeIdentifiers` line was reverted along with the rest of that approach, so `build.ps1` was left
unchanged — but the fragility is recorded here because the next RID-carrying package this repository
ships will rediscover it if this entry is not read first.

### S7 — Measurement: reading the evidence already written — landed

**The corpus already existed; nothing had ever read it.** `TOOLKIT-08` has appended a structured
`InvocationRecord` for every `dotnet anneal` invocation since S2 — action, outcome, category, model
usage, duration — to `.anneal/records/invocations.jsonl`. Both the S5 and S6 completion reports named
the same 🔴 HIGH gap: the daily cadence's premise of steering by success rates had no rates to steer
by, only expectation. This stage added no new recording, only a reader: `stats`, a new deterministic
`Advisory` action that groups the existing corpus by action and reports a pass rate — `Succeeded ÷
(Succeeded + Failed + Refused + Escalated)`, `UsageError` excluded from both sides — across five
cumulative windows (today, last 3 days, last 7 days, last 30 days, all-time), with raw counts always
shown beside the percentage.

**Run against this repository's own working tree, it was immediately informative rather than
theoretical.** `check-contracts` sat at 91% (43/47) and `lint-fix` at 33% (2/6) across this session's
own use of the tool on itself — real, previously invisible numbers, not placeholders inserted to
prove the mechanism works. That is the exit condition met in substance as well as in form: a future "what's next"
conversation can now open with `dotnet anneal stats` instead of recalling the story from memory.

**A different-model review caught one real defect, a third instance of the same finding at S5 and
S6.** `RecordStore.Write` is not crash-atomic, so a process killed mid-append can leave a truncated
final line in `invocations.jsonl`. The first implementation deserialized each line with no exception
handling, so that one corrupt line would throw out of `ExecuteAsync` and crash the whole report
instead of answering from the records still readable. The fix catches the deserialization failure per
line, skips it, and continues — proven by a test that plants one well-formed record beside one
deliberately corrupt line and asserts the well-formed one is still reported correctly, not merely that
the operation avoids throwing.

**Clause.** `TOOLKIT-21` — `stats` reads a repository's invocation records and reports, for each
action found, its pass rate across the five cumulative windows above, with raw counts behind every
percentage. It is deterministic and consults no model.

`stats` gets its own section document under Toolkit, as every CLI-invocable operation does.

**Leaves working:** everything. No existing operation, record shape or file changes; `stats` only
reads what `TOOLKIT-08` already writes.

**Exit conditions met:** `dotnet anneal stats`, run against this repository's real recorded
invocations, printed correct per-action, per-window pass rates with counts, including windows with no
data reporting that rather than a misleading rate; `TOOLKIT-21` is verified by
`ToolkitContractTests.StatsReportsPerActionPassRatesAcrossWindows`, which exists and passes;
`pwsh ./build.ps1` (167 C# tests, 9 process-contract, 43 check-contracts self-tests, all passing) and
`pwsh ./lint.ps1` (70 clauses, 70 test links, exit 0) both pass.

### S8 — The primitive library and the first two compiled workers — landed

Three passes, one design. Pass 1 landed `DemaConsulting.Anneal.Toolkit.Primitives` — `Oracle<T>`,
`Research`, `DocumentAuthor`, `Developer`, `DeterministicCheck`, `Verifier`, `RepairLoop<T>`,
`StepResult<T>` — against no compiled caller yet, proven only by primitive-level interior tests. Pass 2
landed `DemaConsulting.Anneal.Toolkit.Process` — `Router`, `RoutingLedger`, `RouteDecision`,
`WorkerDescriptor`, `WorkerBrief`, `WorkerRunResult`, `RouteFailureReport` — and the first worker, Small
Fix, proving the Router's two independent budgets (research iterations, worker reroutes) fail
closed. Pass 3 landed the second worker, Contract Change: `DocumentAuthor` updates the affected system
contract document(s), `Developer` implements against the updated clauses, two `DeterministicCheck` steps
run `build.ps1` and a strict contract check, and a model-backed `Verifier` judges conformance against
that evidence before either completing or spending one of two independent one-shot repair budgets —
documentation first when a verdict names both.

**`RepairLoop<TState>`'s shape does not extend to an ownership-directed, two-owner repair.** The
primitive closes over one `execute` step and one counter, chosen once at construction; Contract Change
needs its repair step chosen dynamically, per verification pass, between `DocumentAuthor` and
`Developer` depending on which of four verdicts (`DocumentationRepairRequired`, `CodeRepairRequired`,
`BothRepairsRequired`, or neither) `Verifier` just reached. Composing two separate `RepairLoop`
instances was considered and rejected — it would have required stacking a second instance of
`SmallFixWorker`'s already-flagged `OperationOutcome.Refused`-as-sentinel trick to bridge them, compounding
a wrinkle rather than resolving it. Pass 3 instead reproduces `RepairLoop`'s exact contract by hand: a
repair spends only the budget its finding names, an escalation or insufficient-evidence verdict stops
immediately without spending either, and a budget spent with the same finding still open reports
`Failed` rather than looping or rerouting. `RepairLoop<TState>` remains correct and in place for Small
Fix's single-owner case; it was not widened, retrofitted, or deprecated to accommodate the case it does
not fit.

**A documentation-only fix and the code it implies are the same repair, not two.** No primitive reports
whether a documentation repair changed an obligation the code must now satisfy, so pass 3 chose to
always re-run `Developer` once after a documentation repair, charged against neither the documentation
nor the code budget — it is necessitated by the doc repair, not an independent finding. The cost is one
extra `Developer` pass on every documentation repair, including ones that turn out to be purely
editorial; that was judged the safer default over silently leaving code out of sync with a contract
clause that just changed.

**A metadata-only parameter from pass 1 surfaced only when a second script needed an argument.**
`DeterministicCheck`'s `selector` parameter reads, in its own XML doc, as passed through to the script it
runs; it is not — it is recorded only as evidence metadata. Pass 3 needed to run
`check-contracts.ps1 -Strict` (an actual switch argument), found no existing seam for it, and added a
strictly additive `PowerShellScripts.RunAsync(script, arguments, cancellationToken)` overload rather than
changing `DeterministicCheck`'s established behavior. The `selector` inconsistency itself is left as a
flagged defect for a later pass, not fixed in passing.

**Clauses.** No contract clause changed across any of the three passes — the router-and-primitives shape
was designed and implemented entirely as internal composition with no CLI/`IOperation` surface, per the
stage's own "Not yet decided by this entry" note about whether the routed front door needs one. That
remains an open question for whichever stage wires a `dotnet anneal` action to the Router, not this one.

**Leaves working:** every existing prose agent (`dispatch`, `apply`, `tier-check`, `architecture-update`,
`template-sync`) keeps running unchanged; Structural Change and Template Sync — the two remaining
compiled workers — and wiring the Router to a CLI surface are explicitly deferred to later stages, as
the stage's own scope line always said.

**Exit conditions met:** all three of S8's planned parts — the primitive library, the Router with Small
Fix, and Contract Change — are landed and covered by interior tests; `pwsh ./build.ps1` passes clean
across all three passes' cumulative test count (241 C# tests, 9 process-contract, 43 check-contracts
self-tests, all passing) with no contract clause touched and none broken.

### S9 — The Structural Change worker — landed

Built and landed in one pass rather than three, since S8 had already proven every primitive this stage
composes; only `StructuralChangeWorker` itself and one new `VerificationVerdict` case were new.
`Planner.PlanAsync(...)` runs once, producing an `ImplementationPlan` whose steps compose the
instruction text for `DocumentAuthor.AuthorAsync(...)` — constructed with `targetFileCountBudget: 8`,
a deliberately round number chosen to comfortably cover `overview.md` plus several system and section
documents without ceasing to act as a budget — and then `Developer.DevelopAsync(...)`, followed by the
same two `DeterministicCheck`s Contract Change already runs and a `Verifier.VerifyAsync(...)` pass. A
new `VerificationVerdict.StrategyRevisionRequired` case names a different failure than a repair: the
plan's decomposition itself was wrong, not its execution. On this verdict the worker spends its one
re-plan budget — a second, final `Planner.PlanAsync(...)` call, fed the verifier's own finding — and
restarts `DocumentAuthor` → `Developer` → checks → `Verifier` once; a second `StrategyRevisionRequired`
or an exhausted documentation/code repair budget reports `Failed`. All three budgets (re-plan,
documentation repair, code repair) are independent and none resets another, exactly as designed.

**The design's "outside any repair loop" framing needed correcting once the code existed to check it
against.** The doc-update pass that followed implementation re-read `StructuralChangeWorker.cs` itself
rather than trusting the design doc or the implementation report, and found the re-plan path does
re-enter `DocumentAuthor` → `Developer` → checks → `Verifier` the same way a documentation or code
repair does — it is not, in fact, outside the loop. What stays independent is the *budget*, not the
control flow: the re-plan budget is counted, spent and exhausted on its own, never borrowing headroom
from or lending it to the documentation/code repair budgets. `process.md`'s Decisions section was
revised to say this precisely, rather than leaving the original, now-inaccurate sentence standing
next to code that contradicts it.

**A live smoke test proved the ordinary path and could not induce the re-plan path — and found out
why.** A throwaway harness (the same pattern S8 established: a standalone project exploiting
`InternalsVisibleTo`, driving the real Copilot SDK against a scratch fixture outside this repository)
ran `StructuralChangeWorker` once against a cross-system fixture requiring coordinated edits
across three documents and two source files. `Planner`, `DocumentAuthor`, `Developer`, both
`DeterministicCheck`s and `Verifier` all fired in the right order; the file changes were real and
independently re-verified by re-running the fixture's own check scripts from a fresh shell after the
worker finished. No repair or re-plan was spent, correctly, since the task was designed not to need
one. A second, harder attempt tried twice, across two different fixtures, to induce a genuine
`StrategyRevisionRequired` finding by hiding a cross-system invariant a first plan would plausibly
miss — and could not. The reason is architectural, not a failure of effort: `Planner` alone has no tool
access and judges from brief text only, but `DocumentAuthor` and `Developer` do have full repository
tools and their own instructions are already open-ended, so a capable model routinely discovers and
self-corrects exactly this kind of cross-system miss before any deterministic check or `Verifier` pass
ever runs. `StrategyRevisionRequired` is therefore only reachable when every deterministic check
already passes and `Verifier`, reading evidence text with no tool access of its own, judges the plan's
decomposition wrong regardless — a narrower window than either the design or the interior tests alone
would suggest. The re-plan control flow remains proven correct by interior tests against a scripted
endpoint (exact call counts, budget isolation, no-third-attempt exhaustion); it is not yet proven to
fire against a real model's own judgement, and this entry records why that is a harder, rarer condition
to reach than a first reading of the design would assume, not an open defect.

**Leaves working:** every existing prose agent, unchanged. (This entry originally claimed no prose
agent could retire until Template Sync existed, citing the one-way invariant as authority — the
invariant says nothing about sequencing unrelated work, and S10 Part B, landed after this entry, said
so explicitly. Corrected here rather than left standing as an unchecked decree.)

**Exit conditions met:** `StructuralChangeWorker` is landed, covered by 9 interior tests spanning the
happy path, both `PlanningDecision` non-`Plan` cases, single and exhausted documentation repair, single
and exhausted re-plan, verifier reroute/escalation, and the two failure paths; `pwsh ./build.ps1` (255
C# tests, 9 process-contract, 43 check-contracts self-tests, all passing) and `pwsh ./lint.ps1` both
pass with no contract clause touched or broken; a live smoke test confirmed the ordinary path
end-to-end against a real model with independent verification, and the re-plan path's non-firing
across two deliberate attempts is recorded above as a characterized, understood boundary rather than
an unexamined gap.

### S10 Part B — Router wired into a real CLI action — landed

Added `RouteOperation` (`src/DemaConsulting.Anneal.Toolkit/Operations/RouteOperation.cs`), the first
`IOperation` that ever constructs a real `Process.Router` outside a throwaway test harness, and
registered it in `AnnealTool.DefaultOperations` as the sixth shipped action. It assembles a production
`WorkerCatalogEntry[]` wiring all three landed workers under the exact keys their own interior tests
already use (`small-fix`, `contract-change`, `structural-change`), and projects the internal
`RouterOutcome`/`RouteFailureReport` types into a new public `RouteReport` finding, the same
"populated half tells you which path was taken" shape `LintFixReport` already established.

**The action name, argument shape, and charters were this pass's own judgement call, as the stage's
own text anticipated.** `route` was chosen over `develop`/`work` as reading most plainly as "hand this
repository a real piece of work and let the routing oracle decide." The work item is a single
positional argument, changed-file hints follow it positionally, mirroring every other action's own
style (`probe-rule-owner <rule>`). Every charter (route oracle, research, planner, document author,
developer, verifier) was authored fresh rather than lifted from a prose agent, since Router and its
three workers have never had a prose predecessor — unlike `lint-fix`, which duplicated
`lint-fix.agent.md`'s own guidance. `RequiredRole => ModelRole.Heavy` and
`Category => OperationCategory.Authoring` follow `LintFixOperation`'s own reasoning: the most demanding
role and the most permissive category any path through the action can reach, declared unconditionally
regardless of which path a given run takes.

Added contract clause `TOOLKIT-23` to `docs/architecture/toolkit/route.md` (a new section document,
mirroring `stats.md`'s shape) naming `ToolkitContractTests.RouteRunsTheSelectedCompiledWorker`, and
extended `ToolkitContractTests`' `TOOLKIT-01` action-list assertion to include `"route"`. Eight new
interior tests (`RouteOperationTests.cs`) cover a route to each of the three workers completing, the
oracle naming no route (`Failed`), the oracle naming a human-only next step (`Escalated`), and the two
usage-error argument shapes, all driven through the same single-`QueuedEndpoint`, strict-call-order
fake-endpoint pattern every prior worker's own tests already use.

**A live smoke test exercised the routing oracle itself against a real model, not merely a worker.** A
throwaway harness (a standalone console project outside this repository, referencing this repository's
Toolkit project directly and calling the public `AnnealTool.RunAsync(["route", workItem],...)`
surface with no substituted endpoint) drove a real Copilot endpoint against a real scratch fixture: a
tiny two-project .NET solution with a deliberate, genuine off-by-one bug in `Calculator.Average`
(divides by `numbers.Length - 1` instead of `numbers.Length`) and one xUnit test that failed against
it, confirmed failing by an independent `build.ps1` run before the harness touched anything. The
harness gave `route` one plain-English work item naming the failing test and the wrong method. The
routing oracle selected `small-fix` and the action completed on its first pass, reporting exactly one
changed file, `src/Calc/Calculator.cs`. Independent verification: reading the file by hand confirmed
the exact one-line divisor fix (`numbers.Length` in place of `numbers.Length - 1`); re-running the
fixture's own `build.ps1` from a fresh shell after the harness exited showed the previously-failing
test now passing; `git status`/`git diff --stat` inside the fixture showed only `Calculator.cs` (plus
build output) touched, matching the tool's own report exactly. Both the fixture and the harness project
were deleted afterward, and `git status --short` in this repository was confirmed clean of any harness
artifact before landing.

**Exit conditions met:** the new `route` action exists, is covered by eight interior tests against the
fake endpoint the same way every prior worker was; `pwsh ./build.ps1` (263 C# tests, +8 from this pass),
`pwsh ./lint.ps1`, and a strict `check-contracts` run all pass, with `TOOLKIT-23` confirmed naming a
real, passing boundary test; and the routing oracle itself was exercised at least once against a real
model on a real work item, with both the classification and the resulting file change independently
re-verified by hand — the one exit condition S9's own entry noted no prior stage had proven.

**What this stage does not do:** `dispatch`, `apply`, `architecture-update`, and `scope-check` are not
retired by this pass — that remains a later stage, as S10's own text said it would.

**Two further live trials, run after this stage first landed, built more confidence in the oracle's
own judgement and caught a real defect.** One clean easy case (above) does not prove the oracle handles
harder calls; two more real work items were routed against the real Copilot SDK, using the same
harness pattern, each in its own throwaway scratch fixture repository outside this one.

*Trial 1 — Contract Change.* A tiny `WidgetApi` fixture with one contract clause (`WIDGET-01`,
`WidgetService.GetWidget` returning `Id`/`Name`) was handed a work item asking for a new optional
`Description` field on the response, documented as part of the contract — the classification
`change-classification.md`'s own worked-examples table names directly ("Add an optional field to an
API response … New consumer-observable promise"). The oracle's **first** ask returned `NeedResearch`
with `HasSufficientEvidence: false` ("I need a narrow look-around to find the contract document,
response type, and existing test/requirement linkage before routing it"); `Router.RunAsync` then
failed the run closed immediately instead of spending its research budget — see the defect below. On a
second, independent invocation the oracle answered directly: `SelectWorker → contract-change`,
reasoning "this work changes the Widget API contract by adding a new response field and explicitly
documenting it." `ContractChangeWorker` updated `WIDGET-01`'s prose to describe the new optional field,
added `WidgetResponse.Description`, wired it through `WidgetService`, and added a second contract test
— all cited correctly in the updated clause. Independent verification: reading every changed file by
hand matched the tool's own summary; a fresh-shell `build.ps1` passed 2/2 tests; a fresh-shell
`check-contracts.ps1 -Strict` reported "1 clauses, 1 test links checked" clean; `git diff --stat` in
the fixture showed exactly the four files the tool reported, nothing else. Correct on every count.

*Trial 2 — a ambiguous case.* An `AgeParser` fixture's one contract clause (`AGE-01`) promised
only that `ParseAge` parses text to an integer, throwing on non-numeric input — silent on the sign of
the result. The work item asked to make `ParseAge` reject negative input, matching the shape of
`change-classification.md`'s own "Tighten input validation … Narrows a clause; breaking" worked
example, but arguable the other way since the clause never promised anything about negative
numbers to begin with. The oracle answered `SelectWorker → small-fix` in one pass, reasoning "this is a
localized code fix and test addition inside an existing component, with no contract-document or
architecture change implied." `SmallFixWorker` added the `ArgumentException` check and one new test,
touching no documentation. Independent verification: the two changed files matched the tool's report
exactly; a fresh-shell `build.ps1` passed 2/2; `check-contracts.ps1 -Strict` still reported the one
clause and its (unchanged) test cleanly. **Assessment:** defensible, not the only possible answer — the
contract's own silence on sign supports reading this as filling a gap rather than narrowing a promise,
which is different from `change-classification.md`'s own worked example (there, the clause it narrows
is a stated one). The oracle's stated reasoning does not show it weighed the countervailing read at
all — it asserts "no contract-document change implied" without engaging with why tightening validation
might narrow an implicit promise, so an honest reading is that it picked a defensible path without
visibly recognizing the two-sided nature of the call, not that it consciously chose the higher scope
per `change-classification.md`'s own "when uncertain, choose the higher one" guidance.

**A real defect was found and fixed from Trial 1's first invocation.** `Router.RunAsync` treated
`OperationOutcome.Refused` from the route oracle identically to `OperationOutcome.Failed` — an
immediate hard stop discarding the decoded envelope — before ever checking whether the decision was
`RouteDecisionKind.NeedResearch`. But `Oracle<TDecision>.AskAsync` maps `HasSufficientEvidence: false`
to `Refused` unconditionally, and for `NeedResearch` specifically, `HasSufficientEvidence: false` is
not a failure to answer — it is `RouteCharter`'s own instructed, honest signal ("ask for a bounded,
narrow look-around... do not guess"). The `RouteDecision.NeedResearch` case already existed in
`Router.RunAsync`'s own switch statement, spending the research budget correctly — it was
unreachable on this path, because the fake-endpoint interior test for it
(`RunAsync_NeedsResearchThenSelectsWorker_RunsResearchAndSucceeds`) hardcoded
`"hasSufficientEvidence":true` in its own `NeedResearchJson` fixture helper, masking the exact
condition a real model produces. **Fix:** `Router.RunAsync` now stops immediately only on a true
`Failed` outcome (no envelope was ever decoded); a `Refused` outcome still decodes its envelope and is
switched on exactly as `Succeeded` is, so `NeedResearch` spends its research budget as designed
whichever outcome carried it, while `SelectWorker` reached with `Refused` still fails closed (a
worker name paired with "I don't have enough evidence to commit to this" is a contradictory
reply, unlike `NeedResearch`, where the same flag is expected). Two new interior tests
(`RunAsync_NeedsResearchReportsInsufficientEvidence_StillRunsResearchAndSucceeds` and
`RunAsync_SelectWorkerReportsInsufficientEvidence_FailsClosedWithoutRunningTheWorker`) lock in both
halves of the corrected behavior; all pre-existing `RouterTests` still pass. Fixed in
`src/DemaConsulting.Anneal.Toolkit/Process/Router.cs` and
`test/DemaConsulting.Anneal.Toolkit.Tests/Process/RouterTests.cs`, verified by `pwsh ./build.ps1` (265
C# tests, +2 from this pass) and `pwsh ./lint.ps1`.
