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

Process is the product: eight agent prompts, eight standards, and the skills they load on demand. It is
the only system whose content is read by a language model rather than executed, and everything odd about
this repository follows from that. If Process were rewritten, a consumer would notice immediately — not
because an interface changed, but because agents would classify work differently, touch different files,
and stop at different boundaries. The observable surface of a prompt is the behavior it produces.

That makes the contract below unusual, and it is worth being explicit about what it does **not** cover.
No clause here promises that an agent behaves well. Those promises exist, but they are properties of a
conversation, and they are established by inspection or by a sandbox run rather than by a script — the
repository-wide decision in [overview.md](./overview.md) records why. What the contract covers is the
structural integrity of the payload: the properties that must hold for the prose to be loadable,
routable, and internally consistent at all. A prompt that fails these is broken regardless of how well it
is written.

## Contract

### Provides

- **PROCESS-01** — Every agent file carries front matter whose `name` matches its filename and whose
  invocation flags are well-formed, so an agent can be selected by name without reading its body.
  *Verified by:* `TODO.AgentFrontMatterIsWellFormed`

- **PROCESS-02** — Every standard, skill, script, and register file named by an agent prompt exists at
  the path given.
  *Verified by:* `TODO.AgentReferencesResolve`

- **PROCESS-03** — Every standard in `.github/standards/` is referenced by at least one agent, so no
  standard ships that nothing loads.
  *Verified by:* `TODO.NoOrphanedStandards`

- **PROCESS-04** — Every agent prompt defines a report template whose first metadata field is `**Result**`.
  *Verified by:* `TODO.ReportTemplateShapeIsUniform`

- **PROCESS-05** — Every agent handles each result value that any agent it invokes is able to emit.
  *Verified by:* `TODO.HandoffCoverageIsComplete`

- **PROCESS-06** — The worst-case single invocation — `AGENTS.md`, the largest agent prompt, and the four
  largest standards — stays within the declared context budget.
  *Verified by:* `TODO.WorstCaseInvocationWithinBudget`

- **PROCESS-07** — Every mode and tier named anywhere in the payload is one that
  `change-classification.md` defines, so no agent can act on a classification no other agent recognizes.
  *Verified by:* `TODO.ClassificationVocabularyIsClosed`

- **PROCESS-08** — The repository's `AGENTS.md` equals `.github/template/AGENTS.pristine.md` plus its
  Template Stewardship section, so process content cannot drift between the working and shipped copies.
  *Verified by:* `TODO.AgentsFileMatchesPristine`

### Requires

- **[ContractCheck](./contract-check.md)** — mechanical verification of clause-to-test links, and a
  failure taxonomy stable enough for a skill to explain.
- **[Template](./template.md)** — presence of an unmodified `AGENTS.pristine.md` in the shipped layout.

### Invariants

- **PROCESS-I1** — Exactly two agents are user-invocable; every other agent is reachable only as a
  sub-agent.
  *Verified by:* `TODO.EntryPointsAreExactlyTwo`

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
        TierCheck[tier-check]
        LintFix[lint-fix]
        TemplateSync[template-sync]
    end

    Tree[("docs/architecture/")]

    User --> Helper
    User --> ArchDesign
    Helper --> Dispatch
    Helper --> LintFix
    Helper --> TemplateSync
    Dispatch --> ArchUpdate
    Dispatch --> Apply
    Dispatch --> TierCheck

    Helper -.-> ArchDesign
    ArchDesign ==> Tree
    Tree ==> ArchUpdate
    Tree ==> Apply

    linkStyle 8 stroke-dasharray: 3 3
```

Three kinds of edge appear, and confusing them is the failure this diagram exists to prevent:

- **Solid — invocation.** One agent calls another as a sub-agent and consumes its report.
- **Thick — artifact.** No call occurs. `architecture-design` writes the tree, and other agents read it
  later, possibly in a different session.
- **Dotted — hand-off by name.** `helper` tells the developer to invoke `architecture-design` themselves.
  It must not call it, because that agent's method is a live interview and a headless invocation would
  invent the answers.

The zones exist because the two kinds of agent fail differently. An interactive agent fails by assuming
instead of asking; a mechanical one fails by widening its scope or misreporting its result. The
mechanical zone is therefore where the structural contract above earns its place, and the interactive
zone is where behavioral verification is spent.

The **standards** are cross-cutting rather than owned by any agent: each is the single definition of its
subject, and agents load two to four by task. This is why PROCESS-02 and PROCESS-03 are contract clauses —
a payload whose references do not resolve is not a smaller payload, it is a broken one. The **skills** sit
below the standards and carry repeatable procedures that would otherwise bloat a prompt paid for on every
invocation.

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

**Standards are loaded on demand, never bundled** — an agent reads the two to four standards its task
needs. Bundling them into `AGENTS.md` was rejected because that file is paid for on every invocation,
which is the budget PROCESS-06 protects.

## Details

- [Prompt Authoring](./process/prompt-authoring.md) — how a prompt earns the tokens it costs
