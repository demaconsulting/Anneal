# Anneal

**A development process for AI coding agents working in long-lived .NET codebases.**

Anneal installs into your repository as a set of agent prompts, coding standards, a repository
template, and a command-line tool. From then on you work by asking an agent for what you want, and
the process decides how much rigor the request deserves — from none at all for a change nobody
outside the code can observe, up to a staged, approved restructure when the architecture itself is the
thing that is wrong. It is aimed at .NET and C# products that will be maintained for years by a mix of
people and agents.

The mechanism is a single rule: **documentation work is triggered only when you change a promise
other code depends on.** The interior of a system is therefore yours to rearrange as often as you
like at no documentation cost. Those promises live in one file per system, and every one of them
names a test that proves it — so the build fails the moment a promise loses its proof. Agents work
inside a scope they declare before they start, and reaching the edge of it stops them and returns a
report to you.

That places Anneal between two unsatisfying options: unstructured prompting, which is quick until
the design ossifies and nobody can say what anything still guarantees, and regulated development,
which buys traceability at a price paid on every subsequent change. The name is the metaphor:
annealing relieves the stress that repeated working builds up in metal, so it can be shaped again.

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

**One of these is enforced by a machine; the rest are instructions.** The clause-to-test link fails
the build, and a fixture suite holds that check to its own documented behavior. Everything else above
is a rule agents are told to follow, carried by prompt and review rather than by tooling;
[`overview.md`](docs/architecture/overview.md) carries the full enforcement account.

## How It Works

Anneal is a small set of parts installed into your repository, which work together to keep the code
and the documents describing it in agreement.

**The parts:**

- **Agents** (`.github/agents/`) — the workers. Two are conversational and you invoke them yourself:
  `helper`, which takes a request in ordinary words and routes it, and `architecture-design`, which
  establishes system boundaries by interview. The other five are invoked by the process rather than by
  you: `dispatch` classifies the work, `apply` implements it, `architecture-update` moves the
  architecture documents with it, `scope-check` verifies the finished change, and `template-sync` keeps
  the repository aligned with the template.
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

**What steers them** is four documents in your repository, and the agents read and maintain these
rather than carrying the knowledge in their prompts:

- **`README.md`** — what the product is, who it is for, and what it promises. The entry point.
- **`CONSTRAINTS.md`** — what the product must respect regardless of what anyone asks for.
- **`BACKLOG.md`** — work identified but not yet scheduled, so a good idea raised at a bad moment is
  recorded instead of built.
- **`docs/architecture/`** — the systems the product is divided into and what each publishes to the
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
[`docs/architecture/overview.md`](docs/architecture/overview.md) names them, says how they interact,
and links to what each one promises. It is the next stop for any detail below this altitude.

## Direction

Anneal has a settled destination: it becomes its own agent CLI. Work arrives at any point on the
complexity spectrum, a router classifies it and selects one of a catalog of processes, and each
process runs as compiled state-flow logic — models do the work, and oracles, meaning narrow typed
questions with no side effects, decide its branches. The prose agents under `.github/agents/` are
the bootstrap harness that made this reachable, not the product; they are dismantled into that
catalog. `helper` and `architecture-design` are absorbed **last**, because a conversation is the
hardest control flow to encode — not because they are exempt. Along the way, Anneal takes on the
capabilities of a separate, earlier autonomous-coding project built under a rigid regulated process
that could not evolve, and replaces it.

Routing is what makes that catalog affordable. A planning-and-review process that runs on every
change multiplies the cost of every change, which is exactly the mechanism this repository refuses;
the same process run only on work that earns it is proportionality, not overhead. That is the same
principle progressive disclosure and scoped effort already apply — read only as deep as the task needs,
document only as much as the contract moved, run only as heavy a process as the work warrants.

**The dividing line.** The Toolkit may absorb **control flow and context assembly** — sequencing
steps, gating on their outcomes, and composing what a model is shown. It must never absorb
**judgement as compiled behavior**. The agent prompt files under `.github/agents/` are bootstrap
scaffolding and compile away with the rest of the control flow they once encoded by hand; what stays
data is the *content* a compiled step composes into what a model sees — standards, and a repository's
own declared contracts — because those are corrected in one edit, where a wrong compiled rule is
corrected only through build, test, publish and restore. Whether that content stays a plain file or
becomes a packaged resource is a delivery detail still open (see `MIGRATION.md`); a repository's own
contracts cannot become one, because they are a fact about that installation, not shared behavior.

