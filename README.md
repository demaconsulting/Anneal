# Anneal

Agent definitions, standards, and a repository template for **evolutionary** software development —
designs that must keep moving after the first version ships.

Repeated working makes metal brittle; annealing relieves the accumulated stress so it can be shaped
again. Architectures stiffen the same way under accumulated change. This process is built to keep
them workable, and to restructure them deliberately when they are not.

Anneal gives AI coding agents enough structure to produce maintainable, reviewable software without
the inertia of a formal process. It sits between unstructured prompting and regulated development.

**The idea, in one paragraph.** A repository is divided into **systems** — parts that could be
replaced whole without the rest noticing. A system's **contract** is what other code is allowed to
depend on, the promises it publishes at its boundary. Documentation work is triggered by a change to
a contract, and by nothing else. Rewrite a system's internals however you like and you owe no
documentation; change what it promises and you edit one file, where every promise names the test
that proves it. Agents classify a task against that rule before starting, and the classification
decides both how much process the task gets and what the agent is permitted to touch.

> **Not a regulated-development process.** `Anneal` does not produce IEC 62304 or equivalent
> compliance evidence.[^1]

## Features

- **Refactor without paperwork.** Rearrange the inside of a system as much as you like.
  Documentation costs you something only when you change a promise other code depends on.
- **Every promise is backed by a test.** Each clause of a contract names a test, and the build fails
  if that test is missing, renamed, or last seen failing.
- **Process sized to the change.** Every task is classified before work starts, and the common case —
  a change no other code can observe — carries no documentation step at all.
- **Agents stop instead of improvising.** An agent declares what it will touch before it touches it.
  Reaching that boundary is a stop and a report back to you, never a decision to widen it.
- **Filing a need costs one line.** A standing property the product must always satisfy goes into
  `CONSTRAINTS.md`, where the next design review reads it; work that finishes goes into `BACKLOG.md`;
  a belief the design rests on goes into this file's Assumptions. No code, no tests, no contract.
- **Tidying is a first-class activity.** Background quality work has its own mode, with a declared
  scope and a stopping point, so it cannot drift into a redesign.
- **Restructuring is a defined operation.** Reshaping the architecture proceeds in steps you approve,
  rather than one enormous commit or a branch that lives for months.
- **The reasoning survives.** Why a system promises what it promises is recorded beside the
  promise, so a new developer — or a new agent — can recover it without a parallel tree of design
  documents to keep in sync.
- **One command to install**, from a clone you check out at the revision you want.

**One of these is enforced by a machine; the rest are instructions.** `check-contracts.ps1` fails the
build when a contract clause names no test, or names a test that is missing, stale, or failing — and
`test-check-contracts.ps1` holds it to that, one fixture repository per documented failure.
Everything else above is a rule agents are told to follow, held by prompt and review rather than by
tooling.

## What It Costs

Every system boundary needs its promises written down, and every promise needs a named test —
that is the adoption cost on an existing codebase, and it is real. Restructures need your approval at
each step, so the process cannot run unattended on the work that matters most. And the trade itself —
a small cost per contract change in exchange for none per file change — only pays on a design that is
still moving.

Several things are deliberately absent: per-unit requirements, per-unit and per-subsystem design
documents, verification design documents, an architecture model, formal review tracking, and
multi-retry orchestration. Each was left out because its cost is paid on **every** subsequent change.
[Architecture Overview](docs/architecture/overview.md) has the full rationale, the trade-offs, and
what must not be reintroduced.

## Requirements

What the process must hold true to deliver the features above, stated so you can check a repository
against them rather than argue about them. Each says something no feature bullet already says:

- Every kind of work a product receives has exactly **one** defined entry point.
- How much process a task needs is decided from a **single** definition of classification, used by
  every agent — so no two agents can hold different ideas of what a tier means.
- A large restructure can proceed in stages without disabling any check.
- The standards an agent must load for any one task are typically two and never more than four.
- Nothing below system level is documented as a requirement, design, or verification artifact.

## How It Works

The process rests on one rule: **documentation work is triggered by a change to what other code
depends on, never by a change to a file.** Each system publishes a contract — the promises consumers
outside it may rely on. Everything below that boundary is free to change without documentation cost.
That freedom is the point; it is what lets a design keep moving after the first version ships.

Work is classified twice before it starts. **Mode** — recording an idea, making a change, tidying,
or migrating — decides what an agent is allowed to touch. **Tier** decides how far a change reaches
into published contracts, and therefore how much documentation moves with it. The two are
independent, and most work lands on the cheapest combination of both.

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

Judgement that cannot be reduced to a script is delegated to an **oracle agent** — one invoked for a
single question, given first-hand facts, and permitted to disagree. Is this change observable at its
boundary? Did the implementation honour the tier it declared? `tier-check` answers the second by
obtaining its own `git diff` and reading the contract, rather than trusting the summary of the agent
it is checking. These are checks rather than steps: they exist to disagree, and the repair budget
exists to spend on their disagreement.

