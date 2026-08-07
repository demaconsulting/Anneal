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

### S11 — Dispatch hands Change-mode work to the Router

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

1. **One live trial of `route` aimed at Structural Change** — a work item genuinely requiring
   coordinated changes across more than one document or system, the same shape S9's own smoke test
   used, given to `route` rather than to `StructuralChangeWorker` directly. Independently verify the
   outcome the same way every prior trial did: read the changed files by hand, re-run the fixture's
   own checks fresh, confirm `git diff`/`git status` show only the expected files. If this surfaces a
   defect, fix it following the same discipline as `216368d` — root-caused, smallest correct fix,
   re-verified, committed and pushed separately before continuing.

2. **Once that trial confirms correct routing (or a found defect is fixed and re-verified), update
   `.github/agents/dispatch.agent.md`:** for Change-mode work specifically, `dispatch` hands off to the
   `route` action instead of sequencing `architecture-update` → `apply` → `scope-check`. `dispatch`
   keeps its other jobs unchanged — Intake (appending to `BACKLOG.md`/`CONSTRAINTS.md`/README
   assumptions) and handing off Maintenance and Migration work — because `route`'s catalog covers none
   of those, and `Router`'s worker catalog is Change-mode only, not a general dispatcher replacement.

**What this stage does not do:** it does not delete `apply.agent.md`, `architecture-update.agent.md`,
or `scope-check.agent.md`. Each keeps a live job `route` does not cover: `apply` still does
Maintenance-mode work directly, `architecture-update`/`scope-check` still apply to Migration-mode
work and to any Change-mode invocation a caller runs through the prose path directly rather than
through `dispatch`. Deleting those files is a later stage, once every mode they still serve has its
own compiled equivalent or is deliberately decided to stay prose. It does not fold `architecture-design`
into `helper` — that remains a separate, later, named stage.

**Exit conditions:** the Structural Change live trial is run and independently verified (or its
defect fixed and re-verified); `dispatch.agent.md` is updated and its Change-mode routing table entry
reads `route` rather than `architecture-update` → `apply` → `scope-check`; `pwsh ./lint.ps1` passes.

**Step 1 is done: the Structural Change live trial ran, found and fixed a real defect, then confirmed
correct routing end to end.** A throwaway fixture (`OrderPipeline`, outside this repository, same
harness pattern as every prior trial: a standalone console project referencing this repository's
Toolkit project directly, calling `AnnealTool.RunAsync(["route", workItem], ...)` against a real
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

Step 2 (updating `dispatch.agent.md`) remains outstanding and is intentionally out of scope for this
step — a human decides separately whether to proceed to it.

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
to be right, and the deny-list in particular is only validated by a process actually hitting it.
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
Fix, proving the Router's two independent budgets (research iterations, worker reroutes) actually fail
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
ran `StructuralChangeWorker` once against a genuinely cross-system fixture requiring coordinated edits
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

**Leaves working:** every existing prose agent; Template Sync is now the only remaining deferred
worker, and no prose agent retires until it exists and is proven, per the migration's one-way
invariant.

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
regardless of which path a given run actually takes.

Added contract clause `TOOLKIT-23` to `docs/architecture/toolkit/route.md` (a new section document,
mirroring `stats.md`'s shape) naming `ToolkitContractTests.RouteRunsTheSelectedCompiledWorker`, and
extended `ToolkitContractTests`' `TOOLKIT-01` action-list assertion to include `"route"`. Eight new
interior tests (`RouteOperationTests.cs`) cover a route to each of the three workers completing, the
oracle naming no route (`Failed`), the oracle naming a human-only next step (`Escalated`), and the two
usage-error argument shapes, all driven through the same single-`QueuedEndpoint`, strict-call-order
fake-endpoint pattern every prior worker's own tests already use.

**A live smoke test exercised the routing oracle itself against a real model, not merely a worker.** A
throwaway harness (a standalone console project outside this repository, referencing this repository's
Toolkit project directly and calling the public `AnnealTool.RunAsync(["route", workItem], ...)`
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

*Trial 2 — a genuinely ambiguous case.* An `AgeParser` fixture's one contract clause (`AGE-01`) promised
only that `ParseAge` parses text to an integer, throwing on non-numeric input — silent on the sign of
the result. The work item asked to make `ParseAge` reject negative input, matching the shape of
`change-classification.md`'s own "Tighten input validation … Narrows a clause; breaking" worked
example, but genuinely arguable the other way since the clause never promised anything about negative
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
per `change-classification.md`'s own "when genuinely uncertain, choose the higher one" guidance.

**A real defect was found and fixed from Trial 1's first invocation.** `Router.RunAsync` treated
`OperationOutcome.Refused` from the route oracle identically to `OperationOutcome.Failed` — an
immediate hard stop discarding the decoded envelope — before ever checking whether the decision was
`RouteDecisionKind.NeedResearch`. But `Oracle<TDecision>.AskAsync` maps `HasSufficientEvidence: false`
to `Refused` unconditionally, and for `NeedResearch` specifically, `HasSufficientEvidence: false` is
not a failure to answer — it is `RouteCharter`'s own instructed, honest signal ("ask for a bounded,
narrow look-around ... do not guess"). The `RouteDecision.NeedResearch` case already existed in
`Router.RunAsync`'s own switch statement, spending the research budget correctly — it was simply
unreachable on this path, because the fake-endpoint interior test for it
(`RunAsync_NeedsResearchThenSelectsWorker_RunsResearchAndSucceeds`) hardcoded
`"hasSufficientEvidence":true` in its own `NeedResearchJson` fixture helper, masking the exact
condition a real model produces. **Fix:** `Router.RunAsync` now stops immediately only on a true
`Failed` outcome (no envelope was ever decoded); a `Refused` outcome still decodes its envelope and is
switched on exactly as `Succeeded` is, so `NeedResearch` spends its research budget as designed
whichever outcome carried it, while `SelectWorker` reached with `Refused` still fails closed (a
worker name paired with "I don't have enough evidence to commit to this" is a genuinely contradictory
reply, unlike `NeedResearch`, where the same flag is expected). Two new interior tests
(`RunAsync_NeedsResearchReportsInsufficientEvidence_StillRunsResearchAndSucceeds` and
`RunAsync_SelectWorkerReportsInsufficientEvidence_FailsClosedWithoutRunningTheWorker`) lock in both
halves of the corrected behavior; all pre-existing `RouterTests` still pass. Fixed in
`src/DemaConsulting.Anneal.Toolkit/Process/Router.cs` and
`test/DemaConsulting.Anneal.Toolkit.Tests/Process/RouterTests.cs`, verified by `pwsh ./build.ps1` (265
C# tests, +2 from this pass) and `pwsh ./lint.ps1`.
