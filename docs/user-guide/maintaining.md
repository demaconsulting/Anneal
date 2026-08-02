# Maintaining

How this repository is put together, which decisions are load-bearing, and how to change it without
undoing the thing it was built to fix.

## Why This Exists

The [Agents](https://github.com/demaconsulting/Agents) process produces IEC 62304 compliant software
quickly from an architecture. It does that by cross-referencing requirements, design, verification,
code, and tests at every level of the software item hierarchy.

That cross-referencing is exactly what makes a design expensive to change afterwards. Encoding the
initial idea into five artifacts per unit gives the design enormous inertia: a change that touches
several units drags a documentation project behind it, and agent runs of 80 to 240 minutes were
common as the process fought that inertia.

Anneal targets software that must keep **evolving** after the first version ships. It is not a
replacement for `Agents`, and it does not produce compliance evidence.

## Where the Cost Actually Was

Worth being precise, because it determines what must never be reintroduced:

**Per-unit artifact fan-out** — `software-items.md` mandated five artifacts per software item, down
to individual classes. Cost scaled with the number of items touched, so every refactor rewrote or
renamed a pile of files.

**Per-unit requirements** — `requirements-principles.md` mandated a requirements file per subsystem
**and** unit. Same fan-out, plus identifier churn every time something moved.

**Hard-fail companion gates** — `quality.agent.md` failed the build when any companion artifact was
missing, turning every omission into a full retry cycle.

**Multi-retry orchestration** — `implementation.agent.md` ran PLANNING → DEVELOPMENT → QUALITY with
three retries, multiplying everything above by up to four.

Notably, **ReqStream was not the problem**. Its clause-to-test matching was the one deterministic,
cheap, reliable check in the whole process. The expensive part was hand-written prose fan-out policed
by an LLM. Do not confuse the two when trimming further.

## Load-Bearing Decisions

These are the decisions the process rests on. Changing one is a redesign, not a tweak. Each is
recorded with the alternative that was rejected, so it is not re-litigated from scratch.

**Classification is defined in exactly one file.** `change-classification.md` is the sole definition
of both modes and tiers; `AGENTS.md`, the agent prompts, and the README link to it and never restate
it.
Rejected: a convenient summary in each place an agent might look. The summaries drift — this process
carried tier definitions in three files for months, and they had already disagreed with each other
about whether a bug fix was Tier 0 before anyone noticed. A rule stated twice is a rule whose meaning
depends on which copy an agent read first.

**Work is classified by mode as well as tier.** Tier measures only how far a change reaches into
published contracts, which is the wrong question for work that is not a change. Rejected: adding
more tiers. Recording an idea, tidying, and migrating differ from an ordinary change in *kind* — in
what may be touched and what "done" means — not in degree, so they are a second, orthogonal axis.
The tier ladder stops at 2 deliberately; anything that felt like "Tier 3" was really Migration mode.

**Anneal has no architecture tree of its own.** Its README is level 0 and there is no
`docs/architecture/`. Rejected: applying the full tree to itself for its own sake. Most of what would
go in an `overview.md` here is *normative* content that belongs in a standard — the mode and tier
tables are rules agents follow, not descriptions of structure — and once relocated, the remaining
inventory is already covered by `AGENTS.md` and `.github/template/repository-map.md`. Creating a level
nothing descends into is the premature-boundary failure this process rejects elsewhere. This decision
expires when any of the following becomes true:

- A second scaffold appears beside the .NET template — that is a genuine second system.
- Agent count grows past roughly ten, or two standards begin contradicting each other again.
- `AGENTS.md` grows past the point where an agent reliably reads all of it.

**Documentation is triggered by contract change, not file change.** Rejected: mandatory documentation
updates on any file change, as in `Agents`. That mandate is what converts refactoring into a
documentation project. If this is ever reintroduced, everything else here stops mattering.

**Contracts live at system level only.** Rejected: subsystem and unit requirements. Interior structure
must churn freely; requirements written against it convert every refactor into paperwork. The absence
of below-system requirements is the process, not an oversight.

**Doc comments replace unit-level requirements, design, and verification.** Interior intent has to be
recorded somewhere, and a doc comment is the only place that costs nothing to keep in sync: it is
colocated, so a refactor edits it in the same file in the same edit, and deleting the code deletes it.
That is the same reasoning that embeds contracts in `{system}.md` rather than a parallel tree, applied
one level down. Rejected on one side: recording interior intent in separate artifacts, which is the
fan-out this process exists to remove. Rejected on the other: mandating a doc comment on *every*
symbol regardless of whether it carries intent. Blanket coverage reads as thorough but produces
signature restatement at scale — and because nothing verifies a doc comment, and agents copy whatever
convention they find in surrounding code, filler propagates and compounds. Once every member has a
comment, having one stops meaning anything. So the boundary is mandatory and compiler-enforced, and
interior members are documented by reason: intent that cannot be recovered from the code, or nothing.

**Contracts are embedded in `docs/architecture/{system}.md`.** Rejected: a parallel `docs/reqstream/`
tree. Parallel trees must be kept in sync forever, and the sync cost is paid on every change. It also
breaks progressive disclosure — a reader descending the tree would have to jump sideways to a YAML
file.

**Each level owns content no other level restates.** Rejected: parent documents summarizing their
children. Summaries create N-way coupling, so every child edit dirties its ancestors, and the tree
becomes the inertial mass this process exists to avoid. The one-file test enforces this.

**Interior tests are disposable; contract tests are durable.** Rejected: treating all tests as
permanent evidence. If every test is permanent, interior refactoring drags a test rewrite behind it
and the code stops moving. This was the second-largest inertia source after artifact fan-out.

**Drift is detected, not gated.** Rejected: blocking documentation gates on every source change. The
`covers` front matter raises advisory flags. Hard gates on file change were the `quality` agent's
central mistake.

**Exactly one mechanically enforced relationship.** Rejected on one side: enforcing nothing, which
lets `*Verified by:*` references rot silently the moment a test is renamed. Rejected on the other:
full ReqStream integration, which needs a tool manifest, a root `requirements.yaml`, a `generated/`
folder, and `.trx` results — pinning the template to .NET. `check-contracts.ps1` does the one check
that matters with no dependencies and no second tree.

**Bounded repairs, no planning phase.** Rejected: the PLANNING → DEVELOPMENT → QUALITY state machine
with three retries. `dispatch` allows one documentation repair and one code repair, because a
documentation finding has to be fixed by `architecture-update` and then still needs an
implementation. A repair that does not clear its finding means the change was misunderstood at the
start, and grinding will not fix it.

## Repository Layout

```text
Anneal/
├── AGENTS.md              This repository's own instructions; pristine plus one section
├── README.md              Project overview
├── docs/user-guide/       This guide
├── .github/
│   ├── agents/            Agent prompts
│   ├── skills/            Procedures loaded on demand
│   ├── standards/         Detailed standards, loaded selectively
│   └── template/          Canonical repository layout and file templates
│       └── AGENTS.pristine.md   Installed to the target root as AGENTS.md
├── retired-payload.txt    Payload files retired by rename or removal, read by install.ps1 -Prune
└── lint.ps1, fix.ps1      Tooling for this repository itself
```

**Anneal is laid out exactly as a repository that has installed Anneal.** That is deliberate: the
process can be maintained using its own agents, and the payload paths need no translation between
this repository and the ones it installs into. The cost is that most root files exist twice — the
working copy here and the pristine copy under `.github/template/` — which the **Template
Stewardship** section of `AGENTS.md` governs.

`.github/` is copied to the root of a consuming repository. `.github/template/` is also fetched by
URL — see the `# Reference Template` section of `AGENTS.md`.

The two live in one repository on one branch, so a change to an agent and the template file it
references lands in one commit. `Agents` split these across `main` and a `template` branch, which
made coordinated changes awkward.

## Provenance

**Carried over unchanged** — `csharp-language.md`, `csharp-testing.md`, `fix.ps1`, the lint
configuration, and `lint-fix.agent.md` with one section retargeted.

**Rewritten** — `coding-principles.md` (the requirement-traceability anti-pattern replaced with
undeclared boundary behavior), `testing-principles.md`, `technical-documentation.md`, and the
`apply`, `architecture-design`, and `template-sync` agents.

**Deliberately dropped** — `requirements-principles.md`, `software-items.md`, `reqstream-usage.md`,
`reviewmark-usage.md`, `design-documentation.md`, `verification-documentation.md`,
`sysml2-modeling.md`; the `implementation`, `planning`, `quality`, and `formal-review` agents; the
SysML2 model, ReviewMark, and the compliance tool manifest.

**Kept** — the Pandoc document collections. Every folder under `docs/` still compiles to a PDF, which
is what makes the architecture tree a deliverable rather than a pile of markdown, and what makes a
loose file in `docs/` a structural error rather than a matter of taste.

New in Anneal: `architecture-documentation.md`, `system-contracts.md`,
`change-classification.md`, the `dispatch`, `architecture-update`, and `tier-check` agents,
`check-contracts.ps1`, and the `check-contracts` skill.

## Changing an Agent

The failure mode to guard against is **prompt bloat**. Agent prompts grow by accretion — every
incident adds a rule, no incident ever removes one — until the agent spends its attention on process
rather than the task. `implementation.agent.md` and `quality.agent.md` are what that looks like at
the end.

Before adding a rule to an agent prompt:

1. **Is it a rule, a procedure, or a standard?** Rules about *how the agent works* belong in the
   prompt. Repeatable *procedures* — run this, interpret the output this way — belong in a skill.
   Rules about *what good output looks like* belong in a standard. Skills and standards are cheap
   because they are loaded only when relevant; prompt text is paid for on every invocation.
2. **Can an existing rule be tightened instead?** Prefer editing to appending.
3. **Does it need to be in `AGENTS.md`?** Only if it applies to every agent. `AGENTS.md` is loaded
   always, so it is the most expensive place to put anything.
4. **What does it replace?** If nothing, be suspicious.

Checklist for a new agent:

- [ ] Front matter: `name`, `description`, `user-invocable`
- [ ] Named for what it owns or does, never for a role. An agent that owns a named artifact takes
      `{artifact}-{verb}` (`architecture-update`, `tier-check`, `lint-fix`); one that acts on
      whatever the work requires takes a bare verb (`dispatch`, `apply`). Check the name against the
      four modes, the other agents, the scripts, and your host's built-in agent names — a name that
      collides with any of them costs more than it saves
- [ ] A stated purpose narrow enough that "when not to use this" is obvious
- [ ] Explicit standards to load, by name
- [ ] A report template with `**Result**` as the first metadata field
- [ ] Listed in `agents/AGENTS.md` under **Agent Delegation Guidelines**
- [ ] Documented in [Reference](reference.md)

Renaming or deleting any payload file — an agent, a standard, or a skill — has one extra obligation:
append its installed path to `retired-payload.txt`. That file is what lets `install.ps1 -Prune` tell
a stale Anneal file from one the repository added itself, and a line never removed from it is what
lets a repository upgrade from any earlier version. Skipping it leaves a superseded agent installed
and selectable, which is worse than not shipping the rename at all.

## Changing a Standard

- [ ] Front matter: `name`, `description`, optional `globs`
- [ ] Every MANDATORY rule states **why** — a rule without a reason gets worked around
- [ ] Quality gates at the end as a checklist
- [ ] Listed in the **Standards Application** matrix in `agents/AGENTS.md`
- [ ] Listed in the standards table in [Reference](reference.md)

Standards should be readable in isolation. Cross-references between them are fine; duplication is
not — the same one-file test applies here.

## Changing a Skill

Skills are procedures loaded on demand. A skill earns its place by **removing** text from an agent
prompt — if it only adds a fourth description of something already covered, it is duplication and
the one-file test fails.

- [ ] Front matter: `name`, `description` — the description states *when* to load it, since that is
      what selection is based on
- [ ] Describes a procedure, not a parameter list. Anything that mirrors a script's own flags will
      drift, because the script and the skill install from different payloads
- [ ] Says what to do when its subject is absent, so a repository that has not been scaffolded gets
      a clear answer instead of an improvised one
- [ ] The agent prompts that used to carry the procedure now reference the skill instead
- [ ] Listed in the **Skills** section of `agents/AGENTS.md`
- [ ] Documented in [Reference](reference.md)

## Changing the Template

- [ ] Add or update the entry in `.github/template/repository-map.md` — `template-sync` uses it as the
      authoritative list
- [ ] `TEMPLATE-DIRECTIVE` comment blocks are instructions to the agent filling the file in, and must
      be removed from the written output
- [ ] Inline `TODO:` values are content placeholders and must always be resolved
- [ ] Keep the template small; it is meant to be a starting point, not a scaffold to grow into

## Changing `check-contracts.ps1`

It parses a specific markdown shape. If you change the clause format in `system-contracts.md`, the
script's regexes must change with it — and vice versa. They are coupled by design, and that coupling
is the price of not having a second artifact tree.

**`test-check-contracts.ps1` must pass before and after any change to it.** The suite builds a
throwaway fixture repository per case in a temp directory — no .NET toolchain needed, because the
`.trx` files are written directly, which is also how a case controls result age and outcome. It
covers the clean case plus every failure the **check-contracts** skill documents.

Adding a case is a dozen lines: build a repository with `New-Repo`, `Set-SystemDoc`,
`Set-ContractTests` and `Set-Trx`, then assert with `Test-Case` on the exit code and the message
substrings. Assert on `-Reject` as well as `-Expect` where a case is about something *not* firing.

A new case earns its place by failing when the behavior it protects is removed. Confirm that: comment
out the line in `check-contracts.ps1` that implements the rule, watch the case fail, then restore it.
A case that passes either way is documentation, not a test — that mistake was made and caught here
once already, in the fixture for comment stripping.

## Verifying a Change to This Repository

```pwsh
pwsh ./fix.ps1
pwsh ./lint.ps1
pwsh ./test-check-contracts.ps1
```

If the change touched anything under `docs/`, build the affected document too — a document that no
longer compiles is broken whether or not it reads well:

```pwsh
./docs/user-guide/build.bat
```

`.github/workflows/build.yml` runs the last two on every push and pull request.

There is no test suite for agent prompts — they are prose, and every defect found in them so far was
found by a person reading. The meaningful verification is running a real change through `dispatch` in
a repository using the process and reading the reports, particularly the tier rationale and the prune
results.

## Known Gaps

- **No release packaging.** `Agents` packaged `src/` into a release zip on each release; the
  equivalent for the payload has not been written. `install.ps1` covers installation from a clone, and
  `.github/template/.github/workflows/build.yml` covers per-repository CI, but Anneal itself does not
  publish an artifact.
- **C# only.** `cpp-language.md` and `cpp-testing.md` were dropped; the process targets .NET
  repositories for now. Re-adding a language means a standards pair, `-TestFilePatterns` and
  `-TestAttributes` defaults in `check-contracts.ps1`, and a result format for the pass check.
- **No graduation path tooling.** The compelling story is evolving fast under Anneal and promoting a
  stabilized repository into the `Agents` process once the design stops moving. Nothing automates
  that today, and the mapping from contracts to system requirements would be the place to start.
- **`check-contracts.ps1` reads only `.trx`.** Clause parsing and test-declaration matching work for
  any language whose files match `-TestFilePatterns` and whose tests are marked by an attribute in
  `-TestAttributes`; the pass and staleness checks need a result parser per format.
- **Drift detection is unimplemented.** `covers` front matter is mandatory and is the intended
  anchor, but nothing computes drift — `tier-check` judges it by reading. Treat reported drift
  verdicts as opinion, not evidence.

## Health Signals

Signs the process is working:

- Most changes are Tier 0 and touch no documentation
- Contract tests survive refactoring untouched
- `architecture-update` reports contain deletions
- Contract clause counts stay in the 5 to 25 range per system

Signs it is degrading back toward the heavyweight process:

- Tier 1 is the common case
- Contract clauses mention class or method names
- Section documents accumulate and are never deleted
- Documentation edits regularly touch two or more levels
- Agents are asked to update artifacts that this process does not have
