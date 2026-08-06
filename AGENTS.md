# Project Facts

This file is identical in every repository that installs this process — it holds no project-specific
values. What the product is, who owns it, and what it is written in live in `README.md`, level 0 of
the architecture tree. Read it before choosing standards.

# Process Model

This repository follows an **evolutionary** development process. Its central rule:

> Documentation work is triggered by **contract change**, never by file change.

Each system publishes a contract — what consumers outside it may rely on. Interior structure below
that boundary is free to change without documentation cost. That freedom is the point: it is what
lets a design keep moving after the first version ships.

There are deliberately **no requirements, design documents, or verification documents below system
level**. If you find yourself creating one, stop.

# Architecture Tree (Progressive Disclosure)

Documentation is a descent, not a pile. Read only as far down as your task requires.

| Level | File |
| --- | --- |
| 0 | `README.md` |
| 1 | `docs/architecture/overview.md` |
| 2 | `docs/architecture/{system}.md` |
| 3+ | `docs/architecture/{system}/{section}.md` |

`architecture-documentation.md` defines what each level answers and owns; this table is navigation
only. The tree is recursive: a node earns children — a deeper level — only when subdivision earns
its place, so levels appear only when needed.

**Each level owns content no other level restates.** A parent names its children and gives each a
brief scope-and-purpose signpost — enough to decide whether to descend — never a summary of their
contents. Any change should require editing exactly one documentation file.

# Project Structure

```text
├── CONSTRAINTS.md
├── BACKLOG.md
├── docs/
│   └── architecture/
│       ├── overview.md
│       ├── {system}.md
│       └── {system}/
│           └── {section}.md
├── src/
│   └── {System}/
└── test/
    └── {System}.Tests/
        └── Contract/
```

# Classification (ALL Agents)

**Classify before working.** Read `.github/standards/change-classification.md` and classify along both
axes before touching anything. That file is the **single definition** of both — this file names them
and routes; it never restates what they mean.

- **Mode** — `Intake`, `Change`, `Maintenance`, or `Migration`. Decides what you may touch.
- **Tier** — `Tier 0 (Interior)`, `Tier 1 (Contract)`, or `Tier 2 (Structural)`, within Change mode.
  Decides how much documentation moves.

Routing once classified:

| Mode / Tier | Route |
| --- | --- |
| Intake | `dispatch` appends to `BACKLOG.md` or README assumptions, or proposes a constraint; no other agent runs |
| Change, Tier 0 (Interior) | `apply` |
| Change, Tier 1 (Contract) or Tier 2 (Structural) | `architecture-update` → `apply` → `tier-check` |
| Maintenance | `apply`, within a declared bound |
| Migration | `architecture-design` → approved `MIGRATION.md` → staged `apply` work |

Modes and tiers may be raised mid-flight, never silently lowered. An agent never promotes itself
into Migration, and never edits a boundary that forbids its work — that is a stop condition and a
report.

# Test Lifecycles (ALL Agents)

- **Contract tests** are durable. They exercise a system only through its public boundary, are named
  in the clause they verify, and must survive Tier 0 changes untouched.
- **Interior tests** are disposable. Delete or rewrite them freely when the code they cover is
  restructured. They need no clause and no justification.

`testing-principles.md` owns the detail. The clause-to-test link is the **only** mechanically
enforced relationship in this process: `check-contracts.ps1`, run by `lint.ps1`, fails CI when a
clause is not backed by a boundary test that exists and passed, and it fails closed. Run
`pwsh ./build.ps1` before it, or the pass verification has no results to read. `system-contracts.md`
lists what it rejects; the **check-contracts** skill says how to fix each one. Everything else here
is judgement, deliberately.

# Standards Application (ALL Agents Must Follow)

Read the relevant standards from `.github/standards/` before working. Load only what your task
needs — **usually two or three, never more than four** — selecting by the file in scope and the
**Technology** section of `README.md`:

- **Any code**: `coding-principles.md`
- **C# code**: `coding-principles.md`, `csharp-language.md`
- **Any tests**: `testing-principles.md`
- **C# tests**: `testing-principles.md`, `csharp-testing.md`
- **Architecture documents**: `architecture-documentation.md`
- **System contracts**: `system-contracts.md`
- **Classifying work**: `change-classification.md`
- **Any other documentation**: `technical-documentation.md`

**Each rule has exactly one owner.** A standard is the sole definition of its subject; this file and
every agent prompt link to it rather than restating it. A rule stated in two places drifts, and the
copy an agent happens to read first wins. If you find the same rule defined twice, that is a defect
to report — not a choice to make.

# Skills

Skills in `.github/skills/` are loaded on demand, when the situation they describe arises. Prefer
the skill over reconstructing a procedure from memory.

- **check-contracts** — running and interpreting `check-contracts.ps1`: which invocation to use for
  each tier, and how to resolve each failure

# Agent Delegation Guidelines

The default agent handles simple, well-understood tasks directly.

Two agents cannot be delegated to at all: `helper` and `architecture-design` are started by the user
and work by talking to them. When a request would be better talked through than specified, or when
system boundaries need establishing or re-cutting, say so and name the agent — do not attempt the
conversation on the user's behalf, and do not attempt the work instead.

Delegate only for:

