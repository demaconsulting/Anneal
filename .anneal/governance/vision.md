# Vision

Anneal's long-term strategic destination, held at varying confidence, and the reasoning behind it —
distinct from `tenets.md` (what Anneal already, unconditionally chooses to be) in that this file
describes where the project is heading and how sure it is, not a settled identity.

Owner-authored, owner-approved to change, same gate as `tenets.md` and `assumptions.md`. Anneal may
propose a revision but never edits this file unilaterally.

Anneal has a settled destination: it becomes its own agent CLI. Work arrives at any point on the
complexity spectrum, a router classifies it and selects one of a catalog of processes, and each
process runs as compiled state-flow logic — models do the work, and oracles, meaning narrow typed
questions with no side effects, decide its branches. The prose agents under `.github/agents/` are
the bootstrap harness that made this reachable, not the product; they are dismantled into that
catalog. `helper` and `architecture-design` are absorbed **last**, because a conversation is the
hardest control flow to encode — not because they are exempt. Along the way, Anneal takes on the
capabilities of a separate, earlier autonomous-coding project built under a rigid regulated process
that could not evolve, and replaces it.

Once absorbed, the catalog is not only reactive. Work does not have to arrive from outside — the
same catalog that processes a request can originate one, proposing a Maintenance sweep, an
architectural review, or a documentation pass from its own inspection of the repository, on the same
terms as anything a person asks for. Origination does not relax the terms; whether a change is safe
to make depends on whether it can reach `main` only through the ordinary route of branch, review, and
test, never on whether a person or the catalog itself proposed it — reversibility is the guard, not
authorship. The one place that guard is insufficient is a change that leaves version control's own
blast radius: a published release, an install into another repository, or a tool grant with a
real-world effect outside this repository. Those keep asking for a person, not because judgement
elsewhere is untrusted, but because nothing short of a person can be rolled back.

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
becomes a packaged resource is a delivery detail still open (see `.anneal/work/active-plan.md`); a repository's own
contracts cannot become one, because they are a fact about that installation, not shared behavior.

The admission test underneath is the one *What must not be reintroduced* in
[overview.md](../architecture/overview.md) turns on: does a mechanism add cost paid on every
subsequent change? Anneal exists to refuse mechanisms that do. Automation that mechanizes work in
order to *remove* per-change cost is the point of this direction, not a case against it.

One further item is held at lower confidence than the rest, and named here because it shapes
thinking below this line without being committed: an on-premises model provider. It would be
re-decided when a stage that depends on it is approached.

A further item is held at the same low confidence: whether the catalog eventually chooses its own
forward direction, rather than a person choosing it — not proposing a bounded sweep within a category
already named above, but selecting which capability to build next. This is not committed here. It is
conditional on first demonstrating, at the narrower scope above, that self-originated work stays
reliably positive under long-term unattended maintenance and planning; that scope has to be earned
and observed before this one is even re-decided, let alone granted.

How the journey is run is not part of this direction and is deliberately not scheduled here.
[active-plan.md](../work/active-plan.md) owns it, and plans one stage at a time. It was
`MIGRATION.md` at the repository root through stage S20; S21 relocated it here without changing its
content.