The admission test underneath is the one *What must not be reintroduced* in
[overview.md](docs/architecture/overview.md) turns on: does a mechanism add cost paid on every
subsequent change? Anneal exists to refuse mechanisms that do. Automation that mechanizes work in
order to *remove* per-change cost is the point of this direction, not a case against it.

One further item is held at lower confidence than the rest, and named here because it shapes
thinking below this line without being committed: an on-premises model provider. It would be
re-decided when a stage that depends on it is approached.

How the journey is run is not part of this direction and is deliberately not scheduled here.
[MIGRATION.md](MIGRATION.md) owns it, and plans one stage at a time.

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
the layout, then `@architecture-design` to interview you and generate the architecture tree.

The payload installs eight agents and the standards they load. You invoke two: `@helper` is the front
door for anything you want done, and `@architecture-design` interviews you when system boundaries
need establishing or re-cutting. The other six run as sub-agents.
[Process](docs/architecture/process.md) describes the full composition.

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
- **An agent that must justify its answer is more reliable than one that merely states it.**
  Separating incentives removes the motive to approve but does not oblige a judge to derive its
  verdict. If reasoning-required agents proved no more accurate than agents asked only for a
  conclusion, the judging layer would be ceremony.
- **The prompt files are the reliability mechanism.** Reliability follows from the quality of the
  facts and the clarity of the question, so a defect in an agent prompt degrades every downstream
  agent's facts. Prompt changes are the highest-risk changes in this repository.
- **Products adopting this process are .NET and C#.** The shipped layout defaults to `*.cs` sources,
  xUnit attributes and TRX results, and the template's scripts assume a solution. The process itself
  is language-neutral; the repository it hands you is not. Adoption for another ecosystem would mean
  the template is the wrong shape, not a defect to patch. The
  [Toolkit](docs/architecture/toolkit.md) hardens this into a dependency: it ships as a .NET tool, so
  a repository outside that ecosystem can read the process but not run its operations.
- **Structural properties of a prompt predict how an agent behaves.** Checking references resolve,
  every result value is handled, and the context budget holds is worth doing because those
  properties correlate with reliable behavior. If they don't, the mechanical contract is theater, and
  verification would have to move wholesale to inspection and sandbox runs.
- **Where a response schema appears in a conversation changes how reliably it is followed** — a
  schema given after the reasoning is done is followed more closely than one given at the outset.
  This is the belief the [Toolkit](docs/architecture/toolkit.md) exists to exploit; see there for why.
- **A described schema is enough without constrained decoding.** The Copilot session API has no
  response-format facility, so a typed answer rests on a schema described in the prompt and a retry
  on parse failure. If failures survive the retry budget often enough to matter — measured at stage
  S1b of [MIGRATION.md](MIGRATION.md) — typed probes need a provider that enforces the shape on the
  wire.
- **The build now requires network access**, to fetch the Copilot CLI the SDK depends on. Build-time
  only — no enforcement operation's runtime determinism is affected. See
  [Toolkit](docs/architecture/toolkit.md) for the mechanism.

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
- **`src/`, `test/`, `Anneal.slnx`** — the Toolkit, a .NET tool hosting operations that combine
  deterministic checks with model-backed judgement
- **`.anneal/`** — repository-local runtime configuration the Toolkit resolves: role-to-model
  mapping, and the arguments a self-hosted run's contract check is invoked with
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

- **[Architecture Overview](docs/architecture/overview.md)** — the systems Anneal is built from
- **[User Guide](docs/user-guide/README.md)** — installing, first run, and day-to-day usage

## License

[MIT](LICENSE)

[^1]: `Anneal` is a sibling to [Agents](https://github.com/demaconsulting/Agents), which targets
      IEC 62304 regulated development. Use `Agents` where compliance evidence is required.
