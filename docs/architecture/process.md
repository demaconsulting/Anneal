---
level: system
covers:
  - .github/agents/**
  - .github/skills/**
  - .github/standards/**
  - AGENTS.md
---

[← Architecture Overview](./overview.md)

# Process

Process is the bootstrap harness: the agent prompts, the standards, and the skills they load
on demand. Its content is read by a language model rather than executed, and everything odd about
this repository follows from that. It is **terminal** — [MIGRATION.md](../../MIGRATION.md)
dismantles it into compiled processes, with `helper` and `architecture-design` absorbed last — so
the contract below is deliberately not extended, and a clause is retired with the agent it describes
rather than carried forward. If Process were rewritten, a consumer would notice immediately — not
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
  What is promised is membership in that layout, not presence on disk: `MIGRATION.md` belongs to the
  layout while being absent from every repository outside a Migration. Build output the tooling
  produces rather than the layout defines is outside this promise.
  *Verified by:* `AgentReferencesResolve`

- **PROCESS-03** — Every standard in `.github/standards/` is reachable by an agent — named by an agent
  prompt, or by the Standards Application matrix in `AGENTS.md` that every agent loads — so no standard
  ships that nothing loads.
  *Verified by:* `NoOrphanedStandards`

- **PROCESS-04** — Every agent prompt defines a report template whose first metadata field is `**Result**`.
  *Verified by:* `ReportTemplateShapeIsUniform`

- **PROCESS-05** — Every agent handles each result value that any agent it invokes is able to emit.
  *Verified by:* `TODO.HandoffCoverageIsComplete`

- **PROCESS-06** — The worst-case single invocation — `AGENTS.md`, the largest agent prompt, and the four
  largest standards — stays within the context budget declared in
  [Prompt Authoring](./process/prompt-authoring.md), counted by the method declared there.
  *Verified by:* `WorstCaseInvocationWithinBudget`

- **PROCESS-07** — Every work mode the payload names — in a report template's mode field, in the
  classification vocabulary `AGENTS.md` carries, or in the phrase form `{Name} mode` — is one that
  `change-classification.md` defines, so no agent can act on a classification no other agent recognizes.
  *Verified by:* `ModeVocabularyIsClosed`

- **PROCESS-08** — The repository's `AGENTS.md` equals `.github/template/AGENTS.pristine.md` plus its
  Template Stewardship section, so process content cannot drift between the working and shipped copies.
  *Verified by:* `AgentsFileMatchesPristine`

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
- **[Template](./template.md)** — presence of an unmodified `AGENTS.pristine.md` in the shipped layout,
  and of the repository scripts the agent prompts instruct an agent to run together with the tooling
  configuration those scripts read.

### Invariants

- **PROCESS-I1** — Exactly two agents are user-invocable; every other agent is reachable only as a
  sub-agent.
  *Verified by:* `EntryPointsAreExactlyTwo`

- **PROCESS-I2** — No normative rule is stated in more than one payload file; other files reference the
  owning file rather than restating it.
  *Verified by:* `TODO.NormativeRulesHaveOneOwner`

## Composition

Process divides into two zones with a single crossing point, and the division is the most important thing
about its interior.

```mermaid
flowchart TD
    User(["Developer"])

    subgraph Interactive["Interactive zone — converses, never mechanical"]
        Helper[helper]
        ArchDesign[architecture-design]
    end

    subgraph Mechanical["Mechanical zone — routed, never converses"]
        Dispatch[dispatch]
        ArchUpdate[architecture-update]
        Apply[apply]
        ScopeCheck[scope-check]
        TemplateSync[template-sync]
    end

    Tree[("docs/architecture/")]

    User --> Helper
    User --> ArchDesign
    Helper --> Dispatch
    Helper --> TemplateSync
    Dispatch --> Apply

    Helper -.-> ArchDesign
    ArchDesign ==> Tree
    Tree ==> ArchUpdate
    Tree ==> Apply

    linkStyle 5 stroke-dasharray: 3 3
```

Three kinds of edge appear, and confusing them is the failure this diagram exists to prevent:

- **Solid — invocation.** One agent calls another as a sub-agent and consumes its report.
- **Thick — artifact.** No call occurs. `architecture-design` writes the tree, and other agents read it
  later, possibly in a different session.
- **Dotted — hand-off by name.** `helper` tells the developer to invoke `architecture-design` themselves.
  It must not call it, because that agent's method is a live interview and a headless invocation would
  invent the answers.

`architecture-update` and `scope-check` stay in the mechanical zone as directly invocable agents —
`helper` still calls `scope-check` to verify a finished change, and both apply to Migration-mode work —
but `dispatch` no longer calls either for Change mode; see Decisions below.

The zones exist because the two kinds of agent fail differently. An interactive agent fails by assuming
instead of asking; a mechanical one fails by widening its scope or misreporting its result. The
mechanical zone is therefore where the structural contract above earns its place, and the interactive
zone is where behavioral verification is spent.

For the span of the migration [MIGRATION.md](../../MIGRATION.md) carries, a third shape coexists with
these two rather than replacing either: a compiled Router selects one of a small worker catalog
(`DemaConsulting.Anneal.Toolkit.Process`), each worker composed from the primitive library
[Toolkit](./toolkit.md) owns. `dispatch` calls it directly for every Change-mode request (see Decisions
below); `architecture-update` and `scope-check` keep their edges to `Tree` above for the jobs the Router
does not cover — Maintenance, Migration, and any Change-mode invocation run through the prose path
directly rather than through `dispatch`.

The **standards** are cross-cutting rather than owned by any agent: each is the single definition of its
subject, and agents load two to four by task. This is why PROCESS-02 and PROCESS-03 are contract clauses —
a payload whose references do not resolve is not a smaller payload, it is a broken one. The **skills** sit
below the standards and carry repeatable procedures that would otherwise bloat a prompt paid for on every
invocation.

The standards divide into two populations, and PROCESS-03 admits two loading surfaces because of it. The
**process** standards — architecture documentation, change classification, system contracts — are named
directly by the agent prompts, because every agent here does process work and knows at authoring time
which of them it needs. The **product-code** standards — coding, C# language, C# testing, technical
documentation, testing principles — are named by no prompt at all, and reached only through the Standards
Application matrix keyed on the file types an agent discovers at runtime. That indirection is deliberate:
requiring each prompt to name them would hard-code a product technology into agents that must stay
technology-neutral, and would enlarge every prompt against the budget PROCESS-06 defends.

The seam that must not move is the one between `helper` and `dispatch`. Everything upstream of it talks
to a person; everything downstream is classified, bounded work. If that seam ever widens — if `helper`
starts performing work, or a mechanical agent starts asking the developer questions — the interactive
zone has become a system in its own right and should be split out of Process.

## Decisions

**`architecture-design` is deliberately not model-invocable** — it is marked
`disable-model-invocation: true` so no agent can call it. The rejected alternative was letting `dispatch`
invoke it when boundaries look wrong, which would produce a plausible architecture tree that nobody
agreed to. That is worse than producing none, because a tree carries authority once it exists. This
reverses only if the agent gains a non-interview mode whose output is explicitly provisional.

**`helper` routes but never works** — the front door classifies nothing and edits nothing; it gathers
context and calls `dispatch`. Allowing it to make small edits directly was rejected because a self-granted
exemption does not stay narrow, and because `helper` does not load the standards that would make the edit
correct. The same argument is under review one level down for `dispatch`, and is recorded in
[BACKLOG.md](../../BACKLOG.md).

**Exactly two entry points** — every other agent is a sub-agent, so there are no trigger words to learn.
The rejected alternative was exposing each agent to the developer, which pushes classification onto the
person least equipped to do it consistently.

**Standards are loaded on demand, never bundled** — recorded here because it is the design choice the
budget PROCESS-06 protects depends on. [Prompt Authoring](./process/prompt-authoring.md) owns the
mechanism and why bundling into `AGENTS.md` was rejected.

**Bounded repairs, no planning phase** — `dispatch` allows one documentation repair and one code repair
per change, because a documentation finding has to be fixed before implementation can use it, but
grinding a finding that will not clear means the change was misunderstood at the start. The rejected
alternative was a PLANNING → DEVELOPMENT → QUALITY state machine with three retries, which multiplied
every cost by up to four and made the process expensive rather than the design. What was refused was
the multiplier — cost paid on every subsequent change — not orchestration itself; sequencing bounded
work is what `dispatch` already is.

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
`CONSTRAINTS.md` has ever arrived through Intake in this repository's history, so closing only the
Intake path would have left every other mode's route into the file open. This entry records that
Process's Decisions section holds Change, Maintenance and Migration to the rule exactly as Intake is
held; `change-classification.md` § *Only the User Admits a Constraint* owns the rule itself, the
reasoning behind it, and the proposal mechanism.

No clause is added for it. Whether an agent proposes rather than files is a property of what agents do,
not of whether the payload loads and routes, so it falls on the behavioral side of the verification split
in [overview.md](./overview.md) and is established by inspection. Mechanizing it would mean deciding from
text alone whether a mention of `CONSTRAINTS.md` authorizes a write or merely points at it — a judgement
the four files that legitimately name the register would defeat.

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
repository at Migration stage S6, the condition [MIGRATION.md](../../MIGRATION.md) had named for
retiring the prose agent that preceded it, so `.github/agents/lint-fix.agent.md` is retired rather
than kept as a fallback with nothing left to fall back from. The node is removed rather than
redrawn as something else in the Mechanical zone, because it was never a sub-agent another agent
calls and consumes a report from — a developer or `helper` invokes `dotnet anneal lint-fix` directly,
the same relationship `ContractCheck` already has to this diagram without ever appearing in it as an
agent. Redrawing it as a direct-invocation node was rejected: this diagram's solid edges are
reserved for sub-agent invocation with a consumed report, and a fourth edge kind for "developer runs
a compiled command" would document machinery this system does not decide, since Toolkit already owns
that boundary in [overview.md](./overview.md) and `toolkit/lint-fix.md`.

**`dispatch`'s edges to `architecture-update` and `scope-check` were removed, not redrawn** — at
Migration stage S11, `dispatch` began calling `route` directly for every Change-mode request instead of
chaining `architecture-update` → `apply` → `scope-check`; the diagram above reflects that. Both agents
keep their edges from `Tree`, because `architecture-update` still writes it and `scope-check` still
reads it for the jobs `route` does not cover: Maintenance, Migration, and any Change-mode invocation run
through the prose path directly.

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

**Effort joins Scope as a second classification axis, and Migration is preserved rather than
dissolved into it** — `change-classification.md` now classifies Change-mode work along Scope
(contract reach) and Effort (magnitude) independently, because neither implies the other: a
mechanical rename touching hundreds of files crosses no contract, and a one-line change to a public
signature crosses one. A Massive Effort item decomposes into phases only under a mandatory
cumulative-Scope check evaluated across the whole phase set — never phase by phase alone — plus a
deterministic tripwire on `README.md`, `docs/architecture/**`, `CONSTRAINTS.md`, and `BACKLOG.md`,
because a set of individually-innocent phases can still cross a boundary none of them crosses alone.
Dissolving Migration into ordinary Scope×Effort routing was considered and rejected: Migration's step
invariants (self-hosting, one-way, no-silent-loss, named suspensions) are cross-phase monotonicity
properties that a per-request classification cannot express, and folding it away would mechanically
break `PROCESS-07`'s closed mode vocabulary. Migration is kept exactly as it already exists; the only
new integration point is that a single stage's own implementation, once a human has written the stage
and its exit condition, classifies by Scope and Effort like any other Change-mode work.

## Details

- [Prompt Authoring](./process/prompt-authoring.md) — how a prompt earns the tokens it costs, and
  what every prompt and standard in the payload must do to participate
