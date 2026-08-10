# Anneal

**A development process for AI coding agents working in long-lived source repositories.**

Anneal installs into your repository as a set of agent prompts, coding standards, a repository
template, and a command-line tool. From then on you work by asking an agent for what you want, and
the process decides how much rigor the request deserves — from none at all for a change nobody
outside the code can observe, up to a staged, approved restructure when the architecture itself is the
thing that is wrong. It is aimed at codebases that will be maintained for years by a mix of
people and agents; today's shipped template covers .NET and C# only.

The mechanism is a single rule: **documentation work is triggered only when you change a promise
other code depends on.** The interior of a system is therefore yours to rearrange as often as you
like at no documentation cost. Those promises live in one file per system, and every one of them
names a test that proves it — so the build fails the moment a promise loses its proof. Agents work
inside a scope they declare before they start, and reaching the edge of it stops them and returns a
report to you.

The name is the metaphor: annealing relieves the stress that repeated working builds up in metal, so
it can be shaped again.

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
  `.anneal/work/constraints.md`, where the next design review reads it; work that finishes goes into
  `.anneal/work/backlog.md`; a belief the design rests on goes into `.anneal/governance/assumptions.md`.
  No code, no tests, no contract.
- **Tidying is a first-class activity.** Background quality work has its own mode, with a declared
  scope and a stopping point, so it cannot drift into a redesign.
- **Restructuring is a defined operation.** Reshaping the architecture proceeds in steps you approve,
  rather than one enormous commit or a branch that lives for months.
- **The reasoning survives.** Why a system promises what it promises is recorded beside the
  promise, so a new developer — or a new agent — can recover it without a parallel tree of design
  documents to keep in sync.
- **One command to install**, from a clone you check out at the revision you want.

**One of these is enforced by a machine; the rest are instructions.** The clause-to-test link fails
the build, and a fixture suite holds that check to its own documented behavior. Everything else above
is a rule agents are told to follow, carried by prompt and review rather than by tooling;
[`overview.md`](.anneal/architecture/overview.md) carries the full enforcement account.

## How It Works

Anneal is a small set of parts installed into your repository, which work together to keep the code
and the documents describing it in agreement.

**The parts:**

- **Agents** (`.github/agents/`) — the workers. One is conversational and you invoke it yourself:
  `helper`, which takes a request in ordinary words, classifies its mode, invokes the minimum
  compiled toolkit action directly, and handles boundary interviews when the repository needs its
  first architecture tree or a re-cut. `template-sync` is invoked by the process rather than by you,
  keeping the repository aligned with the template. Verifying a finished change is itself a compiled
  toolkit action, `verify-change`, run directly rather than through an agent.
- **Standards** (`.github/standards/`) — the rules the agents work to, one subject per file, each the
  sole owner of its subject: coding principles and C# language, testing principles and C# testing,
  system contracts, architecture and technical documentation, and change classification. An agent
  loads the two or three relevant to the files in front of it, not all of them.
- **Skills** (`.github/skills/`) — procedures loaded only when the situation arises, so a rarely
  needed recipe costs nothing the rest of the time.
- **Scripts** (repository root) — `build.ps1`, `lint.ps1`, `fix.ps1`. These are what CI runs, so the
  checks apply whether a human or an agent made the change.
- **A command-line tool** (`dotnet anneal`) — the checks that need real analysis rather than pattern
  matching, packaged so they run identically on a laptop and in CI.

**What steers them** is a set of documents under `.anneal/`, and the agents read and maintain these
rather than carrying the knowledge in their prompts:

- **`README.md`** — what the product is, who it is for, and what it promises. The entry point.
- **`.anneal/governance/`** — tenets, assumptions, and the long-term vision: what the product must
  respect, what it takes on faith, and where it is headed.
- **`.anneal/work/constraints.md`** — standing properties the product must respect regardless of what
  anyone asks for.
- **`.anneal/work/backlog.md`** — work identified but not yet scheduled, so a good idea raised at a bad
  moment is recorded instead of built.
- **`.anneal/architecture/`** — the systems the product is divided into and what each publishes to the
  others, in progressively finer detail.

**How the parts meet.** A request reaches an agent, which reads only as far down that documentation
as the work requires and loads only the standards that apply. It declares what it will touch, works
inside that edge, and stops rather than widening it. A *different* agent then checks the result
against the documents, fetching its own diff instead of trusting the first agent's account of it.
Where a change alters something a system publishes, the document is edited before the code.

One relationship in all of this is enforced by machine rather than judgement: every promise a system
publishes must name a test that exists, sits on that system's boundary, and passed. The build fails
if any promise cannot show one, and fails closed — a clause it cannot understand is an error, never
a silent skip. Everything else is carried by prompt and review, deliberately.

