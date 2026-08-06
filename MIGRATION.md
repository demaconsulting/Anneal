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

### S6 — The tool surface and the first compiled process

The first prose agent becomes a compiled process. `lint-fix` is the pathfinder, chosen because its
success is decided by an exit code rather than by judgement: `pwsh ./lint.ps1` returns 0 or it does
not. Every other agent's success is a verdict, and a pathfinder whose success cannot be checked
mechanically proves nothing — a failure could be the machinery or the judgement, with no way to tell
which.

The state flow is deterministic apart from the work itself:

```text
fix.ps1                                    the process runs it; not a tool the model may call
loop, bounded:
    lint.ps1  →  exit 0                 →  SUCCEEDED
              →  repairable in scope    →  worker edits files, loop
              →  repair needs the user  →  INCOMPLETE, stop and ask
              →  budget exhausted       →  FAILED
```

**The four outcomes are the point, not the loop.** A prose `lint-fix` today does something a naive
compilation would destroy: seeing lint pick up build artifacts, it recognizes that the correct repair
is an ignore pattern in a protected configuration file, and asks rather than editing sources to dodge
a misconfigured linter. Compiled as success-or-failure, the process would instead grind its budget and
report FAILED, or "fix" symptoms. Escalation is therefore contracted as an outcome distinct from
failure, exactly as `TOOLKIT-06` already makes refusal distinct from both success and failure — the
same principle one level up, at the operation rather than the model call.

**Escalation is produced by refusal, not by an opinion.** The write tools confine to the repository
root and refuse the protected configuration files and scripts. A worker that attempts one is denied,
and the denial is a recorded fact — *this write was attempted and refused* — rather than a
self-report that it complied. That is the *judging agent must show the basis for its verdict*
constraint met structurally instead of by asking a prompt nicely, and it is the reason the deny-list
is in this stage rather than a later hardening pass.

Compiling a process also converts advisory rules into enforced ones. `lint-fix.agent.md` asks an
agent never to modify auto-generated files and to respect protected configuration; a model that
ignores those produces a plausible report of having complied. A tool that refuses the path makes the
rule a wall. That gain is independent of which model is used, and it generalizes to every later
process.

**What is taken from the reference implementation, and what is not.** `WorkspaceSandbox` in Jeeves is
a single lexical containment primitive that fails closed and documents both the defect it replaced
and the limit it does not cover — symbolic links and junctions resolve outside it. It is taken. The
tool-group concept is taken with it, because scoping by *selection* — a tool not granted is simply
absent from the set the model is offered — has no gate to talk past, and because grouping is what
keeps later tool additions from each needing their own wiring decision. Jeeves reuses
`Microsoft.Extensions.AI` message and tool types as the vocabulary across its own seam even on the
Copilot path, translating them onto the SDK protocol, so that where the tool loop runs stays a
per-provider concern; that decision is taken too, because it is what lets a second provider return
later without a second tool abstraction.

Three things are deliberately left. The `util-read` group is the rigid regulated process Anneal
replaces. The shell tool is not granted at all: this process runs `fix.ps1` and `lint.ps1` as control
flow, and a worker that can run commands can do anything and report plausibly that it did not.
Jeeves' own `IChatEndpoint` is not adopted — Anneal's already distinguishes *run* from *probe* and
requires an availability enquiry, which is the better factoring; it gains tools rather than being
replaced.

**A contract gap this stage must close.** `TOOLKIT-11` transcribes the prompt sent, the reply
received, the model consulted and the token usage. When the provider runs the tool loop natively, a
worker's tool calls happen inside the SDK, so that transcript would record a prompt and a final reply
while being blind to every file the model touched — omitting the only part of a writing worker's
behavior worth auditing. Tool invocations earn their own clause rather than widening `TOOLKIT-11`,
because the two are checked by different readings of the record and either could lose its mechanism
without taking the other with it.

**Clauses.** `TOOLKIT-18` — every tool invocation a model makes is transcribed with its arguments and
its outcome, including a refused one. `TOOLKIT-19` — `lint-fix` drives the repository to a clean lint
or reports why it could not. `TOOLKIT-20` — an operation reports escalation as an outcome distinct
from both success and failure. `TOOLKIT-I6` — a model is granted tools only by group selection, every
filesystem path resolves inside the repository root, and a write to a protected configuration file or
repository script is refused. **`TOOLKIT-I1` is retired**, not deleted silently: its read-only
guarantee cannot survive a writing process, and `TOOLKIT-I6` keeps the explicit-allowlist half that
was doing the real work. The retired number is never reused.

`lint-fix` gets its own section document under Toolkit, as every CLI-invocable operation does.

**Leaves working:** everything. `lint.ps1`, `build.ps1` and the existing operations are untouched;
the prose `lint-fix` agent stays in the payload and keeps working until the compiled one is proven
against this repository.

**Exit conditions:** `dotnet anneal lint-fix` drives Anneal's own repository from a dirty lint state
to a clean one without human help; a write to each protected configuration file and repository script
is provably refused; a path escaping the repository root is provably refused; a refused write appears
in the transcript; escalation is returned and rendered distinctly from failure; `TOOLKIT-18`,
`TOOLKIT-19`, `TOOLKIT-20` and `TOOLKIT-I6` are verified by tests that exist and pass; `TOOLKIT-I1` is
retired from the contract with its replacement in place; `pwsh ./build.ps1` and `pwsh ./lint.ps1` both
pass.

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
