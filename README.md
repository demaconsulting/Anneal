# Anneal

Agent definitions, standards, and a repository template for **evolutionary** software development —
designs that must keep moving after the first version ships.

Repeated working makes metal brittle; annealing relieves the accumulated stress so it can be shaped
again. Architectures stiffen the same way under accumulated change. This process is built to keep
them workable, and to restructure them deliberately when they are not.

Anneal gives AI coding agents enough structure to produce maintainable, reviewable software without
the inertia of a formal process. It sits between unstructured prompting and regulated development.

**The idea, in one paragraph.** A component's **contract** is what other code is allowed to depend
on — the promises it publishes at its boundary. Documentation work is triggered by a change to a
contract, and by nothing else. Rewrite a component's internals however you like and you owe no
documentation; change what it promises and you edit one file, where every promise names the test
that proves it. Agents classify a task against that rule before starting, and the classification
decides both how much process the task gets and what the agent is permitted to touch.

> **Not a regulated-development process.** `Anneal` does not produce IEC 62304 or equivalent
> compliance evidence.[^1]

## Features

- **Refactor without paperwork.** Rearrange the inside of a component as much as you like.
  Documentation costs you something only when you change a promise other code depends on.
- **Every promise is backed by a test.** Each clause of a contract names a test, and the build fails
  if that test is missing, renamed, or last seen failing.
- **Process sized to the change.** Every task is classified before work starts, and the common case —
  a change no other code can observe — carries no documentation step at all.
- **Agents stop instead of improvising.** An agent declares what it will touch before it touches it.
  Reaching that boundary is a stop and a report back to you, never a decision to widen it.
- **Filing a need costs one line.** A standing property the system must always satisfy goes into
  `CONSTRAINTS.md`, where the next design review reads it; work that finishes goes into `BACKLOG.md`.
  No code, no tests, no contract.
- **Tidying is a first-class activity.** Background quality work has its own mode, with a declared
  scope and a stopping point, so it cannot drift into a redesign.
- **Restructuring is a defined operation.** Reshaping the architecture proceeds in steps you approve,
  rather than one enormous commit or a branch that lives for months.
- **The reasoning survives.** Why a component promises what it promises is recorded beside the
  promise, so a new developer — or a new agent — can recover it without a parallel tree of design
  documents to keep in sync.
- **One command to install**, from a clone you check out at the revision you want.

**One of these is enforced by a machine; the rest are instructions.** `check-contracts.ps1` fails the
build when a contract clause names no test, or names a test that is missing, stale, or failing.
Everything else above is a rule agents are told to follow, held by prompt and review rather than by
tooling.

## What It Costs

Every component boundary needs its promises written down, and every promise needs a named test —
that is the adoption cost on an existing codebase, and it is real. Restructures need your approval at
each step, so the process cannot run unattended on the work that matters most. And the trade itself —
a small cost per contract change in exchange for none per file change — only pays on a design that is
still moving.

Several things are deliberately absent: per-unit requirements, per-unit and per-subsystem design
documents, verification design documents, an architecture model, formal review tracking, and
multi-retry orchestration. Each was left out because its cost is paid on **every** subsequent change.
[Maintaining](docs/user-guide/maintaining.md) has the full rationale, the trade-offs, and what must
not be reintroduced.

## Documentation

- **[Getting Started](docs/user-guide/getting-started.md)** — install, bootstrap, first change
- **[Common Tasks](docs/user-guide/common-tasks.md)** — the prompt to use for each day-to-day job
- **[Workflow](docs/user-guide/workflow.md)** — classification and agent routing in practice
- **[Authoring](docs/user-guide/authoring.md)** — writing the architecture tree and contracts well
- **[Reference](docs/user-guide/reference.md)** — every agent, standard, skill, and script in detail
- **[Maintaining](docs/user-guide/maintaining.md)** — design rationale and how to change this system

## Requirements

What the process must hold true to deliver the features above, stated so you can check a repository
against them rather than argue about them. Each says something no feature bullet already says:

- Every kind of work a product receives has exactly **one** defined entry point.
- How much process a task needs is decided from a **single** definition of classification, used by
  every agent — so no two agents can hold different ideas of what a tier means.
- A large restructure can proceed in stages without disabling any check.
- The standards an agent must load for any one task are typically two and never more than four.
- Nothing below component level is documented as a requirement, design, or verification artifact.

## How It Works

The process rests on one rule: **documentation work is triggered by a change to what other code
depends on, never by a change to a file.** Each system publishes a contract — the promises consumers
outside it may rely on. Everything below that boundary is free to change without documentation cost.
That freedom is the point; it is what lets a design keep moving after the first version ships.

Work is classified twice before it starts. **Mode** — recording an idea, making a change, tidying,
or migrating — decides what an agent is allowed to touch. **Tier** decides how far a change reaches
into published contracts, and therefore how much documentation moves with it. The two are
independent, and most work lands on the cheapest combination of both. Classification is defined in
exactly one place, so no two agents can hold different ideas of what a tier means.

Documentation is a descent, not a pile. Each level answers a different question at a different
altitude, no level restates the one below it, and a reader descends only as far as the task requires.
Any change should require editing exactly one documentation file. Levels are created when they are
earned — a repository whose whole story fits in its README is correctly documented, not
under-documented.

One relationship is **mechanically enforced**: every contract clause names a boundary test that
exists and passed. `check-contracts.ps1` fails CI when a clause names nothing, names something that
is not a test, or is backed only by a stale or failing result. It fails closed — a clause it cannot
understand is an error, never a silent skip — and it needs no tooling beyond PowerShell. Everything
else is judgement, deliberately; an unenforced reference is prose that rots the moment somebody
renames a test.

## The Architecture Tree

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, how does it work? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none, and the `architecture-update` agent prunes those
that stop
earning their place.

## Repository Layout

- **`agents/`** — the drop-in payload: `AGENTS.md`, `.github/agents/`, `.github/skills/`,
  `.github/standards/`
- **`template/`** — the canonical repository layout and file templates
- **`docs/user-guide/`** — how to use and maintain this process

## Installation

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This copies the `agents/` payload to the target repository root and vendors `template/` to
`.github/template/`. The vendored copy matters: `AGENTS.md` resolves the template from there first,
and it pins the template to the agent versions installed beside it. Then open `AGENTS.md` and replace
the `TODO` placeholders in the **Project Overview** section.

For a new repository, run `@architecture-design` to interview and generate the architecture tree, or
`@template-sync Scaffold` to lay down the structure from the template.

The payload installs seven agents — `evolve` is the entry point for any non-trivial change — and the
standards they load. [Reference](docs/user-guide/reference.md) describes each one.

## License

[MIT](LICENSE)

[^1]: `Anneal` is a sibling to [Agents](https://github.com/demaconsulting/Agents), which targets
    IEC 62304 regulated development. Use `Agents` where compliance evidence is required.