Those parts divide into systems, and
[`.anneal/architecture/overview.md`](.anneal/architecture/overview.md) names them, says how they
interact, and links to what each one promises. It is the next stop for any detail below this
altitude.

## Direction

Anneal's long-term vision, and the load-bearing beliefs it rests on, live in
[`.anneal/governance/vision.md`](.anneal/governance/vision.md) and
[`.anneal/governance/assumptions.md`](.anneal/governance/assumptions.md) — read those rather than a
summary here, so there is exactly one place either can be revised.

## Installation

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This copies the payload into the target repository and vendors the template to `.github/template/`.
The vendored copy matters: `AGENTS.md` resolves the template from there first, which pins it to the
agent versions installed beside it.

There is nothing to fill in afterwards. `AGENTS.md` holds no project-specific values — what your
product is and what it is written in belong in your own `README.md`, which agents read as level 0 of
the architecture tree — so an upgrade can replace it outright with `-Force`.

For a new repository, run `@helper scaffold the repository structure from the template` to lay down
the layout, then ask `@helper` to interview you and generate the architecture tree.

The payload installs two agent prompts and the standards they load. You invoke one: `@helper` is
the front door for anything you want done, including the boundary interview when system boundaries
need establishing or re-cutting. `template-sync` runs only when layout-versus-template work is the
thing being done.
[Process](.anneal/architecture/process.md) describes the full composition.

## Assumptions

The load-bearing beliefs this design rests on — what would have to hold for the architecture to be
the right shape — live in
[`.anneal/governance/assumptions.md`](.anneal/governance/assumptions.md). A disproved assumption is a
re-cut trigger, not a bug.

## The Architecture Tree

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, how does it work? |
| 1 | `.anneal/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `.anneal/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `.anneal/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none, and the pass authoring a contract change prunes those
that stop earning their place.

## Repository Layout

This repository is laid out exactly as a repository that has installed Anneal, so the process can be
maintained using its own agents.

- **`.github/agents/`, `.github/skills/`, `.github/standards/`** — the payload, live here and shipped
  unchanged
- **`.github/template/`** — the canonical repository layout and file templates, including the
  pristine `AGENTS.md`
- **`docs/user-guide/`** — how to use and maintain this process
- **`docs/template/`** — shared Pandoc inputs: HTML template and the collection link filter
- **`docs/build-doc.ps1`** — compiles one document collection into HTML and then PDF
- **`src/`, `test/`, `Anneal.slnx`** — the Toolkit, a .NET tool hosting operations that combine
  deterministic checks with model-backed judgement
- **`.anneal/`** — the sole authoritative source for Anneal's own governance, product profile,
  work-tracking, and architecture documents (see [Layout](.anneal/profile/layout.md) for the full
  breakdown), plus repository-local runtime configuration the Toolkit resolves (role-to-model
  mapping, the arguments a self-hosted run's contract check is invoked with), `skills/`, where
  `file-skill` writes deliberately curated, committed lessons about this repository (see
  [Skills](.anneal/architecture/toolkit/skills.md)), and `logs/`, where invocation and
  model-interaction records are written
- **`test-process-contract.ps1`** — a fixture suite holding the payload itself to its documented
  behavior; `dotnet anneal check-contracts` is held to its own contract by
  `CheckContractsSubprocessTests` under `test/`, a compiled C# suite that spawns the tool as a
  real subprocess
- **`.agent-logs/`** — agent report corpus (gitignored, local only); `AGENTS.md` already requires
  every agent to write a report here, making the corpus automatic; `agent-metrics.ps1` harvests it
  into a bounded behavioral summary

## Technology

- **Languages** — PowerShell and C#, with Markdown and YAML as the primary content
- **Platform** — PowerShell 7 and the .NET SDK; Node and Python supply the lint tooling
- **Model access** — the GitHub Copilot SDK, under the ambient account of the calling session

To work on Anneal itself (not a repository that has installed it): `pwsh ./fix.ps1`, then
`pwsh ./build.ps1`, then `pwsh ./lint.ps1`. The order matters — `build.ps1` runs every test suite and
records the results that `lint.ps1` reads when it checks that each promise still names a passing test.

## Documentation

- **[Architecture Overview](.anneal/architecture/overview.md)** — the systems Anneal is built from
- **[User Guide](docs/user-guide/README.md)** — installing, first run, and day-to-day usage

## License

[MIT](LICENSE)

[^1]: `Anneal` is a sibling to [Agents](https://github.com/demaconsulting/Agents), which targets
      IEC 62304 regulated development. Use `Agents` where compliance evidence is required.