## Assumptions

What this design takes to be true and cannot itself guarantee. If one of these stops holding, the
architecture resting on it is the wrong shape — so they are stated here rather than left implicit in
the agent prompts. A disproved assumption is a re-cut trigger, not a bug.

- **A focused agent is a reliable judge.** An agent given the specific facts and a single clear
  question answers it reliably. Reliability degrades with breadth and vagueness far more than with
  difficulty. This is why judgement is split into separate single-question invocations instead of
  being asked as one part of a larger job.
- **Judging and doing have different incentives.** An agent asked to complete work is under pressure
  to call the work done. An agent asked only to judge is not. Classification and verification are
  therefore never performed by the agent that did the work.
- **Correlated error is the residual risk.** Separate invocations of the same model can share a blind
  spot; independence of incentive is not independence of judgement. A judging agent is therefore
  given first-hand facts rather than the working agent's summary of them.
- **An agent that must justify its answer is more reliable than one that merely states it.** Separating
  incentives removes the motive to approve, but it does not oblige a judging agent to derive its verdict,
  and an unobliged judge can ratify a plausible impression it never checked. If agents required to show
  their reasoning proved no more accurate than agents asked only for a conclusion, the judging layer would
  be ceremony and reliability would have to be sought outside the prompts.
- **The prompt files are the reliability mechanism.** Because reliability follows from the quality of
  the facts and the clarity of the question, a defect in an agent prompt degrades the facts every
  downstream agent works from. Prompt changes are the highest-risk changes in this repository.
- **Products adopting this process are .NET and C#.** The shipped layout defaults to `*.cs` sources,
  xUnit attributes and TRX results, and the template's build and lint scripts assume a solution. The
  process itself is language-neutral, but the repository it hands you is not. Adoption for another
  ecosystem would not be a defect to patch — it would mean the template is the wrong shape.
- **Structural properties of a prompt predict how an agent behaves.** Checking that references resolve,
  that every result value is handled, and that the context budget holds is worth doing because those
  properties correlate with reliable behavior. If they turn out not to, a mechanical contract over the
  payload is theater: it would pass while agents still misbehaved, and verification would have to move
  wholesale to inspection and sandbox runs.

## The Architecture Tree

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, how does it work? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none, and the `architecture-update` agent prunes those that
stop earning their place.

## Repository Layout

This repository is laid out exactly as a repository that has installed Anneal, so the process can be
maintained using its own agents.

- **`.github/agents/`, `.github/skills/`, `.github/standards/`** — the payload, live here and shipped
  unchanged
- **`.github/template/`** — the canonical repository layout and file templates, including the
  pristine `AGENTS.md`
- **`docs/architecture/`** — Anneal's own architecture tree, maintained with its own agents
- **`docs/user-guide/`** — how to use and maintain this process
- **`docs/template/`** — shared Pandoc inputs: HTML template and the collection link filter
- **`docs/build-doc.ps1`** — compiles one document collection into HTML and then PDF
- **`test-check-contracts.ps1`** — fixture suite proving `check-contracts.ps1` still fails in every
  way its skill file documents

## Technology

- **Languages** — PowerShell, with Markdown and YAML as the primary content
- **Platform** — PowerShell 7; Node and Python supply the lint tooling

To work on Anneal itself (not a repository that has installed it): `pwsh ./fix.ps1`, then
`pwsh ./test-check-contracts.ps1`, then `pwsh ./lint.ps1`. The order matters — `lint.ps1` reads
results that `test-check-contracts.ps1` writes.

## Documentation

- **[Architecture Overview](docs/architecture/overview.md)** — the systems Anneal is built from
- **[User Guide](docs/user-guide/README.md)** — installing, first run, and day-to-day usage

## Installation

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This copies the payload to the target repository and vendors the template to `.github/template/`. The
vendored copy matters: `AGENTS.md` resolves the template from there first, and it pins the template to
the agent versions installed beside it.

There is nothing to fill in afterwards. `AGENTS.md` holds no project-specific values — what your
product is and what it is written in belong in `README.md`, which agents read as level 0 of the
architecture tree — so an upgrade can replace it outright with `-Force`.

For a new repository, run `@helper scaffold the repository structure from the template` to lay down
the layout, then `@architecture-design` to interview and generate the architecture tree.

The payload installs eight agents and the standards they load. You invoke two of them: `@helper` is
the front door for everything you want done, and `@architecture-design` interviews you when system
boundaries need establishing or re-cutting. The other six run as sub-agents, so there are no trigger
words to learn. [Process](docs/architecture/process.md) describes the full composition.

## License

[MIT](LICENSE)

[^1]: `Anneal` is a sibling to [Agents](https://github.com/demaconsulting/Agents), which targets
    IEC 62304 regulated development. Use `Agents` where compliance evidence is required.
