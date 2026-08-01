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

Agents2 targets software that must keep **evolving** after the first version ships. It is not a
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

**Documentation is triggered by contract change, not file change.** Rejected: mandatory documentation
updates on any file change, as in `Agents`. That mandate is what converts refactoring into a
documentation project. If this is ever reintroduced, everything else here stops mattering.

**Contracts live at system level only.** Rejected: subsystem and unit requirements. Interior structure
must churn freely; requirements written against it convert every refactor into paperwork. The absence
of below-system requirements is the process, not an oversight.

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

**One repair pass, no planning phase.** Rejected: the PLANNING → DEVELOPMENT → QUALITY state machine
with three retries. If one repair pass is not enough, the change was misunderstood at the start and
grinding will not fix it.

## Repository Layout

```text
Agents2/
├── README.md              Project overview
├── docs/user-guide/       This guide
├── agents/                Drop-in payload for consuming repositories
│   ├── AGENTS.md          Top-level instructions, loaded by every agent
│   └── .github/
│       ├── agents/        Agent prompts
│       └── standards/     Detailed standards, loaded selectively
├── template/              Canonical repository layout and file templates
└── lint.ps1, fix.ps1      Tooling for this repository itself
```

`agents/` is copied to the root of a consuming repository. `template/` is fetched by URL — see the
`# Reference Template` section of `agents/AGENTS.md`.

The two live in one repository on one branch, so a change to an agent and the template file it
references lands in one commit. `Agents` split these across `main` and a `template` branch, which
made coordinated changes awkward.

## Provenance

**Carried over unchanged** — `cpp-language.md`, `cpp-testing.md`, `csharp-language.md`,
`csharp-testing.md`, `fix.ps1`, the lint configuration, and `lint-fix.agent.md` with one section
retargeted.

**Rewritten** — `coding-principles.md` (the requirement-traceability anti-pattern replaced with
undeclared boundary behavior), `testing-principles.md`, `technical-documentation.md`, and the
`developer`, `software-architect`, and `template-sync` agents.

**Deliberately dropped** — `requirements-principles.md`, `software-items.md`, `reqstream-usage.md`,
`reviewmark-usage.md`, `design-documentation.md`, `verification-documentation.md`,
`sysml2-modeling.md`; the `implementation`, `planning`, `quality`, and `formal-review` agents; the
Pandoc document collections, the SysML2 model, ReviewMark, and the compliance tool manifest.

New in Agents2: `architecture-documentation.md`, `system-contracts.md`, `change-tiers.md`, the
`evolve`, `architect`, and `contract-check` agents, and `check-contracts.ps1`.

## Changing an Agent

The failure mode to guard against is **prompt bloat**. Agent prompts grow by accretion — every
incident adds a rule, no incident ever removes one — until the agent spends its attention on process
rather than the task. `implementation.agent.md` and `quality.agent.md` are what that looks like at
the end.

Before adding a rule to an agent prompt:

1. **Is it a rule or a standard?** Rules about *how the agent works* belong in the prompt. Rules about
   *what good output looks like* belong in a standard, loaded selectively. Standards are cheap
   because they are loaded only when relevant; prompt text is paid for on every invocation.
2. **Can an existing rule be tightened instead?** Prefer editing to appending.
3. **Does it need to be in `AGENTS.md`?** Only if it applies to every agent. `AGENTS.md` is loaded
   always, so it is the most expensive place to put anything.
4. **What does it replace?** If nothing, be suspicious.

Checklist for a new agent:

- [ ] Front matter: `name`, `description`, `user-invocable`
- [ ] A stated purpose narrow enough that "when not to use this" is obvious
- [ ] Explicit standards to load, by name
- [ ] A report template with `**Result**` as the first metadata field
- [ ] Listed in `agents/AGENTS.md` under **Agent Delegation Guidelines**
- [ ] Listed in the project `README.md` agent table
- [ ] Documented in [Reference](reference.md)

## Changing a Standard

- [ ] Front matter: `name`, `description`, optional `globs`
- [ ] Every MANDATORY rule states **why** — a rule without a reason gets worked around
- [ ] Quality gates at the end as a checklist
- [ ] Listed in the **Standards Application** matrix in `agents/AGENTS.md`
- [ ] Listed in the standards table in [Reference](reference.md)

Standards should be readable in isolation. Cross-references between them are fine; duplication is
not — the same one-file test applies here.

## Changing the Template

- [ ] Add or update the entry in `template/repository-map.md` — `template-sync` uses it as the
      authoritative list
- [ ] `TEMPLATE-DIRECTIVE` comment blocks are instructions to the agent filling the file in, and must
      be removed from the written output
- [ ] Inline `TODO:` values are content placeholders and must always be resolved
- [ ] Keep the template small; it is meant to be a starting point, not a scaffold to grow into

## Changing `check-contracts.ps1`

It parses a specific markdown shape. If you change the clause format in `system-contracts.md`, the
script's regexes must change with it — and vice versa. They are coupled by design, and that coupling
is the price of not having a second artifact tree.

Test any change against a synthetic repository covering all five paths: valid clause, missing test,
duplicate ID, clause with no test, and `TODO` obligation. Then verify the clean case still exits 0.

## Verifying a Change to This Repository

```pwsh
pwsh ./fix.ps1
pwsh ./lint.ps1
```

There is no test suite for agent prompts — they are prose. The meaningful verification is running a
real change through `evolve` in a repository using the process and reading the reports, particularly
the tier rationale and the prune results.

## Known Gaps

- **No CI workflows.** `Agents` packaged `src/` into a release zip on each release; the equivalent for
  `agents/` has not been written.
- **`template/` has no `.gitattributes`.**
- **No graduation path tooling.** The compelling story is evolving fast under Agents2 and promoting a
  stabilized repository into the `Agents` process once the design stops moving. Nothing automates
  that today, and the mapping from contracts to system requirements would be the place to start.
- **`check-contracts.ps1` reads only `.trx`.** Other test result formats need adding for non-.NET
  repositories to get check 4; checks 1 through 3 already work for any language.

## Health Signals

Signs the process is working:

- Most changes are Tier 0 and touch no documentation
- Contract tests survive refactoring untouched
- `architect` reports contain deletions
- Contract clause counts stay in the 5 to 25 range per system

Signs it is degrading back toward the heavyweight process:

- Tier 1 is the common case
- Contract clauses mention class or method names
- Section documents accumulate and are never deleted
- Documentation edits regularly touch two or more levels
- Agents are asked to update artifacts that this process does not have
