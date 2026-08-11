---
level: system
covers:
  - .github/agents/**
  - .github/skills/**
  - .github/standards/**
---

[← Architecture Overview](./overview.md)

# Process

Process is the bootstrap harness: the agent prompts, the standards, and the skills they load
on demand. Its content is read by a language model rather than executed, and everything odd about
this repository follows from that. It is **terminal** — [active-plan.md](../work/active-plan.md)
dismantles it into compiled processes, with the remaining conversational entry point absorbed last —
so the contract below is deliberately not extended, and a clause is retired with the agent or path it
describes rather than carried forward. If Process were rewritten, a consumer would notice immediately — not
because an interface changed, but because agents would classify work differently, touch different files,
and stop at different boundaries. The observable surface of a prompt is the behavior it produces.

That makes the contract below unusual, and it is worth being explicit about what it does **not** cover.
No clause here promises that an agent behaves well — the repository-wide decision in
[overview.md](./overview.md) records the verification split and why it exists. What the contract covers
is the structural integrity of the payload: the properties that must hold for the prose to be loadable,
routable, and internally consistent at all. A prompt that fails these is broken regardless of how well it
is written.

## Contract

### Provides

- **PROCESS-01** — Every agent file carries front matter whose `name` matches its filename and whose
  invocation flags are well-formed, so an agent can be selected by name without reading its body.
  *Verified by:* `AgentFrontMatterIsWellFormed`

- **PROCESS-02** — Every standard, skill, and agent prompt named by an agent prompt exists at the path
  given, and every other path an agent prompt names belongs to the repository layout this process
  defines — the layout [Template](./template.md) ships, and the files and directories every installed
  repository carries — so no reference resolves only in the repository the payload was authored in.
  What is promised is membership in that layout, not presence on disk: `.anneal/work/active-plan.md`
  belongs to the
  layout while being absent from every repository outside a Migration. Build output the tooling
  produces rather than the layout defines is outside this promise.
  *Verified by:* `AgentReferencesResolve`

- **PROCESS-03** — Every standard in `.github/standards/` is reachable — named by an agent
  prompt, or by a compiled worker's fixed standards list in source — so no standard
  ships that nothing loads.
  *Verified by:* `NoOrphanedStandards`

- **PROCESS-06** — The worst-case single invocation — the largest agent prompt, and the four
  largest standards — stays within the context budget declared in
  [Prompt Authoring](./process/prompt-authoring.md), counted by the method declared there.
  *Verified by:* `WorstCaseInvocationWithinBudget`

- **PROCESS-07** — Every work mode the payload names — in a report's mode field, or in the
  phrase form `{Name} mode` — is one that
  `change-classification.md` defines, so no agent can act on a classification no other agent recognizes.
  *Verified by:* `ModeVocabularyIsClosed`

- **PROCESS-09** — Every Scope value the payload's report-template fields name is one that
  `change-classification.md` defines, so the vocabulary agents route on cannot be extended or
  re-labelled by a single file.
  *Verified by:* `ScopeVocabularyIsClosed`

- **PROCESS-10** — Every Effort value the payload names is one that `change-classification.md`
  defines, so the magnitude vocabulary a decomposition check routes on cannot be extended or
  re-labelled by a single file — the same closure PROCESS-09 already gives Scope, extended to the
  axis alongside it.
  *Verified by:* `EffortVocabularyIsClosed`

### Requires

- **[ContractCheck](./toolkit/contract-check.md)** — mechanical verification of clause-to-test links, and a
  failure taxonomy stable enough for a skill to explain.
- **[Template](./template.md)** — presence of the repository scripts the agent prompt instructs a
  developer to run together with the tooling configuration those scripts read.

### Invariants

- **PROCESS-I1** — Exactly one agent file exists in the payload, and it is user-invocable.
  *Verified by:* `EntryPointsAreExactlyOne`

- **PROCESS-I2** — No normative rule is stated in more than one payload file; other files reference the
  owning file rather than restating it.
  *Verified by:* `NormativeRulesHaveOneOwner`

## Composition

Process has a single conversational entry point. Everything else it triggers is compiled Toolkit
work, not a second prose agent.

```mermaid
flowchart TD
    User(["Developer"])
    Helper[helper]
    Toolkit[["dotnet anneal ..."]]
    Tree[(".anneal/architecture/")]

    User --> Helper
    Helper --> Toolkit
    Helper ==> Tree
```

Two kinds of edge appear, and confusing them is the failure this diagram exists to prevent:

- **Solid — invocation.** `helper` runs a compiled Toolkit operation and reads its exit code and
  report.
- **Thick — artifact.** No call occurs. `helper` writes the tree during boundary work, and a later
  session — human or compiled — reads it.

Every other prose agent this repository ever hosted (`dispatch`, `apply`, `tier-check`,
`architecture-update`, `scope-check`, `lint-fix`, `template-sync`) is retired; each was removed from
this diagram in turn as the compiled path replacing its job was proven, per the Decisions entries
below. `helper` is what remains: it classifies a request's Mode itself and invokes the matching
Toolkit operation (`intake`, `route`, `maintain`, `stage-contract`, `verify-change`, `lint-fix`,
and peers) directly, or, for boundary work — the repository's first architecture tree, a re-cut, or
a Migration proposal — writes the tree itself rather than handing off to a second entry point.

The seam that must not move is the one between `helper`'s conversation and the compiled work it
triggers. Everything upstream of it talks to a person; everything downstream is classified, bounded,
and mechanical. If that seam ever widens — if `helper` starts performing ordinary implementation work
instead of only boundary deliverables, or a compiled operation starts asking the developer questions —
the boundary this file draws has moved and Process needs to say so explicitly, not absorb it silently.

The **standards** are cross-cutting rather than owned by any agent: each is the single definition of its
subject, and `helper` loads two to four by task; a compiled worker loads its own fixed list from
source. This is why PROCESS-02 and PROCESS-03 are contract clauses —
a payload whose references do not resolve is not a smaller payload, it is a broken one. The **skills** sit
below the standards and carry repeatable procedures that would otherwise bloat a prompt paid for on every
invocation.

The standards divide into two populations, and PROCESS-03 admits two loading surfaces because of it. The
**process** standards — architecture documentation, change classification, system contracts — are named
directly by `helper`'s own prompt, because that is the only agent prompt left and it does process work
throughout. The **product-code** standards — coding, C# language, C# testing, technical
documentation, testing principles — are named by no prompt at all, and reached only through the fixed
per-worker standards arrays each compiled Toolkit worker carries in source
(`WorkerStandards.cs` and the `*Standards` arrays under `src/DemaConsulting.Anneal.Toolkit/Process/Workers/`).
That indirection is deliberate: requiring `helper`'s prompt to name them would hard-code a product
technology into the one prose agent that must stay technology-neutral, and would enlarge it against
the budget PROCESS-06 defends.

## Decisions

**Boundary work stayed inside `helper` rather than remaining a second entry point** — the rejected
alternatives were a cold hand-off to `architecture-design` or a headless sub-agent call. Both force
the user to predict the need for a re-cut before the interview does, and the headless form would
invent answers for a conversation nobody had.

**`helper` routes ordinary work but writes boundary deliverables itself** — Intake, Change, and
Maintenance still go straight to compiled actions. The exception is boundary work: the architecture
tree, `README.md`, governance files, and `.anneal/work/active-plan.md` when a Migration proposal is
what the repository needs. Letting `helper` implement ordinary code or maintenance directly was
rejected because a self-granted exemption does not stay narrow.

**Exactly one entry point** — every other agent is a sub-agent or compiled action, so there are no
trigger words to learn and no separate boundary-design front door to guess. The rejected alternative
was exposing multiple prompts to the developer, which pushes classification onto the person least
equipped to do it consistently.

**Standards are loaded on demand, never bundled** — recorded here because it is the design choice the
budget PROCESS-06 protects depends on. [Prompt Authoring](./process/prompt-authoring.md) owns the
mechanism and why bundling into `AGENTS.md` was rejected.

**Bounded repairs, no planning phase** — the compiled authoring paths allow one documentation repair
and one code repair per change, because a documentation finding has to be fixed before implementation
can use it, but grinding a finding that will not clear means the change was misunderstood at the
start. The rejected alternative was a PLANNING → DEVELOPMENT → QUALITY state machine with three
retries, which multiplied every cost by up to four and made the process expensive rather than the
design. What was refused was the multiplier — cost paid on every subsequent change — not
orchestration itself; sequencing bounded work is what `route`, `maintain`, and `stage-contract`
already do.

**The classification vocabulary is contracted as two clauses, not one** — modes and scope are one idea
to a reader and two different problems to a checker, and PROCESS-07 spent a release as an unfulfilled
obligation because of it. The mode half is only checkable while no payload file uses the word for
something else: `template-sync` called its own three operations modes, and its report field declared
them in a form indistinguishable from a work-mode declaration, so any pattern strict enough to catch a
real mistake also caught that file. Renaming that agent's concept to **Operation** — rather than
renaming the work modes, which a standard owns, five agents reference, and PROCESS-07 rides on — is
what made the mode half honestly mechanizable, and the two halves then earn separate clauses because
each is checked by a different reading of the payload and either could lose its mechanism without
taking the other with it. The rejected alternative was one clause naming two tests, which would report
a single verdict over two independent promises and hide which one had lapsed.

**Every mode here is bound by the constraint-admission rule, not only Intake** — no entry in
`.anneal/work/constraints.md` has ever arrived through Intake in this repository's history, so closing only the
Intake path would have left every other mode's route into the file open. This entry records that
Process's Decisions section holds Change, Maintenance and Migration to the rule exactly as Intake is
held; `change-classification.md` § *Only the User Admits a Constraint or Assumption* owns the rule itself, the
reasoning behind it, and the proposal mechanism.

No clause is added for it. Whether an agent proposes rather than files is a property of what agents do,
not of whether the payload loads and routes, so it falls on the behavioral side of the verification split
in [overview.md](./overview.md) and is established by inspection. Mechanizing it would mean deciding from
text alone whether a mention of `.anneal/work/constraints.md` authorizes a write or merely points at it —
a judgement the four files that legitimately name the register would defeat.

**Scope values are bare names, not qualified ordinals** — S10 retired the numeric ordinal scale in
favor of naming each value directly after the toolkit's own compiled worker
(`SmallFixWorker`, `ContractChangeWorker`, `StructuralChangeWorker`), so the vocabulary humans read and
the vocabulary the code already runs became the same words. The ordinal-plus-qualifier apparatus this
entry used to describe existed only to keep a bare numeric scale readable and correctly ordered — a
name alone carries no direction, and the priors a model brings about a bare `0`, `1`, `2` scale point
the wrong way in this domain, where its lowest ordinal was the trivial class rather than the severe
one incident and security usage would suggest. Once the scope vocabulary is itself a set of names with
no numeral to misread, that scaffolding is redundant weight rather than a safeguard: **Scope may still
be raised mid-flight, never silently lowered** — `change-classification.md` states the order the three
names carry — but nothing about that ordering depends on a digit or a parenthetical qualifier being
repeated at each decision site.

**`lint-fix` left the diagram entirely rather than changing shape within it** — the compiled
`dotnet anneal lint-fix` (`TOOLKIT-19` in [Toolkit](./toolkit.md)) was proven end to end against this
repository, satisfying the condition [active-plan.md](../work/active-plan.md) named for
retiring the prose agent that preceded it, so `.github/agents/lint-fix.agent.md` is retired rather
than kept as a fallback with nothing left to fall back from. The node is removed rather than
redrawn as something else in the Mechanical zone, because it was never a sub-agent another agent
calls and consumes a report from — a developer or `helper` invokes `dotnet anneal lint-fix` directly,
the same relationship `ContractCheck` already has to this diagram without ever appearing in it as an
agent. Redrawing it as a direct-invocation node was rejected: this diagram's solid edges are
reserved for sub-agent invocation with a consumed report, and a fourth edge kind for "developer runs
a compiled command" would document machinery this system does not decide, since Toolkit already owns
that boundary in [overview.md](./overview.md) and `toolkit/lint-fix.md`.

**`dispatch.agent.md` was retired once `intake` compiled its last remaining job** — after `route`,
`maintain`, `stage-contract`, `lint-fix`, and `verify-change` were already direct Toolkit paths, the
only mechanical work still living in prose was Intake filing. `intake` compiles that admission test,
so `helper` now owns Mode selection itself and invokes `intake`, `route`, `maintain`, or
`stage-contract` directly. One behavior did not carry forward: `dispatch`'s hidden "recreate a
missing register from template" fallback was dropped. A missing Intake register now escalates so
layout repair stays explicit rather than being buried inside unrelated filing work.

**`apply.agent.md` was retired** once its last two callers — `helper`'s "a
specific fix already reported" row and the stale Migration-mode routing that predated direct Toolkit
invocation — were rewired to the compiled Change path itself, following exactly the `lint-fix`
precedent above: the node is removed once nothing calls it, not kept as an unused fallback. Its own
removal from the diagram left `architecture-update`
and `scope-check` each with one fewer edge, adjusted above; neither agent's own remaining job changed.
`architecture-update` and `scope-check` were not exempt from the same fate — each was the next named
target once a compiled equivalent for its remaining job existed (a design-only path built from
`DocumentAuthor` alone, and a standalone verify path built from `Verifier` and the existing
`DeterministicCheck` pair), not a permanent carve-out from the destination this file states.

**`architecture-update.agent.md` was retired** once `stage-contract` — the
Toolkit's compiled design-only path, running `DocumentAuthor` alone to stage a contract clause ahead
of implementation — was live-validated against a real model, the same bar `maintain` cleared before
`apply.agent.md` retired. `helper` gained a **caller-declared** direct branch for it: Step 1 sends a
request to `stage-contract` only when the user explicitly asked to stage a clause rather than build
it now, never inferred from a request's size or difficulty, mirroring how Maintenance mode is
caller-declared rather than classified. `architecture-update`'s edge to `Tree` is removed from the
diagram above, following the
`lint-fix`/`apply` precedent: the node is removed once nothing calls it. One thing did not carry
forward — `architecture-update.agent.md`'s Step 5 produced an explicit `Prune Results` table naming
every section document examined and its verdict; `StageContractReport` carries only the files changed
and a summary, since the mechanical checks this stage added verify scope and clause well-formation, not
which documents `DocumentAuthor` considered pruning. `.anneal/architecture/toolkit/stage-contract.md`'s
own charter still instructs pruning; only the itemized report of it did not survive.

`scope-check` is not exempt from the same fate — it is the one remaining named target once a compiled
equivalent for its job exists (a standalone verify path built from `Verifier` and the existing
`DeterministicCheck` pair), not a permanent carve-out from the destination this file states.

**`scope-check.agent.md` was retired** once `verify-change` — the compiled
standalone verify path built from `DiffCheck`, `DeterministicCheck`, and `Verifier`, the same
primitives `ContractChangeWorker`/`StructuralChangeWorker` already compose for their own verification
half — was live-validated against a real model, the same bar `stage-contract` cleared before
`architecture-update.agent.md` retired. Unlike `stage-contract`, `helper` invokes it directly:
`verify-change` declares `OperationCategory.Advisory`, edits nothing, and produces no change for a
separate routing agent to classify or register, so there is nothing here for a prose middle layer to
do — the same "developer runs a compiled command" relationship `lint-fix` already has to this
diagram, not the `stage-contract` precedent's sub-agent call. `helper`'s Step 3 table gained
one row for it; `AGENTS.md` and its pristine template counterpart gained the matching bullet.
`scope-check`'s own edge from `Helper` is removed from the diagram above, following the
`lint-fix`/`apply`/`architecture-update` precedent: the node is removed once nothing calls it. This was
the last named retirement target this file tracked; no further prose agent is queued behind it as of
this stage.

**The compiled catalog is a Router choosing a bounded worker, not a generic plan-build-review loop**
— the router asks one narrow typed question per pass (select a worker, ask for bounded research, or
report no route) against two independent counters, a research budget and a worker-reroute budget,
because the two are different failures: research means the router lacked facts, reroute means the
selected worker learned mid-execution that the classification was wrong, and sharing one counter
would let a cheap research pass starve a legitimate reroute. Cap exhaustion with no crisp human-only
next step reports `Failed`; it reports `Escalated` only when the router can name a specific step only
a person can take. The rejected alternative, considered directly against a comparable
planner-developer-quality implementation agent elsewhere, was a universal three-phase loop with fixed
retries on every change — rejected for the same reason **Bounded repairs, no planning phase** above
already rejected it once: the multiplier is paid on every subsequent change regardless of whether the
change needed it. Planner and Verifier are **route-selected per worker**, not universal — a Small Fix
worker (`Developer` → `DeterministicCheck`, one local repair pass) pays for neither, while a Structural
Change worker spends at most two `Planner` calls: an initial plan, and — only against a
`VerificationVerdict.StrategyRevisionRequired` finding, never a documentation or code repair finding —
one re-plan from its own independent, non-resetting budget, counted and exhausted separately from the
documentation and code repair budgets. Only Template Sync now remains deferred; Small Fix, Contract
Change, and Structural Change all ship. (An earlier draft of this entry claimed no prose agent could
retire until Template Sync existed too, citing the one-way invariant as authority — the invariant says
nothing about sequencing unrelated work, and later migration stages retired individual prose agents
as their own proven conditions were met. Corrected here rather than left standing as an unchecked
decree.)

**`AGENTS.md`, `.github/template/AGENTS.pristine.md`, and `template-sync.agent.md` were retired
together, without a compiled replacement.** Anneal moved from a multi-prompt bootstrap — a shared
rule file every prose agent loaded, kept byte-identical to a template copy by a dedicated sync agent —
to a single self-contained prose agent (`helper`) plus compiled Toolkit operations. Once `helper`
was the only prose agent left, a shared cross-agent rule file had nothing left to coordinate: its
routing and classification content already lived in `helper.agent.md` and `change-classification.md`,
and its Standards Application matrix duplicated what each compiled worker's own fixed standards array
already states more precisely in source. Carrying it forward would have meant maintaining a second,
looser description of a mechanism the code already enforces exactly. `template-sync.agent.md` had no
remaining job once there was no `AGENTS.md` copy to reconcile and no other agent-authored payload file
drifting from the template on a schedule this repository could not otherwise check; `PROCESS-08`
(byte-equality) and `INSTALLER-03`/`TEMPLATE-04` (pristine-copy install/patch) retired with it,
following the same "removed once nothing calls it" precedent as every prose-agent retirement above,
rather than being kept as unused fallback machinery. One real, unrelated defect surfaced while
untangling this: `technical-documentation.md` had been reachable only through `AGENTS.md`'s Standards
Application matrix and was an orphaned standard the moment that matrix went away — it is now listed
directly in `DocumentAuthorStandards` for both `ContractChangeWorker` and `StructuralChangeWorker`,
closing the gap `NoOrphanedStandards` exists to catch.

**Effort joins Scope as a second classification axis, and Migration is preserved rather than
dissolved into it** — `change-classification.md` now classifies Change-mode work along Scope
(contract reach) and Effort (magnitude) independently, because neither implies the other: a
mechanical rename touching hundreds of files crosses no contract, and a one-line change to a public
signature crosses one. A Massive Effort item decomposes into phases only under a mandatory
cumulative-Scope check evaluated across the whole phase set — never phase by phase alone — plus a
deterministic tripwire on `.anneal/governance/`, `.anneal/profile/`, `.anneal/work/`, and
`.anneal/architecture/`, because a set of individually-innocent phases can still cross a boundary none
of them crosses alone.
Dissolving Migration into ordinary Scope×Effort routing was considered and rejected: Migration's step
invariants (self-hosting, one-way, no-silent-loss, named suspensions) are cross-phase monotonicity
properties that a per-request classification cannot express, and folding it away would mechanically
break `PROCESS-07`'s closed mode vocabulary. Migration is kept exactly as it already exists; the only
new integration point is that a single stage's own implementation, once a human has written the stage
and its exit condition, classifies by Scope and Effort like any other Change-mode work.

## Details

- [Prompt Authoring](./process/prompt-authoring.md) — how a prompt earns the tokens it costs, and
  what every prompt and standard in the payload must do to participate