- **Any non-trivial change** → `dispatch` (classifies the mode and tier, then routes to the minimum
  process)
- **Scoped implementation with a known approach** → `apply`
- **Contract or architecture tree changes** → `architecture-update`
- **Verifying a completed change against its tier** → `tier-check`
- **Repository layout versus template** → `template-sync`

# Agent Reporting (Specialized Agents Must Follow)

Specialized agents MUST generate a completion report:

1. Save to `.agent-logs/{agent-name}-{subject}-{unique-id}.md` where `{subject}` is a kebab-case
   task summary (max 5 words) and `{unique-id}` is a short unique suffix
2. Start with `**Result**: (SUCCEEDED|FAILED|INCOMPLETE)` as the first metadata field
3. Include the agent-specific report sections defined in each agent's prompt
4. Return the summary to the caller

Result semantics:

- **SUCCEEDED**: work completed and the checks applicable to that agent's scope passed
- **FAILED**: work could not be completed, or checks failed
- **INCOMPLETE**: work cannot proceed without information only the user can provide

# Scope Discipline (ALL Agents Must Follow)

- **Declare scope upfront** — list files to be changed before changing them
- **Minimum necessary changes** — only files directly required by the task
- **No speculative refactoring** — do not refactor adjacent code unless asked
- **No drive-by fixes** — document pre-existing issues in the report; do not fix them
- **No generated file access** — files inside any `generated/` folder are build outputs

# Documentation Discipline (ALL Agents Must Follow)

- **Do not write documentation an agent was not obliged to write.** Documentation nobody asked for is
  future maintenance debt paid by every subsequent change.
- **Never create a requirement below system level.** There is no such artifact in this process.

`architecture-documentation.md` owns the rest — level ownership, and deleting in the same change that
obsoletes.

# Language and Spelling (ALL Agents)

Always use **US English** spelling in all output.

# Reference Template

Resolve the template in this order, and use the first that is available:

1. **`.github/template/`** in this repository — a vendored copy, if present. Prefer it; it needs no
   network and is guaranteed to match the agents installed alongside it.
2. **`template-url`**: `https://github.com/demaconsulting/Anneal/raw/refs/heads/main/.github/template`

- **Repository map**: `{template-root}/repository-map.md`
- **Template files**: `{template-root}/{file-path}` for files described in the map

If neither resolves, report INCOMPLETE and say which — do not guess at template content.

# Key Configuration Files

Scripts, all at the repository root:

- **`fix.ps1`** — applies all auto-fixers silently; always exits 0
- **`lint.ps1`** — runs all lint checks; exits 1 on failure
- **`build.ps1`** — builds the solution, clears `artifacts/tests`, and runs all tests
- **`check-contracts.ps1`** — verifies every contract clause names a boundary test that exists and
  passed

**Protected — do not modify** unless the task explicitly requires it and the change preserves the
documented intent. These carry deliberate configuration, and the four scripts above are protected
too:

- **`.editorconfig`** — code formatting rules
- **`.cspell.yaml`** — spell-check configuration and technical term dictionary
- **`.markdownlint-cli2.yaml`** — markdown formatting rules
- **`.yamllint.yaml`** — YAML formatting configuration

Dependency manifests: **`package.json`** (markdown and spell tooling), **`pip-requirements.txt`**
(yamllint and yamlfix).

# Formatting (After Making Changes)

```pwsh
pwsh ./fix.ps1
```

This applies all available fixers silently and **always exits 0** — agents do not respond to its
output. Full lint compliance is a **pre-PR responsibility**: run `dotnet anneal lint-fix` once before
opening a pull request.

# Template Stewardship (This Repository Only)

Anneal ships the process it uses. This repository is laid out exactly as an installed repository is,
so most root files exist twice: the working copy here, and the pristine copy under
`.github/template/` that downstream repositories receive.

`.github/template/repository-map.md` is the list of paired files — there is no second list. When you
change a file that appears in it, say which of these applies, in your report:

- **Flows to the template** — a generic improvement. Change both copies in the same commit.
- **Adopted from the template** — the template is ahead and this repository is behind. Change both.
- **Deliberately divergent** — the change is specific to Anneal. Change only this copy, and say why.

The third outcome is not a formality. Anneal's `lint.ps1` legitimately differs from the template's,
because it checks contracts against two profiles rather than one and enforces the `AGENTS.md` drift
rule that exists nowhere else; syncing it would break the build. Over-syncing is worse than drift,
because it fails immediately and looks like the safe choice. `CONSTRAINTS.md` owns the standing
condition this protects — *the template must stay valid for a C# product repository* — and this
section does not restate it.

`AGENTS.md` is exempt from that judgement. It carries no per-repository customization, so this copy
must equal `.github/template/AGENTS.pristine.md` exactly, plus this one section — `lint.ps1` checks
it. Any change to the process belongs in the pristine copy, and is mirrored here unchanged.

Anneal has a `src/` and `test/` tree holding the Toolkit and its contract tests, so the Project
Structure above describes this repository as well as a product repository — but only that one system.
Everything else Anneal ships is prose and scripts, which is why root `build.ps1` was **adopted from
the template** and then given project-specific steps: it runs the root-level PowerShell suites
alongside `dotnet test`, and packs the Toolkit so it installs the way a downstream repository
installs it.
