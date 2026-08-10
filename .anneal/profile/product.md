# Product

What Anneal is, who it is for, and how its parts work together — the level-0 pitch this repository's
own `README.md` gives today, kept here as the file an LLM/oracle prompt injects when it needs Anneal's
purpose and shape rather than the full README's installation and licensing detail.

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope, the same mechanism `README.md` carries today.

## What it is

A development process for AI coding agents working in long-lived .NET codebases.

Anneal installs into a repository as a set of agent prompts, coding standards, a repository
template, and a command-line tool. From then on you work by asking an agent for what you want, and
the process decides how much rigor the request deserves — from none at all for a change nobody
outside the code can observe, up to a staged, approved restructure when the architecture itself is
the thing that is wrong. It is aimed at .NET and C# products that will be maintained for years by a
mix of people and agents.

The mechanism is a single rule: **documentation work is triggered only when you change a promise
other code depends on.** The interior of a system is therefore free to rearrange as often as needed
at no documentation cost. Those promises live in one file per system, and every one of them names a
test that proves it — so the build fails the moment a promise loses its proof. Agents work inside a
scope they declare before they start, and reaching the edge of it stops them and returns a report.

That places Anneal between two unsatisfying options: unstructured prompting, which is quick until the
design ossifies and nobody can say what anything still guarantees, and regulated development, which
buys traceability at a price paid on every subsequent change. The name is the metaphor: annealing
relieves the stress that repeated working builds up in metal, so it can be shaped again.

## Features

- **Refactor without paperwork.** Rearrange the inside of a system as much as you like.
  Documentation costs you something only when you change a promise other code depends on.
- **Every promise is backed by a test.** Each clause of a contract names a test, and the build fails
  if that test is missing, renamed, or last seen failing.
- **Process sized to the change.** Every task is classified before work starts, and the common case —
  a change no other code can observe — carries no documentation step at all.
- **Agents stop instead of improvising.** An agent declares what it will touch before it touches it.
  Reaching that boundary is a stop and a report back, never a decision to widen it.
- **Filing a need costs one line.** A standing property the product must always satisfy goes into
  `../work/constraints.md`, where the next design review reads it; work that finishes goes into
  `../work/backlog.md`; a belief the design rests on goes into `../governance/assumptions.md`. No
  code, no tests, no contract.
- **Tidying is a first-class activity.** Background quality work has its own mode, with a declared
  scope and a stopping point, so it cannot drift into a redesign.
- **Restructuring is a defined operation.** Reshaping the architecture proceeds in steps that are
  approved, rather than one enormous commit or a branch that lives for months.
- **The reasoning survives.** Why a system promises what it promises is recorded beside the promise,
  so a new developer — or a new agent — can recover it without a parallel tree of design documents to
  keep in sync.
- **One command to install**, from a clone checked out at the revision wanted.

**One of these is enforced by a machine; the rest are instructions.** The clause-to-test link fails
the build, and a fixture suite holds that check to its own documented behavior. Everything else above
is a rule agents are told to follow, carried by prompt and review rather than by tooling;
[`overview.md`](../architecture/overview.md) carries the full enforcement account.

## How It Works

Anneal is a small set of parts installed into a repository, which work together to keep the code and
the documents describing it in agreement.

**The parts:**

- **Agents** (`.github/agents/`) — the workers. Two are conversational and are invoked directly:
  `helper`, which takes a request in ordinary words and routes it, and `architecture-design`, which
  establishes system boundaries by interview. The other two are invoked by the process rather than
  directly: `dispatch` classifies the work and runs it through the compiled toolkit's `route`,
  `maintain`, and `stage-contract` actions — including staging a contract clause ahead of
  implementation, when explicitly asked to — and `template-sync` keeps the repository aligned with
  the template. Verifying a finished change is itself a compiled toolkit action, `verify-change`, run
  directly rather than through an agent.
- **Standards** — the rules the agents work to, one subject per file, each the sole owner of its
  subject: coding principles, testing principles, system contracts, architecture and technical
  documentation, and change classification, plus the ecosystem-specific conventions named in
  `conventions.md`. An agent loads the two or three relevant to the files in front of it, not all of
  them.
- **Skills** — procedures loaded only when the situation arises, so a rarely needed recipe costs
  nothing the rest of the time.
- **Scripts** (repository root) — `build.ps1`, `lint.ps1`, `fix.ps1`; see `validation.md`.
- **A command-line tool** (`dotnet anneal`) — the checks that need real analysis rather than pattern
  matching, packaged so they run identically on a laptop and in CI.

**What steers them** is this folder's own governance and profile documents, plus
[`../architecture/`](../architecture/) — the systems the product is divided into and what each
publishes to the others, in progressively finer detail — and the agents read and maintain these
rather than carrying the knowledge in their prompts.

**How the parts meet.** A request reaches an agent, which reads only as far down that documentation
as the work requires and loads only the standards that apply. It declares what it will touch, works
inside that edge, and stops rather than widening it. A *different* agent then checks the result
against the documents, fetching its own diff instead of trusting the first agent's account of it.
Where a change alters something a system publishes, the document is edited before the code.

One relationship in all of this is enforced by machine rather than judgement: every promise a system
publishes must name a test that exists, sits on that system's boundary, and passed. The build fails
if any promise cannot show one, and fails closed — a clause it cannot understand is an error, never
a silent skip. Everything else is carried by prompt and review, deliberately.

[`../architecture/overview.md`](../architecture/overview.md) names the systems, says how they
interact, and links to what each one promises. It is the next stop for any detail below this
altitude.

## The Architecture Tree

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` / `product.md` | 50,000 ft | What is this product, what does it give me, how does it work? |
| 1 | `../architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `../architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `../architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

Level 3 is exceptional. Most systems have none, and the pass authoring a contract change prunes those
that stop earning their place.
